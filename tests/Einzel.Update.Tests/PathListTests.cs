using Einzel.Update;

namespace Einzel.Update.Tests;

/// <summary>The user PATH edit the installer makes, on string values (no registry).</summary>
public sealed class PathListTests
{
    private const string Dir = @"C:\Users\someone\AppData\Local\MacCossLab.Einzel\current";

    [Fact]
    public void AnEmptyPathBecomesTheDirectory()
    {
        Assert.Equal(new PathEdit(Dir, true), PathList.Add(string.Empty, Dir));
        Assert.Equal(new PathEdit(Dir, true), PathList.Add(null, Dir));
    }

    [Fact]
    public void ADirectoryIsAppendedAfterEverythingThere()
    {
        var edit = PathList.Add(@"C:\tools;%USERPROFILE%\bin", Dir);

        Assert.True(edit.Changed);
        Assert.Equal(@"C:\tools;%USERPROFILE%\bin;" + Dir, edit.Value);
    }

    [Fact]
    public void ATrailingSeparatorIsNotDoubled()
    {
        Assert.Equal(@"C:\tools;" + Dir, PathList.Add(@"C:\tools;", Dir).Value);
    }

    [Theory]
    [InlineData(Dir)]
    [InlineData(@"c:\users\SOMEONE\appdata\local\maccosslab.einzel\CURRENT")]
    [InlineData(Dir + @"\")]
    [InlineData(Dir + "/")]
    [InlineData("\"" + Dir + "\"")]
    [InlineData("  " + Dir + "  ")]
    public void AnEntryAlreadyNamingTheDirectoryIsNotDuplicated(string existing)
    {
        var path = @"C:\tools;" + existing + @";C:\other";

        var edit = PathList.Add(path, Dir);

        Assert.False(edit.Changed);
        Assert.Same(path, edit.Value);
    }

    [Fact]
    public void AnEntryWrittenWithAVariableIsRecognisedThroughTheExpander()
    {
        var path = @"C:\tools;%LOCALAPPDATA%\MacCossLab.Einzel\current";

        string Expand(string s) => s.Replace("%LOCALAPPDATA%", @"C:\Users\someone\AppData\Local", StringComparison.Ordinal);

        Assert.False(PathList.Add(path, Dir, Expand).Changed);
        Assert.True(PathList.Add(path, Dir).Changed);
        Assert.Equal(@"C:\tools", PathList.Remove(path, Dir, Expand).Value);
    }

    [Fact]
    public void RemovalLeavesEverythingElseByteIdentical()
    {
        // Odd spacing, an empty segment, a variable, a quoted entry and a trailing separator:
        // none of it is the installer's to tidy.
        const string Before = @" C:\a ;;%USERPROFILE%\bin;""C:\Program Files\x"";";
        var path = Before + Dir + @";C:\z;";

        var edit = PathList.Remove(path, Dir);

        Assert.True(edit.Changed);
        Assert.Equal(Before + @"C:\z;", edit.Value);
    }

    [Fact]
    public void RemovalTakesEveryCopyWhateverItsSpelling()
    {
        var path = Dir + @";C:\a;" + Dir.ToUpperInvariant() + @"\;C:\b";

        Assert.Equal(@"C:\a;C:\b", PathList.Remove(path, Dir).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"C:\a;C:\b")]
    [InlineData(@"C:\Users\someone\AppData\Local\MacCossLab.Einzel")]
    [InlineData(@"C:\Users\someone\AppData\Local\MacCossLab.Einzel\current2")]
    public void RemovingWhatIsNotThereChangesNothing(string path)
    {
        var edit = PathList.Remove(path, Dir);

        Assert.False(edit.Changed);
        Assert.Same(path, edit.Value);
    }

    [Fact]
    public void RemovingTheOnlyEntryLeavesAnEmptyPath()
    {
        Assert.Equal(new PathEdit(string.Empty, true), PathList.Remove(Dir, Dir));
    }

    [Fact]
    public void AddThenRemoveRestoresTheOriginalExactly()
    {
        const string Original = @"C:\a;%X%\b;;C:\c";

        var added = PathList.Add(Original, Dir).Value;

        Assert.Equal(Original, PathList.Remove(added, Dir).Value);
    }

    [Fact]
    public void AnEmptySegmentNeverMatches()
    {
        Assert.False(PathList.Contains(";;", Dir));
    }
}
