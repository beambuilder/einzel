<#
.SYNOPSIS
  Checks that every assembly and native library an application's .deps.json names is present
  in its directory.

.DESCRIPTION
  For an application that cannot be started on the build machine - the window has no headless
  mode, and four of the six archives are for platforms the runner cannot execute - this is the
  next best thing to starting it: the host resolves each runtime and native asset of the
  application's own target from the application directory, and a missing one is a launch
  failure on a user's machine. Every asset the manifest lists under "runtime" or "native" for
  the target it runs as must be found, either at its listed path or, for a publish layout, by
  file name in the application directory (a satellite resource assembly under its culture
  directory).

.PARAMETER Directory
  The application directory.

.PARAMETER App
  The application names whose <app>.deps.json to check, e.g. einzel-shell.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Directory,
    [Parameter(Mandatory, ValueFromRemainingArguments)] [string[]] $App
)

$ErrorActionPreference = 'Stop'
$failed = $false

foreach ($name in $App) {
    $manifest = Join-Path $Directory "$name.deps.json"
    if (-not (Test-Path -LiteralPath $manifest)) {
        Write-Host "::error::$name.deps.json missing from $Directory"
        $failed = $true
        continue
    }

    $deps = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    $targetName = $deps.runtimeTarget.name
    $target = $deps.targets.$targetName
    if ($null -eq $target) {
        Write-Host "::error::$name.deps.json has no target '$targetName'"
        $failed = $true
        continue
    }

    $checked = 0
    foreach ($library in $target.PSObject.Properties) {
        foreach ($section in 'runtime', 'native', 'resources') {
            $assets = $library.Value.$section
            if ($null -eq $assets) { continue }
            foreach ($asset in $assets.PSObject.Properties) {
                $listed = $asset.Name
                $local = $asset.Value.localPath
                $candidates = @($listed, (Split-Path -Leaf $listed))
                if ($local) { $candidates = @($local) + $candidates }
                if ($section -eq 'resources' -and $asset.Value.locale) {
                    $candidates += (Join-Path $asset.Value.locale (Split-Path -Leaf $listed))
                }
                $checked++
                if (-not ($candidates | Where-Object { Test-Path -LiteralPath (Join-Path $Directory $_) } | Select-Object -First 1)) {
                    Write-Host "::error::$name.deps.json names $listed ($($library.Name)), which is not in $Directory"
                    $failed = $true
                }
            }
        }
    }
    Write-Host "$name.deps.json: $checked assets checked against $Directory"
}

if ($failed) { exit 1 }
