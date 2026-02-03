# Install-Leaf.ps1
# Installs Leaf to the user's local application directory

param(
    [string]$InstallPath = "$env:LOCALAPPDATA\Leaf"
)

$ErrorActionPreference = "Stop"

Write-Host "Leaf Installer" -ForegroundColor Cyan
Write-Host "==============" -ForegroundColor Cyan
Write-Host ""

# Find Leaf.zip in the same directory as this script
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$zipPath = Join-Path $scriptDir "Leaf.zip"

if (-not (Test-Path $zipPath)) {
    Write-Host "Error: Leaf.zip not found in $scriptDir" -ForegroundColor Red
    Write-Host "Please ensure Leaf.zip is in the same folder as this script." -ForegroundColor Yellow
    exit 1
}

Write-Host "Installing to: $InstallPath"

# Stop Leaf if it's running
$leafProcess = Get-Process -Name "Leaf" -ErrorAction SilentlyContinue
if ($leafProcess) {
    Write-Host "Stopping running Leaf instance..."
    $leafProcess | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# Create or clean install directory
if (Test-Path $InstallPath) {
    Write-Host "Removing previous installation..."
    Remove-Item -Path $InstallPath -Recurse -Force
}

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

# Extract zip
Write-Host "Extracting files..."
Expand-Archive -Path $zipPath -DestinationPath $InstallPath -Force

# Create Start Menu shortcut
$startMenuPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenuPath "Leaf.lnk"
$exePath = Join-Path $InstallPath "Leaf.exe"

if (Test-Path $exePath) {
    Write-Host "Creating Start Menu shortcut..."
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $InstallPath
    $shortcut.Description = "Leaf - Git Client"
    $shortcut.Save()
}

# Add to PATH (user-level)
$userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($userPath -notlike "*$InstallPath*") {
    Write-Host "Adding to PATH..."
    [Environment]::SetEnvironmentVariable("PATH", "$userPath;$InstallPath", "User")
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "You can now:"
Write-Host "  - Search for 'Leaf' in the Start Menu"
Write-Host "  - Run 'leaf' from a new terminal window"
Write-Host ""

# Ask to launch
$launch = Read-Host "Launch Leaf now? (Y/n)"
if ($launch -ne "n" -and $launch -ne "N") {
    Start-Process $exePath
}
