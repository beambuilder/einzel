using System.Diagnostics;
using System.Globalization;
using Einzel.Fields.Analytic;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The least-squares harmonic fit: the discrete Fourier coefficients on uniform samples,
/// an exact recovery of a finite series on any samples, and a refusal of what cannot
/// determine the orders asked for.
/// </summary>
public sealed class HarmonicFitTests(ITestOutputHelper output)
{
    private static readonly HarmonicTerm[] Synthetic =
        [new(0, -0.3, 0.0), new(1, 1.0, 0.1), new(3, 0.5, -0.2), new(7, 0.25, 0.4)];

    /// <summary>
    /// A constant and three harmonics with phases, sampled at 64 uniform phases and fitted to
    /// eight orders, come back term for term to 1e-12, every other order is zero, and the
    /// returned series reproduces the samples to rounding.
    /// </summary>
    /// <remarks>
    /// The negative constant pins order zero's sign; the phases pin the atan2 convention,
    /// checked again by evaluating the returned series, which is what a drive would use.
    /// </remarks>
    [Fact]
    public void AFiniteSeriesOnUniformSamplesComesBackTermForTerm()
    {
        var phases = Enumerable.Range(0, 64).Select(i => i / 64.0).ToArray();
        var fit = RfWaveform.Harmonic.Fit(phases, Sample(phases), 8);

        Assert.Equal(Enumerable.Range(0, 9), fit.Waveform.Terms.Select(t => t.Order));
        AssertRecovers(fit, 1e-12);
        Assert.True(fit.ResidualMax < 1e-14, $"residual {fit.ResidualMax:R}");
    }

    /// <summary>
    /// On 40 random phases the same series is recovered to 1e-10: least squares needs no
    /// uniform grid when the data are a finite series of the fitted orders.
    /// </summary>
    [Fact]
    public void AFiniteSeriesOnRandomPhasesIsRecoveredExactly()
    {
        var random = new Random(7);
        var phases = Enumerable.Range(0, 40).Select(_ => random.NextDouble()).ToArray();
        var fit = RfWaveform.Harmonic.Fit(phases, Sample(phases), 8);

        AssertRecovers(fit, 1e-10);
        Assert.True(fit.ResidualMax < 1e-12, $"residual {fit.ResidualMax:R}");
    }

    /// <summary>
    /// On uniform samples of data that are not a short series - random values - the fitted
    /// cos and sin coefficients equal the discrete Fourier sums to 1e-12, and the reported
    /// residuals are the root-mean-square and the largest magnitude of sample minus series,
    /// whichever its sign.
    /// </summary>
    [Fact]
    public void OnUniformSamplesTheFitIsTheDiscreteFourierTransform()
    {
        const int count = 64;
        var random = new Random(11);
        var phases = Enumerable.Range(0, count).Select(i => i / (double)count).ToArray();
        var values = phases.Select(_ => random.NextDouble() - 0.3).ToArray();
        var fit = RfWaveform.Harmonic.Fit(phases, values, 10);

        foreach (var term in fit.Waveform.Terms)
        {
            var k = term.Order;
            var scale = k == 0 ? 1.0 / count : 2.0 / count;
            var cos = scale * Enumerable.Range(0, count).Sum(i => values[i] * double.CosPi(2.0 * k * phases[i]));
            var sin = scale * Enumerable.Range(0, count).Sum(i => values[i] * double.SinPi(2.0 * k * phases[i]));
            Assert.Equal(cos, term.Amplitude * double.CosPi(2.0 * term.Phase), 1e-12);
            Assert.Equal(sin, -term.Amplitude * double.SinPi(2.0 * term.Phase), 1e-12);
        }

        var residuals = Enumerable.Range(0, count).Select(i => values[i] - fit.Waveform.At(phases[i])).ToArray();
        Assert.Equal(Math.Sqrt(residuals.Sum(r => r * r) / count), fit.ResidualRms, 1e-15);
        Assert.Equal(residuals.Max(Math.Abs), fit.ResidualMax, 1e-15);
        Assert.True(fit.ResidualMax > 0.1, "random data are not a ten-order series");

        // A negative spike: the series is -5/16 there (the Dirichlet kernel of order 2 on 16
        // points), so the largest residual is -11/16 in sign and 11/16 in magnitude.
        var spike = RfWaveform.Harmonic.Fit(
            [.. Enumerable.Range(0, 16).Select(i => i / 16.0)], [-1.0, .. new double[15]], 2);
        Assert.Equal(11.0 / 16.0, spike.ResidualMax, 1e-14);
    }

    /// <summary>
    /// A square wave sampled at 1024 cell midpoints and fitted to 15 orders gives the Fourier
    /// series <c>4 / (π n)</c> on the odd orders to 1e-3, as sines (phase -1/4), nothing on
    /// the even ones, and a residual that obeys Parseval: <c>rms² = 1 - ½ Σ A²</c>.
    /// </summary>
    [Fact]
    public void ASquareWaveFitsToItsFourierSeries()
    {
        var (phases, values) = Square(1024);
        var fit = RfWaveform.Harmonic.Fit(phases, values, 15);

        foreach (var term in fit.Waveform.Terms)
        {
            if (term.Order % 2 == 1)
            {
                Assert.Equal(4.0 / (Math.PI * term.Order), term.Amplitude, 1e-3);
                Assert.Equal(-0.25, term.Phase, 1e-12);
            }
            else
            {
                Assert.True(Math.Abs(term.Amplitude) < 1e-12, $"order {term.Order}: {term.Amplitude:R}");
            }
        }

        var power = fit.Waveform.Terms.Sum(t => t.Amplitude * t.Amplitude);
        Assert.Equal(1.0 - (0.5 * power), fit.ResidualRms * fit.ResidualRms, 1e-12);
    }

    /// <summary>
    /// A negative order, fewer than <c>2·orders + 1</c> samples (and exactly that many is
    /// enough), a non-finite sample, unequal lists and phases that cannot tell order one from
    /// a constant are refused.
    /// </summary>
    [Fact]
    public void WhatCannotDetermineTheOrdersIsRefused()
    {
        var phases = Enumerable.Range(0, 10).Select(i => i / 10.0).ToArray();
        var values = Sample(phases);

        Assert.Throws<ArgumentOutOfRangeException>(() => RfWaveform.Harmonic.Fit(phases, values, -1));
        Assert.Throws<ArgumentException>(() => RfWaveform.Harmonic.Fit(phases, values, 1 << 30));
        var tooFew = Assert.Throws<ArgumentException>(() => RfWaveform.Harmonic.Fit(phases, values, 5));
        Assert.Contains("10 samples cannot determine 11 coefficients", tooFew.Message, StringComparison.Ordinal);

        // Exactly 2·orders + 1 samples: a square system, which the fit interpolates.
        var square = RfWaveform.Harmonic.Fit(phases[..9], values[..9], 4);
        Assert.Equal(5, square.Waveform.Terms.Count);
        Assert.True(square.ResidualMax < 1e-12, $"residual {square.ResidualMax:R}");
        Assert.Throws<ArgumentException>(() => RfWaveform.Harmonic.Fit(phases, [.. values[..9], double.NaN], 2));
        Assert.Throws<ArgumentException>(() => RfWaveform.Harmonic.Fit(phases, values[..9], 2));
        Assert.Throws<ArgumentException>(
            () => RfWaveform.Harmonic.Fit([0.25, 0.25, 0.25, 0.75, 0.75], [1.0, 1.0, 1.0, -1.0, -1.0], 1));
    }

    /// <summary>
    /// Every phase is in (-1/2, 1/2] and none is -0, including the round-off terms of a
    /// constant, and an antiphase cosine, which atan2 puts at exactly -1/2.
    /// </summary>
    [Fact]
    public void PhasesAreInTheHalfOpenRangeAndNeverNegativeZero()
    {
        foreach (var level in new[] { 2.0, -0.7, 0.0 })
        {
            var phases = Enumerable.Range(0, 16).Select(i => i / 16.0).ToArray();
            var fit = RfWaveform.Harmonic.Fit(phases, [.. phases.Select(_ => level)], 7);

            Assert.Equal(level, fit.Waveform.Terms[0].Amplitude, 1e-15);

            foreach (var term in fit.Waveform.Terms)
            {
                Assert.InRange(term.Phase, -0.5 + 1e-300, 0.5);
                Assert.False(term.Phase == 0.0 && double.IsNegative(term.Phase), $"order {term.Order} has phase -0");
            }
        }

        // An antiphase cosine on four samples: atan2 gives exactly -1/2 here, written 1/2.
        double[] quarters = [0.0, 0.25, 0.5, 0.75];
        var antiphase = RfWaveform.Harmonic.Fit(quarters, [.. quarters.Select(x => -double.CosPi(2 * x))], 1);
        Assert.Equal(1.0, antiphase.Waveform.Terms[1].Amplitude, 1e-15);
        Assert.Equal(0.5, antiphase.Waveform.Terms[1].Phase);
    }

    /// <summary>
    /// Measurement, not a claim: fit time for 512 and 4096 samples at 15 and 50 orders, and
    /// the square wave's residual at 5, 15 and 50 orders, which falls as orders are added.
    /// </summary>
    [Fact]
    public void FitTimeAndTheSquareWaveResidualAreMeasured()
    {
        foreach (var count in new[] { 512, 4096 })
        {
            var (phases, values) = Square(count);

            foreach (var orders in new[] { 15, 50 })
            {
                RfWaveform.Harmonic.Fit(phases, values, orders);
                var clock = Stopwatch.StartNew();
                RfWaveform.Harmonic.Fit(phases, values, orders);
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"fit {count} samples, {orders} orders: {clock.Elapsed.TotalMilliseconds:F1} ms"));
            }
        }

        var previous = double.PositiveInfinity;

        foreach (var orders in new[] { 5, 15, 50 })
        {
            var (phases, values) = Square(1024);
            var fit = RfWaveform.Harmonic.Fit(phases, values, orders);
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"square wave, 1024 samples, {orders} orders: rms {fit.ResidualRms:G4}, max {fit.ResidualMax:G4}"));
            Assert.True(fit.ResidualRms < previous);
            previous = fit.ResidualRms;
        }
    }

    private static double[] Sample(double[] phases) =>
        [.. phases.Select(new RfWaveform.Harmonic(Synthetic).At)];

    private static (double[] Phases, double[] Values) Square(int count)
    {
        var phases = Enumerable.Range(0, count).Select(i => (i + 0.5) / count).ToArray();
        return (phases, [.. phases.Select(p => p < 0.5 ? 1.0 : -1.0)]);
    }

    private static void AssertRecovers(HarmonicFit fit, double tolerance)
    {
        foreach (var term in fit.Waveform.Terms)
        {
            var expected = Synthetic.Where(s => s.Order == term.Order).ToArray();

            if (expected.Length == 0)
            {
                Assert.True(Math.Abs(term.Amplitude) < tolerance, $"order {term.Order}: {term.Amplitude:R}");
                continue;
            }

            Assert.Equal(expected[0].Amplitude, term.Amplitude, tolerance);
            Assert.Equal(expected[0].Phase, term.Phase, tolerance);
        }
    }
}
