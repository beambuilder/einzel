using Einzel.Update;

namespace Einzel.Update.Tests;

/// <summary>
/// What the installer may do to the user's PATH value: edit a string, create an absent one, and
/// leave anything else exactly as it was. Against a stub store - never the real registry.
/// </summary>
public sealed class UserPathEditTests
{
    private const string Install = @"C:\Users\u\AppData\Local\MacCossLab.Einzel\current";

    private sealed class Store(PathValue value, Exception? readFails = null) : IPathValueStore
    {
        public List<(string Value, PathValueKind Kind)> Writes { get; } = [];

        public PathValue Read() => readFails is null ? value : throw readFails;

        public void Write(string written, PathValueKind kind) => Writes.Add((written, kind));
    }

    private static Func<string?, PathEdit> AddInstall => p => PathList.Add(p, Install, static s => s);

    [Fact]
    public void AnAbsentValueIsCreatedAsAnExpandableString()
    {
        var store = new Store(new PathValue(PathValueKind.Absent, null, "absent"));
        var log = new List<string>();

        Assert.True(UserPath.Edit(store, AddInstall, log.Add));

        var (value, kind) = Assert.Single(store.Writes);
        Assert.Equal(Install, value);
        Assert.Equal(PathValueKind.ExpandString, kind);
        Assert.Empty(log);
    }

    [Fact]
    public void AStringValueKeepsEveryEntryAndStaysAString()
    {
        var store = new Store(new PathValue(PathValueKind.String, @"C:\tools;C:\bin", "String"));

        Assert.True(UserPath.Edit(store, AddInstall, _ => { }));

        var (value, kind) = Assert.Single(store.Writes);
        Assert.Equal(@"C:\tools;C:\bin;" + Install, value);
        Assert.Equal(PathValueKind.String, kind);
    }

    [Fact]
    public void AnExpandableValueKeepsItsVariablesAndItsKind()
    {
        var store = new Store(new PathValue(PathValueKind.ExpandString, @"%USERPROFILE%\bin", "ExpandString"));

        Assert.True(UserPath.Edit(store, AddInstall, _ => { }));

        var (value, kind) = Assert.Single(store.Writes);
        Assert.StartsWith(@"%USERPROFILE%\bin;", value, StringComparison.Ordinal);
        Assert.EndsWith(Install, value, StringComparison.Ordinal);
        Assert.Equal(PathValueKind.ExpandString, kind);
    }

    /// <summary>
    /// The defect this guards: a value that is present and not a string read as null, was taken
    /// for absent, and the whole PATH was replaced by the install directory alone.
    /// </summary>
    [Theory]
    [InlineData("MultiString")]
    [InlineData("Binary")]
    [InlineData("DWord")]
    public void AValueThatIsNotAStringIsLeftAloneAndTheReasonLogged(string description)
    {
        var store = new Store(new PathValue(PathValueKind.Other, null, description));
        var log = new List<string>();

        Assert.False(UserPath.Edit(store, AddInstall, log.Add));

        Assert.Empty(store.Writes);
        Assert.Contains(log, l => l.Contains(description, StringComparison.Ordinal)
            && l.Contains("left unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public void AReadThatFailsLeavesThePathAlone()
    {
        var store = new Store(default, new UnauthorizedAccessException("denied"));
        var log = new List<string>();

        Assert.False(UserPath.Edit(store, AddInstall, log.Add));

        Assert.Empty(store.Writes);
        Assert.Contains(log, l => l.Contains("denied", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnchangedValueIsNotRewritten()
    {
        var store = new Store(new PathValue(PathValueKind.ExpandString, Install, "ExpandString"));

        Assert.False(UserPath.Edit(store, AddInstall, _ => { }));

        Assert.Empty(store.Writes);
    }
}
