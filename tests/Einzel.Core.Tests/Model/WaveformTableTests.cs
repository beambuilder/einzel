using Einzel.Core.Errors;
using Einzel.Core.Model;

namespace Einzel.Core.Tests.Model;

/// <summary>
/// The waveform table reader: one period of <c>phase,value</c> rows, refused at the
/// pointer of whatever named it when it is not one.
/// </summary>
public sealed class WaveformTableTests
{
    private const string Pointer = "/fields/0/solve/drive/waveform/file";

    [Fact]
    public void ATableWithAHeaderAndOneWithoutReadTheSame()
    {
        var with = WaveformTable.Parse("phase,value\r\n0,1\r\n0.25,0\r\n0.5,-1\r\n0.75,0\r\n", Pointer);
        var without = WaveformTable.Parse("0,1\n0.25, 0\n\n0.5,-1\n 0.75,0", Pointer);

        Assert.Equal([0.0, 0.25, 0.5, 0.75], with.Phases);
        Assert.Equal([1.0, 0.0, -1.0, 0.0], with.Values);
        Assert.Equal(with.Phases, without.Phases);
        Assert.Equal(with.Values, without.Values);
    }

    [Fact]
    public void TwoReadsOfTheSameTextAreEqualAndAChangedValueIsNot()
    {
        var first = WaveformTable.Parse("0,1\n0.25,0\n0.5,-1", Pointer);
        var second = WaveformTable.Parse("phase,value\n0,1\n0.25,0\n0.5,-1\n", Pointer);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, WaveformTable.Parse("0,1\n0.25,0.5\n0.5,-1", Pointer));
        Assert.NotEqual(first, WaveformTable.Parse("0,1\n0.3,0\n0.5,-1", Pointer));
    }

    [Fact]
    public void ExponentsAndAPeriodDecimalPointRead()
    {
        var table = WaveformTable.Parse("0,1e-1\n0.5,-2.5E0\n0.875,.5", Pointer);

        Assert.Equal([0.1, -2.5, 0.5], table.Values);
    }

    [Theory]
    [InlineData("0,1x\n0.25,0\n0.5,-1\n0.75,0", "line 1", "not a phase and a value")]
    [InlineData("0;1\n0.25;0\n0.5;-1", "line 1", "not a phase and a value")]
    [InlineData("0,1\n0.5,0\n0.25,-1", "line 3", "does not follow")]
    [InlineData("0,1\n0.5,0\n0.5,-1", "line 3", "does not follow")]
    [InlineData("0,1\n0.5,0\n1,-1", "line 3", "outside [0, 1)")]
    [InlineData("-0.1,1\n0.5,0\n0.7,-1", "line 1", "outside [0, 1)")]
    [InlineData("phase,value\n0,1\n0.5,0", "2 rows", "at least 3")]
    [InlineData("phase,value\n0,1\nabc,0\n0.7,-1", "line 3", "not a phase and a value")]
    [InlineData("0,1\n0.5;0\n0.7,-1", "line 2", "not a phase and a value")]
    [InlineData("0,1\n0.5,0,2\n0.7,-1", "line 2", "not a phase and a value")]
    [InlineData("0,1\n0.5,NaN\n0.7,-1", "line 2", "not a finite number")]
    [InlineData("0,1\n0.5,Infinity\n0.7,-1", "line 2", "not a finite number")]
    [InlineData("0,1\nNaN,0\n0.7,-1", "line 2", "outside [0, 1)")]
    public void AMalformedTableIsRefusedAtTheGivenPointerNamingTheLine(string text, string where, string what)
    {
        var failure = Assert.Throws<EinzelException>(() => WaveformTable.Parse(text, Pointer));

        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.Equal(Pointer, failure.Error.Path);
        Assert.Contains(where, failure.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains(what, failure.Error.Constraint, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(failure.Error.Suggestion));
    }
}
