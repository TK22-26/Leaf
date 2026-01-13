# Build-Release.ps1
# Builds self-contained release and creates zip for distribution

$ErrorActionPreference = "Stop"
$PublishDir = ".\publish"
$ZipFile = ".\Leaf.zip"

Write-Host "Building self-contained release..." -ForegroundColor Cyan
dotnet publish src/Leaf/Leaf.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

Write-Host "Creating zip..." -ForegroundColor Cyan
if (Test-Path $ZipFile) { Remove-Item $ZipFile }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipFile

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host "Distribute these files to your team:"
Write-Host "  - Leaf.zip"
Write-Host "  - Install-Leaf.ps1"
Write-Host ""
Write-Host "Team install command:"
Write-Host "  powershell -ExecutionPolicy Bypass -File Install-Leaf.ps1" -ForegroundColor Yellow
