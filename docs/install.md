# Installing Einzel

Three ways to get Einzel, by platform. Every release on
[GitHub Releases](https://github.com/maccoss/einzel/releases) carries all of them, with one
`SHA256SUMS.txt` over every file.

## Windows: the installer

Download **`MacCossLab.Einzel-stable-Setup.exe`** and run it. It needs no administrator rights
and asks no questions.

**It is not code-signed yet, so Windows SmartScreen warns the first time**: "Windows protected
your PC". Click **More info**, then **Run anyway**. Check the file against `SHA256SUMS.txt` first
if you want to be sure it is the one the release built:

```powershell
Get-FileHash .\MacCossLab.Einzel-stable-Setup.exe -Algorithm SHA256
```

What it does:

- Installs for **your user only**, into `%LocalAppData%\MacCossLab.Einzel\current`. The
  .NET runtime is included; nothing else has to be installed first.
- Adds a **Start menu and desktop shortcut** to the window (`einzel-shell`).
- Adds that directory to **your user PATH**, so `einzel` and `einzel-mcp` work by name in any
  terminal you open afterwards. A terminal that was already open keeps its old PATH; open a new
  one. Nothing else in your PATH is changed.

**Updates.** Each time the window starts, an installed copy asks GitHub once whether a newer
release exists. If one does, a bar at the top of the window says which version, with
**Download and restart**, **Download only** (installed the next time Einzel starts), **Skip this
version** and **Not now**. Nothing is downloaded until you press one, a restart is refused while
a run is in progress, and nothing checks again until the window is next started. The command
line never contacts the network and never updates itself, so an agent running `einzel` commands
sees one version throughout. If the check cannot reach GitHub the window says nothing; the reason
is written to `%LocalAppData%\MacCossLab.Einzel\einzel-update.log`.

Close running `einzel` commands before restarting into an update: the update replaces the files
they run from.

**Uninstall** from Windows *Settings > Apps > Installed apps > Einzel*. That removes the
directory, the shortcuts and the PATH entry. Projects you created live wherever you put them and
are not touched.

## Windows, Linux and macOS: the portable archives

`einzel-<platform>.zip` (Windows) or `.tar.gz` (Linux, macOS), for x64 and arm64. Unpack anywhere
and run the executables in it; add the directory to your PATH to call them by name. These are
**framework-dependent**: install the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
first (`dotnet --list-runtimes` should list `Microsoft.NETCore.App 10.x`). A portable copy never
checks for updates; to move to a later version, download it and unpack it beside this one.

On Linux the archive is the way to install: there is no installer and no updater. The window
needs a desktop session with OpenGL and the usual desktop libraries; over SSH or on a server use
the command line. On macOS the binaries are not signed or notarized; `PORTABLE.txt` in the
archive says how to get past Gatekeeper.

## From source

See [Build and run](../README.md#build-and-run) in the README.
