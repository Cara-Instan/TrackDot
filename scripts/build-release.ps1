<#
.SYNOPSIS
    Builds release artifacts for TrackDot: Portable ZIP and Installer EXE.

.PARAMETER Version
    The release version number (e.g. "0.1.0"). Default is "0.1.0".

.PARAMETER Configuration
    Build configuration: "Release" or "Debug". Default is "Release".

.PARAMETER SkipInstaller
    Switch to skip Inno Setup installer creation even if ISCC is present.

.EXAMPLE
    .\scripts\build-release.ps1 -Version "0.1.0"
#>

[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$publishDir = "$repoRoot\artifacts\publish\win-x64"
$releaseDir = "$repoRoot\release"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " TrackDot Release Build Pipeline - v$Version ($Configuration)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Run Unit Tests
Write-Host "`n[1/4] Running unit tests..." -ForegroundColor Yellow
dotnet test "$repoRoot\TrackDot.sln" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Unit tests failed! Aborting release build."
    exit 1
}

# 2. Clean publish directory
Write-Host "`n[2/4] Publishing self-contained win-x64 binary..." -ForegroundColor Yellow
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish "$repoRoot\TrackDot.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed!"
    exit 1
}

if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}

# 3. Create Portable Edition (.zip)
Write-Host "`n[3/4] Packaging Portable Edition..." -ForegroundColor Yellow
$portableMarker = Join-Path $publishDir "portable.dat"
Set-Content -Path $portableMarker -Value "TrackDot Portable Mode Marker" -Encoding UTF8

$portableZipPath = Join-Path $releaseDir "TrackDot-v$Version-Portable-x64.zip"
if (Test-Path $portableZipPath) {
    Remove-Item $portableZipPath -Force
}

Compress-Archive -Path "$publishDir\*" -DestinationPath $portableZipPath -Force
Write-Host "Created Portable Archive: $portableZipPath" -ForegroundColor Green

# Remove temporary portable marker from publish dir after zipping
if (Test-Path $portableMarker) {
    Remove-Item $portableMarker -Force
}

# 4. Build Installer Edition (.exe via Inno Setup)
Write-Host "`n[4/4] Packaging Installer Edition..." -ForegroundColor Yellow

if ($SkipInstaller) {
    Write-Host "Skipping installer compilation (-SkipInstaller specified)." -ForegroundColor Yellow
} else {
    $iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path
    if (-not $iscc) {
        $candidatePaths = @(
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            "C:\Program Files\Inno Setup 6\ISCC.exe"
        )
        foreach ($path in $candidatePaths) {
            if (Test-Path $path) {
                $iscc = $path
                break
            }
        }
    }

    if ($iscc) {
        Write-Host "Found Inno Setup Compiler: $iscc" -ForegroundColor Gray
        $issFile = "$repoRoot\installer\installer.iss"
        & $iscc "/DAppVersion=$Version" $issFile
        if ($LASTEXITCODE -eq 0) {
            $installerExePath = Join-Path $releaseDir "TrackDot-Setup-v$Version-x64.exe"
            Write-Host "Created Installer Executable: $installerExePath" -ForegroundColor Green
        } else {
            Write-Warning "Inno Setup compilation failed with code $LASTEXITCODE."
        }
    } else {
        Write-Warning "Inno Setup Compiler (ISCC.exe) was not found."
        Write-Host "  To build the installer setup executable, please install Inno Setup 6+ from https://jrsoftware.org/isinfo.php" -ForegroundColor Gray
    }
}

# Summary
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " Release Build Summary" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

Get-ChildItem $releaseDir | ForEach-Object {
    $sizeMB = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  * $($_.Name) ($sizeMB MB)" -ForegroundColor White
}
Write-Host "`nBuild complete!" -ForegroundColor Green
