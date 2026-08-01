<#
.SYNOPSIS
Publishes MajikUtils and compiles the installer.

.DESCRIPTION
    pwsh tools/build-release.ps1

Produces publish/MajikUtils (self-contained, ReadyToRun) and dist/MajikUtils-Setup-<version>.exe.

Skips the installer with a warning if Inno Setup is not present, since the published folder is
usable on its own.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Find-Iscc {
    # Inno Setup installs per-user by default, which is *not* under Program Files -- checking only
    # the Program Files paths reports it missing on a machine where it is installed.
    foreach ($key in @(
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1')) {
        try {
            $location = (Get-ItemProperty $key -ErrorAction Stop).InstallLocation
            if ($location) {
                $candidate = Join-Path $location 'ISCC.exe'
                if (Test-Path $candidate) { return $candidate }
            }
        }
        catch { }
    }

    foreach ($candidate in @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $candidate) { return $candidate }
    }

    return $null
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

$exe = Join-Path $publish 'MajikUtils.exe'
if (-not (Test-Path $exe)) { throw "publish produced no exe at $exe" }
Write-Host "  $exe"

$iscc = Find-Iscc
if (-not $iscc) {
    Write-Warning 'Inno Setup not found - skipping installer. The published folder is still usable.'
    return
}

Write-Host "Compiling installer with $iscc ..."
& $iscc (Join-Path $root 'installer\MajikUtils.iss') | Select-String -Pattern 'Successful compile|Error'
if ($LASTEXITCODE -ne 0) { throw 'installer compile failed' }

Get-ChildItem (Join-Path $root 'dist\MajikUtils-Setup-*.exe') |
    Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }, LastWriteTime
