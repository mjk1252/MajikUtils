<#
.SYNOPSIS
Publishes MajikUtils and packs a Velopack release, ready to attach to a GitHub Release.

.DESCRIPTION
    pwsh tools/build-velopack-release.ps1
    pwsh tools/build-velopack-release.ps1 -Version 1.1.0

Produces publish/MajikUtils (self-contained, ReadyToRun) and a releases/ folder containing
everything a GitHub Release needs: a Setup.exe for first installs, and the delta/full packages
already-installed copies download when UpdateService finds this release.

This is the auto-update path -- see UpdateService.cs in Dock.App. It is separate from
tools/build-release.ps1, which still builds the Inno Setup installer most people will download for
a first install. Run both when cutting a release: Inno Setup for new installs, this for updating
everyone already on a previous version.

-Version defaults to whatever is in src/Dock.App/Dock.App.csproj's <Version> -- the same number
Settings reads back out of the running app -- so the ordinary release is just bumping that one
line. Pass -Version explicitly to publish under a different number without touching the csproj.
installer/MajikUtils.iss's AppVersion is a separate line still, since Inno Setup has no clean way
to read it from here.

Installs the `vpk` dotnet tool on first run if it is not already on the machine.
#>

param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    $csproj = Get-Content (Join-Path $root 'src\Dock.App\Dock.App.csproj') -Raw
    if ($csproj -notmatch '<Version>([\d.]+)</Version>') {
        throw 'No -Version given and no <Version> found in Dock.App.csproj'
    }
    $Version = $Matches[1]
    Write-Host "Using version $Version from Dock.App.csproj"
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be plain semver (e.g. 1.1.0), got '$Version'"
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host 'Installing the vpk tool (dotnet tool install --global vpk)...'
    dotnet tool install --global vpk
    if ($LASTEXITCODE -ne 0) { throw 'vpk install failed' }

    # A tool installed just now is not on PATH in this process yet.
    $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
}

# A running copy holds its own DLLs open and fails the publish partway through.
Get-Process MajikUtils -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$publish = Join-Path $root 'publish\MajikUtils'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host 'Publishing...'
dotnet publish (Join-Path $root 'src\Dock.App\Dock.App.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true `
    -o $publish -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

$releases = Join-Path $root 'releases'

Write-Host "Packing v$Version with vpk..."
vpk pack `
    --packId MajikUtils `
    --packVersion $Version `
    --packDir $publish `
    --mainExe MajikUtils.exe `
    --icon (Join-Path $root 'assets\MajikUtils.ico') `
    --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }

Write-Host ''
Write-Host "Done. Everything in $releases goes onto the GitHub Release for v$Version -- see"
Write-Host 'docs/ARCHITECTURE.md for which files are which and what to do with them.'
Get-ChildItem $releases | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
