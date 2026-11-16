<#
.SYNOPSIS
  Merges several `dotnet publish` outputs into one directory, refusing any file two of them
  produced with different content.

.DESCRIPTION
  The release ships einzel, einzel-mcp and einzel-shell side by side in one directory (each
  archive, and the installer). Publishing them one after another INTO that directory lets the
  last publish silently overwrite a shared dependency that an earlier application resolved at
  a different version, while the earlier application's .deps.json still names the version it
  was built against - an application that starts on the build machine and fails, or worse
  behaves differently, on a user's. So each application is published to its own directory and
  merged here: a file present in more than one publish must be byte-identical (SHA-256) in all
  of them, or the merge fails listing the file and which applications disagree.

  The fix for a conflict is to align the version centrally (Directory.Packages.props), never to
  pick one copy here.

.PARAMETER Destination
  The merged directory. Created if absent; must be empty if present.

.PARAMETER Source
  The publish directories, as name=path pairs (the name labels the application in a message),
  e.g. einzel=dist/pub/einzel einzel-mcp=dist/pub/einzel-mcp.

.EXAMPLE
  pwsh tools/publish/merge-publish.ps1 -Destination dist/einzel-win-x64 `
    einzel=dist/pub/einzel einzel-mcp=dist/pub/einzel-mcp einzel-shell=dist/pub/einzel-shell
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Destination,
    [Parameter(Mandatory, ValueFromRemainingArguments)] [string[]] $Source
)

$ErrorActionPreference = 'Stop'

$apps = [ordered]@{}
foreach ($pair in $Source) {
    $name, $path = $pair -split '=', 2
    if (-not $path -or -not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "source '$pair' is not name=<existing directory>"
    }
    $apps[$name] = (Resolve-Path -LiteralPath $path).Path
}

# relative path (case-insensitive: Windows is) -> list of @{ App; Full; Hash }
$seen = @{}
foreach ($name in $apps.Keys) {
    $root = $apps[$name]
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
        $key = $relative.Replace('\', '/').ToLowerInvariant()
        if (-not $seen.ContainsKey($key)) { $seen[$key] = [System.Collections.Generic.List[object]]::new() }
        $seen[$key].Add([pscustomobject]@{
            App = $name; Relative = $relative; Full = $file.FullName
            Hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        })
    }
}

$conflicts = @()
foreach ($key in ($seen.Keys | Sort-Object)) {
    $copies = $seen[$key]
    $hashes = @($copies | Select-Object -ExpandProperty Hash -Unique)
    if ($hashes.Count -gt 1) {
        $groups = $copies | Group-Object Hash | ForEach-Object {
            "[$(($_.Group | Select-Object -ExpandProperty App) -join ', ')] sha256 $($_.Name.Substring(0, 12))"
        }
        $conflicts += "$($copies[0].Relative): " + ($groups -join ' vs ')
    }
}

if ($conflicts.Count -gt 0) {
    foreach ($c in $conflicts) { Write-Host "::error::publishes disagree about $c" }
    Write-Host "Align the package version centrally (Directory.Packages.props) so every application resolves the same file."
    exit 1
}

if (Test-Path -LiteralPath $Destination) {
    if (Get-ChildItem -LiteralPath $Destination -Force | Select-Object -First 1) { throw "$Destination is not empty" }
} else {
    New-Item -ItemType Directory -Path $Destination | Out-Null
}

foreach ($key in $seen.Keys) {
    $first = $seen[$key][0]
    $target = Join-Path $Destination $first.Relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $first.Full -Destination $target
}

$shared = @($seen.Values | Where-Object { $_.Count -gt 1 }).Count
Write-Host "merged $($apps.Count) publishes into $Destination`: $($seen.Count) files, $shared shared and identical"
