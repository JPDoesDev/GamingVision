<#
.SYNOPSIS
    Builds GamingVision Overlay for release distribution.

.DESCRIPTION
    Creates a clean release build and packages it into a ZIP file
    ready for distribution.

.PARAMETER Version
    Version number for the release (e.g., "1.0.0").
    If not specified, reads from GamingVision.Overlay.csproj.

.EXAMPLE
    .\build-overlay-release.ps1
    .\build-overlay-release.ps1 -Version "1.2.0"
#>

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

# Paths
$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "src\GamingVision.Overlay\GamingVision.Overlay.csproj"
$BuildOutput = Join-Path $ProjectRoot "src\GamingVision.Overlay\bin\Release\net8.0-windows10.0.22621.0\win-x64"
$ReleaseDir = Join-Path $ProjectRoot "releases"

# Get version from .csproj if not specified
if (-not $Version) {
    $csproj = [xml](Get-Content $ProjectFile)
    $Version = $csproj.Project.PropertyGroup.Version
    if (-not $Version) {
        $Version = "0.1.0"
    }
}

$ReleaseName = "GamingVisionOverlay-v$Version-win-x64"
$StagingDir = Join-Path $ReleaseDir $ReleaseName
$ZipFile = Join-Path $ReleaseDir "$ReleaseName.zip"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  GamingVision Overlay Release Builder" -ForegroundColor Cyan
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clean previous builds
Write-Host "[1/5] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path $StagingDir) {
    Remove-Item $StagingDir -Recurse -Force
}
if (Test-Path $ZipFile) {
    Remove-Item $ZipFile -Force
}

# Step 2: Build in Release mode
Write-Host "[2/5] Building Release configuration..." -ForegroundColor Yellow
dotnet clean $ProjectFile -c Release --verbosity quiet
dotnet build $ProjectFile -c Release --verbosity minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Step 3: Create staging directory
Write-Host "[3/5] Staging release files..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

# Copy main application files (exclude unnecessary files)
$excludePatterns = @(
    "*.pdb",            # Debug symbols
    "*.xml",            # XML documentation
    "*.deps.json",      # Dependency metadata (optional, but keeps folder cleaner)
    "*.dev.json",       # Dev config files
    "*.lib",            # Static libraries (not needed at runtime)
    "*.Debug.dll",      # Debug DLLs
    "crash_log.txt",    # Debug logs
    "overlay_log.txt",  # Overlay debug logs
    "*.log",            # Log files
    "*_log.txt"         # Any log txt files
)

Get-ChildItem -Path $BuildOutput -Recurse | Where-Object {
    $file = $_
    $exclude = $false
    foreach ($pattern in $excludePatterns) {
        if ($file.Name -like $pattern) {
            $exclude = $true
            break
        }
    }
    -not $exclude
} | ForEach-Object {
    $relativePath = $_.FullName.Substring($BuildOutput.Length + 1)
    $destPath = Join-Path $StagingDir $relativePath
    $destDir = Split-Path $destPath -Parent

    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }

    if (-not $_.PSIsContainer) {
        Copy-Item $_.FullName -Destination $destPath
    }
}

# Step 4: Create README
Write-Host "[4/5] Adding README..." -ForegroundColor Yellow
$readmeContent = @"
GamingVision Overlay v$Version
==============================

A visual overlay tool for gamers that draws bounding boxes around
detected game objects using YOLO-based computer vision.

QUICK START
-----------
1. Run GamingVisionOverlay.exe
2. Select a game profile from the dropdown
3. Configure overlay groups (what labels to highlight and how)
4. Click "Start Detection" to begin
5. The overlay will draw boxes around detected objects in real-time
6. Click "Stop Detection" or use Alt+O to toggle the overlay

REQUIREMENTS
------------
- Windows 10/11 (64-bit)
- .NET 8.0 Runtime (download from https://dotnet.microsoft.com/download/dotnet/8.0)
- DirectX 12 compatible GPU (recommended for best performance)

FEATURES
--------
- Real-time object detection overlay
- Customizable overlay groups with different colors and styles
- Multiple box styles: Solid, Dashed, Filled, High Contrast
- Per-group confidence thresholds
- Multi-monitor support
- GPU-accelerated inference with DirectML

OVERLAY STYLES
--------------
- Solid: Continuous colored outline
- Dashed: Dashed colored outline
- Filled: Semi-transparent colored fill
- High Contrast: White border with colored fill
- High Contrast Inverted: Black border with colored fill

GAME MODELS
-----------
Game-specific models are stored in the GameModels folder.
Each game has its own subfolder with:
- game_config.json  (settings)
- *.onnx            (detection model)
- *.txt             (label names)

SUPPORT
-------
Report issues: jpdoesdev@gmail.com

LICENSE
-------
See LICENSE file for details. (MIT)
"@

$readmeContent | Out-File -FilePath (Join-Path $StagingDir "README.txt") -Encoding utf8

# Step 5: Create ZIP
Write-Host "[5/5] Creating ZIP archive..." -ForegroundColor Yellow
if (-not (Test-Path $ReleaseDir)) {
    New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null
}

Compress-Archive -Path $StagingDir -DestinationPath $ZipFile -Force

# Summary
$zipSize = (Get-Item $ZipFile).Length / 1MB
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Release ZIP: $ZipFile" -ForegroundColor White
Write-Host "Size: $([math]::Round($zipSize, 2)) MB" -ForegroundColor White
Write-Host ""
Write-Host "Staging folder preserved at:" -ForegroundColor Yellow
Write-Host "  $StagingDir" -ForegroundColor White
Write-Host ""

Write-Host "Press any key to continue..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
