# Install-Leaf.ps1
# Extracts Leaf and removes Zone.Identifier to bypass SmartScreen

param(
    [string]$InstallPath = "$env:LOCALAPPDATA\Leaf",
    [switch]$CreateShortcut
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ZipFile = Join-Path $ScriptDir "Leaf.zip"

# Check zip exists
if (-not (Test-Path $ZipFile)) {
    Write-Error "Leaf.zip not found in script directory"
    exit 1
}

# Create install directory
Write-Host "Installing Leaf to: $InstallPath"
if (Test-Path $InstallPath) {
    Remove-Item -Path $InstallPath -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

# Extract
Write-Host "Extracting..."
Expand-Archive -Path $ZipFile -DestinationPath $InstallPath -Force

# Unblock all files (removes Zone.Identifier ADS)
Write-Host "Unblocking files..."
Get-ChildItem -Path $InstallPath -Recurse | Unblock-File

# Optional: Create Start Menu shortcut
if ($CreateShortcut) {
    $StartMenu = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
    $ShortcutPath = Join-Path $StartMenu "Leaf.lnk"
    $ExePath = Join-Path $InstallPath "Leaf.exe"

    $WshShell = New-Object -ComObject WScript.Shell
    $Shortcut = $WshShell.CreateShortcut($ShortcutPath)
    $Shortcut.TargetPath = $ExePath
    $Shortcut.WorkingDirectory = $InstallPath
    $Shortcut.Save()

    Write-Host "Created Start Menu shortcut"
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "Run Leaf from: $InstallPath\Leaf.exe"
