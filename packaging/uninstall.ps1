#requires -Version 5.1
<#
.SYNOPSIS
    Removes the per-user CodexBar installation.

.DESCRIPTION
    Deletes the install directory, Start Menu shortcut, uninstall entry, and autostart Run entry.
    User settings under %APPDATA%\CodexBar are kept unless -RemoveSettings is passed.
#>
[CmdletBinding()]
param(
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\CodexBar'),
    [switch] $RemoveSettings
)

$ErrorActionPreference = 'Stop'

Get-Process -Name 'CodexBar.Windows' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

Remove-ItemProperty `
    -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'CodexBar' `
    -ErrorAction SilentlyContinue
Remove-Item `
    -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexBar' `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue

$shortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\CodexBar.lnk'
Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue

if ($RemoveSettings) {
    Remove-Item -LiteralPath (Join-Path $env:APPDATA 'CodexBar') -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $InstallDir) {
    try {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force
    }
    catch {
        # A script cannot always delete the directory it is running from; finish after exit.
        Start-Process -FilePath 'cmd.exe' `
            -ArgumentList '/c', 'timeout /t 2 >nul & rmdir /s /q "' + $InstallDir + '"' `
            -WindowStyle Hidden
    }
}

Write-Host 'CodexBar removed.'
if (-not $RemoveSettings) {
    Write-Host "Settings in $(Join-Path $env:APPDATA 'CodexBar') were kept."
}
