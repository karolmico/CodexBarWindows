#requires -Version 5.1
<#
.SYNOPSIS
    Installs CodexBar for the current user without elevation.

.DESCRIPTION
    Copies a published build into %LOCALAPPDATA%\Programs\CodexBar, creates a Start Menu
    shortcut, and registers an uninstall entry under HKCU. Autostart is intentionally not
    touched here: the app owns that toggle (Settings > Start CodexBar with Windows).

.PARAMETER Source
    Directory produced by `dotnet publish` (contains CodexBar.Windows.exe).

.PARAMETER InstallDir
    Per-user install directory. Defaults to %LOCALAPPDATA%\Programs\CodexBar.
#>
[CmdletBinding()]
param(
    [string] $Source = (Join-Path $PSScriptRoot '..\Windows\publish\win-x64'),
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\CodexBar')
)

$ErrorActionPreference = 'Stop'
$exeName = 'CodexBar.Windows.exe'

if (-not (Test-Path -LiteralPath $Source)) {
    throw "Published build not found at '$Source'. Publish first; see docs/windows/DEVELOPMENT.md."
}

$source = (Resolve-Path -LiteralPath $Source).Path
$sourceExe = Join-Path $source $exeName
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Published build has no $exeName in '$source'."
}

Get-Process -Name 'CodexBar.Windows' -ErrorAction SilentlyContinue | Stop-Process -Force

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $InstallDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $InstallDir -Force

$targetExe = Join-Path $InstallDir $exeName
$uninstallScript = Join-Path $InstallDir 'uninstall.ps1'

$programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
New-Item -ItemType Directory -Path $programs -Force | Out-Null
$shortcutPath = Join-Path $programs 'CodexBar.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetExe
$shortcut.WorkingDirectory = $InstallDir
$shortcut.IconLocation = "$targetExe,0"
$shortcut.Description = 'CodexBar usage tray'
$shortcut.Save()

$version = (Get-Item -LiteralPath $targetExe).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = '0.1.0'
}

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexBar'
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'CodexBar'
Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $version
Set-ItemProperty -Path $uninstallKey -Name Publisher -Value 'CodexBar'
Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $InstallDir
Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "$targetExe,0"
Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value (
    "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`"")
Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord

Write-Host "CodexBar $version installed to $InstallDir"
Write-Host "Start it from the Start Menu, or run: $targetExe"
