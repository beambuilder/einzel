namespace Einzel.Update;

/// <summary>Where an installed copy finds, downloads and applies a newer release.</summary>
/// <remarks>
/// An interface so that <see cref="UpdateWorkflow"/>, which carries every decision, is tested
/// without a network, a GitHub release or an install. <see cref="VelopackFeed"/> is the only
/// real implementation and holds no decisions of its own.
/// </remarks>
public interface IUpdateFeed
{
    /// <summary>The version this copy is, as installed.</summary>
    string CurrentVersion { get; }

    /// <summary>Asks the feed for a release newer than <see cref="CurrentVersion"/>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The newer version, or null when this copy is up to date.</returns>
    Task<string?> FindNewerAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the release <see cref="FindNewerAsync"/> found, and stages it.</summary>
    /// <param name="progress">Percent complete, from 0 to 100.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>A task that completes when the release is staged.</returns>
    Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken);

    /// <summary>Applies the staged release and restarts the window. Does not return on success.</summary>
    void ApplyAndRestart();

    /// <summary>
    /// A release downloaded by an earlier launch and still staged, or null. Velopack's
    /// apply-on-startup is turned off, so such a release waits for an explicit restart.
    /// </summary>
    string? StagedVersion => null;
}

/// <summary>Remembers the one release a person asked not to be told about again.</summary>
public interface ISkippedVersionStore
{
    /// <summary>The skipped version, or null.</summary>
    /// <returns>The version string as it was offered.</returns>
    string? Read();

    /// <summary>Records <paramref name="version"/> as skipped, replacing any earlier one.</summary>
    /// <param name="version">The version string as it was offered.</param>
    void Write(string version);
}
