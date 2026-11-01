using Einzel.Core.Errors;
using Einzel.Core.Model;

namespace Einzel.Core.Tests.Model;

/// <summary>
/// The trace reader: two numeric columns of an oscilloscope export, a counted preamble
/// before them, refused naming the line when a row after the first sample is not one.
/// </summary>
public sealed class WaveformTraceTests
{
    private const string Path = "scope/trace.csv";

    private static readonly string[] Times = ["0", "1e-9", "2E-9"];

    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    [InlineData("\t")]
    public void ACommaASemicolonAndATabReadTheSame(string separator)
    {
        var text = string.Join(separator, "time", "ch1") + "\r\n"
            + string.Join("\n", Times.Select((t, i) => string.Join(separator, t, i - 0.5)));

        var trace = WaveformTrace.Parse(text, Path, 1, 2, 1.0);

        Assert.Equal([0.0, 1e-9, 2e-9], trace.Times);
        Assert.Equal([-0.5, 0.5, 1.5], trace.Values);
        Assert.Equal(1, trace.PreambleLines);
    }

    [Fact]
    public void LeadingLinesThatAreNotSamplesAreCountedAsPreambleAndBlankLinesAreSkipped()
    {
        var text = "Model,MSO54\nRecord Length,3\n\nSample Interval,1e-9\nTIME,CH1\n\n-1e-9,4\n0,5\n\n1e-9,6\n";

        var trace = WaveformTrace.Parse(text, Path, 1, 2, 1.0);

        Assert.Equal(4, trace.PreambleLines);
        Assert.Equal([-1e-9, 0.0, 1e-9], trace.Times);
        Assert.Equal([4.0, 5.0, 6.0], trace.Values);
    }

    [Fact]
    public void ATektronixExportReadsFromColumnsFourAndFiveBesideItsTextColumns()
    {
        var text = "Record Length,4,,-3e-06,187.5\nSample Interval,1E-9,,-2.999e-06,186\n"
            + "Source,CH1,,-2.998e-06,-12.25\n,,,-2.997e-06,0\n";

        var trace = WaveformTrace.Parse(text, Path, 4, 5, 1.0);

        Assert.Equal(0, trace.PreambleLines);
        Assert.Equal([-3e-06, -2.999e-06, -2.998e-06, -2.997e-06], trace.Times);
        Assert.Equal([187.5, 186.0, -12.25, 0.0], trace.Values);
    }

    [Fact]
    public void TheTimeIsScaledToSecondsAndTheValueColumnCanBeAnyColumn()
    {
        var trace = WaveformTrace.Parse("time;ch1;ch2\n-3100;187.5;1\n-3099;186;2\n", Path, 1, 3, 1e-9);

        Assert.Equal([-3100 * 1e-9, -3099 * 1e-9], trace.Times);
        Assert.Equal([1.0, 2.0], trace.Values);
    }

    [Theory]
    [InlineData("time,value\n0,1\n1\n2,3\n", "line 3")]
    [InlineData("0,1\n1,2\n2,abc\n", "line 3")]
    [InlineData("0,1\n1,2\n2,NaN\n", "line 3")]
    [InlineData("0,1\n1;2\n", "line 2")]
    [InlineData("0,1\n2,1\n\n1,1\n", "line 4")]
    [InlineData("0,1\n0,2\n", "line 2")]
    [InlineData("Model,X\n0,1\n", "1 samples")]
    [InlineData("no samples here\n", "0 samples")]
    public void ARowAfterTheFirstSampleThatIsNotOneIsRefusedNamingTheLine(string text, string named)
    {
        var failure = Assert.Throws<EinzelException>(() => WaveformTrace.Parse(text, Path, 1, 2, 1.0));

        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.Equal(Path, failure.Error.Path);
        Assert.Contains(named, failure.Error.Constraint, StringComparison.Ordinal);
    }
}
