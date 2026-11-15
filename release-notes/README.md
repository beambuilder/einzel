# Release notes

One file per release, plus the rolling draft for the next one.

## Versioning

Einzel uses a `YY.feature.patch` convention:

| | |
| --- | --- |
| **YY** | two-digit year — `26` for 2026 |
| **feature** | incremented for each release carrying new capability |
| **patch** | incremented for a fix-only release within the same feature version |

`26.1.0` is the first feature release of 2026, `26.1.1` a fix on top of it, `26.2.0` the
second feature release. **The version is bumped at release time, not during development**,
so the working tree carries the version last released and the commit hash distinguishes
builds within it.

**Why a year rather than a semantic major.** A semantic major number promises something
about compatibility, and the thing this project must not silently break is the *model
format*, which carries its own `schemaVersion` and its own compatibility rule: every
version `SupportedVersions` claims to read is asserted to read, by a test. Overloading a
second compatibility promise onto the package version would put the same claim in two
places, where the two would eventually disagree. The year says when, the format says what
it can read.

**What the version is not.** `solverBehaviourVersion` is separate and deliberately so
(PRJ-3): it changes when the numbers a run produces would change, which is a different
event from a release. `einzel verify` distinguishes them — an edited model or a changed
solver-behaviour version invalidates a stored result, while a different engine build with
identical numerics does not.

## Where the version lives

1. **`Directory.Build.props`** — `<VersionPrefix>`, the single source. Every assembly
   takes it, and `EngineBuild.Version` reads it back through
   `AssemblyInformationalVersionAttribute`, so the CLI, the manifests and
   `einzel doctor` cannot disagree with the build. `einzel --version` prints it with the
   commit appended (`26.2.0+<sha>`).
2. **The git tag** — `v26.2.0`. **The tag must equal `<VersionPrefix>`.** The release workflow
   reads the version out of the tree, refuses a tag that is not `vYY.feature.patch`, and fails
   if the tag and the tree disagree. It never injects a version from the tag: the version is
   bumped in the release PR, and the workflow only verifies it.
3. **This directory** — one notes file per released version.

Prose that quotes a version — sample CLI output in `README.md` and `docs/cli.md`, the
`engineMinimum` example in `docs/extensions.md` — needs it too. Those are examples rather
than sources, and a mechanical bump leaves them contradicting the line above if they are
missed.

## Files

```text
release-notes/
  README.md                       this file
  RELEASE_NOTES_next.md           the working draft, renamed at release time
  RELEASE_NOTES_v26.1.0.md        one per released version
```

One file per release. During development, append to `RELEASE_NOTES_next.md`. It stays
unversioned until the release is cut, so a planned patch can become a feature release when new
capability lands without a file having to be renamed twice.

**The file is the GitHub Release description.** `release.yml` publishes
`release-notes/RELEASE_NOTES_<tag>.md` verbatim as the release body, so write it for the
people reading the Releases page. The rename must happen **before** tagging: the workflow
resolves the path from the tag and fails, after the archives are built, if the file is absent.
To correct a published release later:
`gh release edit <tag> --notes-file release-notes/RELEASE_NOTES_<tag>.md`.

### Content structure

```markdown
# Einzel v<version> release notes

One-sentence summary of the release.

## New Features
## Bug Fixes
## Performance
## Breaking Changes
```

Omit a section that has no entries, **and delete its heading when you rename the draft**: the
draft is seeded with the headings so entries have somewhere to go, and a renamed draft
otherwise arrives carrying the ones nobody filled in. Write in the past tense ("Added",
"Fixed"), lead with what the user sees, include concrete numbers, reference options by their
CLI flag or key, and for a patch release a flat list is enough. (The current draft also
carries a *Known limits* section carried forward from v26.1.0; it is the one heading beyond
the four, kept because it is not release-specific news. Drop or keep it deliberately at
release time.)

## What the assets are

Six archives, one per platform, all **framework-dependent** and not single-file. The .NET 10
runtime is **not** included: the user installs it once (`dotnet --list-runtimes` should show
`Microsoft.NETCore.App 10.x`). Each archive still carries the native apphosts and the native
libraries for its platform.

| asset | built on | packaging |
| --- | --- | --- |
| `einzel-win-x64` | windows-latest | zip |
| `einzel-win-arm64` | windows-latest | zip |
| `einzel-linux-x64` | ubuntu-latest | tar.gz |
| `einzel-linux-arm64` | ubuntu-latest | tar.gz |
| `einzel-osx-x64` | ubuntu-latest (cross-publish) | tar.gz |
| `einzel-osx-arm64` | ubuntu-latest (cross-publish) | tar.gz |

plus `SHA256SUMS.txt`, one file over all six. Each archive holds `einzel` (the command line),
`einzel-mcp` (the live-session server) and `einzel-shell` (the Avalonia window from
`src/Einzel.Shell`), with `LICENSE`, `README.md` and a `PORTABLE.txt` that says what is and is
not included. **One archive per platform rather than a CLI one and a window one**, because
framework-dependent the window adds only the Avalonia and Skia files (a few MB of a 30-38 MB
unpacked directory), and one archive means nobody has to work out which to download.
`src/Einzel.Wpf` is not shipped.

### The Windows installer (Velopack)

Beside the archives, the `installer` job in `release.yml` builds a Windows x64 installer with
[Velopack](https://velopack.io), the tool LabOps installs with:

| asset | what it is |
| --- | --- |
| `MacCossLab.Einzel-stable-Setup.exe` | what a person downloads and runs |
| `MacCossLab.Einzel-<version>-stable-full.nupkg` | the whole install, for an update or a first install |
| `MacCossLab.Einzel-<version>-stable-delta.nupkg` | the change from the previous release (from the second release on) |
| `releases.stable.json`, `RELEASES-stable` | the feed an installed copy's update check reads |

Facts a releaser needs:

- **The package id is `MacCossLab.Einzel`, forever.** It names the install directory
  (`%LocalAppData%\MacCossLab.Einzel`), the uninstall entry and the feed. Change it and every
  existing install is stranded on the old one. It is written in two places, `PACK_ID` in
  `release.yml` and `UpdateFeedInfo.PackageId` in `src/Einzel.Update`, and a test in
  `Einzel.Update.Tests` fails if they differ. The same holds for the channel and for the main
  executable (`einzel-shell.exe`).
- **One channel, `stable`.** LabOps publishes a tag containing `beta` to a `beta` channel;
  Einzel's tag guard refuses any tag that is not digits and dots, so there is no beta channel to
  publish to. Relax the guard first if prereleases are ever wanted, then give the workflow and
  `UpdateFeedInfo` a second channel together.
- **Self-contained**, unlike the archives: the installer carries the .NET runtime, so a person
  installing it needs nothing else. It packs `einzel`, `einzel-mcp` and `einzel-shell` into one
  directory; the workflow checks each keeps its own `.deps.json` and `.runtimeconfig.json` (LabOps
  once shipped an app carrying another program's manifest, and it did not start) and that the CLI
  and MCP server do not depend on Velopack or `Einzel.Update`.
- **Unsigned for now, by decision.** SmartScreen warns on first run and the README says so. Signing
  later is two repository secrets, not a workflow change: `SIGNING_CERT_BASE64` (a base64 `.pfx`)
  and `SIGNING_CERT_PASSWORD`. With them present, `vpk pack` signs every file and the installer;
  without them the step prints a warning.
- **No icon.** The repository has no `.ico`, so `--icon` is not passed and Velopack's default is
  used. Add `--icon <path>` to the pack step when there is one.
- **Deltas.** The job downloads the previous release's full package from GitHub (`vpk download
  github`) as a delta base. On the first release there is none, which is expected: that step is
  allowed to fail, and the release has no delta.
- **One writer.** The installer's files are uploaded as an artifact and published by the same
  `release` job and the same `softprops/action-gh-release` call as the archives, with the same
  notes body, and are included in `SHA256SUMS.txt`. Not `vpk upload github`, which would be a
  second writer to the Release.
- **`vpk` is pinned** (`VPK_VERSION` in the job) to the version of the Velopack library in
  `Directory.Packages.props`. Bump them together.
- `vpk pack` refuses a main executable whose own assembly does not call
  `VelopackApp.Build()...Run()`, which is why that one call is in `src/Einzel.Shell/Program.cs`
  and everything else is in `src/Einzel.Update`.

What the installed copy does is in [`docs/install.md`](../docs/install.md): per-user under
`%LocalAppData%\MacCossLab.Einzel\current`, shortcuts to the window, `einzel` on the user PATH
(added by the install hook, removed by the uninstall hook), and one update check each time the
window starts - never periodic, never from the CLI, nothing downloaded without a click.

**Packing locally, to look at it** (never to publish - DST-5 says nothing distributed is built
locally): publish the three apps self-contained into one directory, then

```powershell
dotnet tool install vpk --version 1.2.161 --tool-path .\tools
.\tools\vpk pack --packId MacCossLab.Einzel --packVersion <version> --packDir <publish dir> `
  --mainExe einzel-shell.exe --packTitle Einzel --channel stable --runtime win-x64 --noPortable `
  --outputDir <out dir>
```

Running the resulting Setup.exe installs into your own profile and edits your PATH; uninstall
from *Settings > Apps* afterwards.

### Archive formats

zip is used for Windows and tar.gz for Linux and macOS, because a zip drops the executable
bit; the workflow runs `chmod +x` before `tar`. The macOS archives are cross-published from
Linux, which is sound because the apphost for a target RID is emitted by the SDK whatever the
build OS.

**The window needs a desktop session with OpenGL**, and on Linux the usual desktop libraries
(X11, fontconfig, ICU). Nothing starts it in CI, so a window that fails on first launch is not
caught before release.

**macOS is unsigned and not notarized.** There is no Apple Developer certificate, so a copy
that came through a browser may be refused by Gatekeeper: right-click and choose Open, or run
`xattr -dr com.apple.quarantine <directory>`. `PORTABLE.txt` in each macOS archive says so.
Signing and notarization would go at the marked comment in `release.yml`; that needs a
Developer ID certificate and notarization credentials as repository secrets.

**What the workflow checks before it publishes:** the tag equals `<VersionPrefix>`; the full
suite passes on Linux and Windows; each archive's contents (the three executables, their
runtime configs, the engine and Avalonia assemblies, and the platform's own Skia and HarfBuzz
native libraries, with no .NET runtime and no WPF in it). Beyond that, the CLI is also
*executed* (`--version`, `doctor`, `init`, `run`, `test`, `schema`) on the two legs whose
runner can run what it built, win-x64 and linux-x64; the other four are checked by contents
alone.

## Cutting a release

The strategy is the one skyline-prism uses: a version tag triggers the workflow, which verifies
the version, runs the suite, builds the archives and the Windows installer, and publishes the
GitHub Release once.

1. **Finalize the notes.** `git mv release-notes/RELEASE_NOTES_next.md
   release-notes/RELEASE_NOTES_v<version>.md`, update its heading, delete every section
   heading with no entries, and create a fresh `RELEASE_NOTES_next.md` seeded with the headings.
2. **Bump the version** in the one source, `<VersionPrefix>` in `Directory.Build.props`, to
   `<version>`, and update the sample output that quotes it (see above).
3. **Run the ship gate locally** (there is no script; these are the commands):
   ```bash
   dotnet build --configuration Release
   dotnet test --configuration Release
   dotnet publish src/Einzel.Cli -c Release -r win-x64 --self-contained false -o /tmp/pub/einzel   # your own RID
   dotnet publish src/Einzel.Mcp   -c Release -r win-x64 --self-contained false -o /tmp/pub/einzel-mcp
   dotnet publish src/Einzel.Shell -c Release -r win-x64 --self-contained false -o /tmp/pub/einzel-shell
   pwsh tools/publish/merge-publish.ps1 -Destination /tmp/ship \
     einzel=/tmp/pub/einzel einzel-mcp=/tmp/pub/einzel-mcp einzel-shell=/tmp/pub/einzel-shell
   /tmp/ship/einzel --version        # must start with <version>; the +sha suffix is the commit
   /tmp/ship/einzel doctor
   ```
4. **Open a PR to `main`**, let CI go green, and **squash-merge** it. Write the squash commit
   message deliberately; it is the permanent record.
5. **Tag the squashed commit on `main`** and push the tag:
   ```bash
   git tag v<version> origin/main
   git push origin v<version>
   ```
   **Pushing the tag builds the archives and the installer and creates the GitHub Release**
   (`release.yml`). Installed copies see the new version the next time their window starts.
   Do not hand-create the release.

To prove the workflow without publishing, run it by hand from the Actions tab (`Release`,
*Run workflow*): it builds and checks every archive and packs the installer (against
`RELEASE_NOTES_next.md`) exactly as a tag does, and publishes nothing.
Prerelease tags are not supported: the workflow refuses any tag that is not digits and dots, and
publishes every release with `prerelease: false`. Relax the tag rule first if they are ever wanted.
