# TrackDot Release & Packaging Guide

This document outlines the release process for **TrackDot**, covering building, packaging, and verifying both the **Portable Edition** and **Installer Edition**.

---

## Release Packages Overview

TrackDot supports two official Windows distribution formats (64-bit architecture):

1. **Portable Edition** (`TrackDot-v{version}-Portable-x64.zip`)
   - Standalone compressed directory.
   - **Self-Contained**: Includes bundled .NET 8 Runtime.
   - **Portable Storage**: Contains `portable.dat` marker. Settings are stored locally in `settings.json` and logs in `logs\crash.log` without requiring installation or modifying Windows Registry.

2. **Installer Edition** (`TrackDot-Setup-v{version}-x64.exe`)
   - Interactive Windows Setup wizard built with Inno Setup.
   - Per-user installation (no mandatory UAC prompt required).
   - Configures Start Menu shortcut, Desktop shortcut, and Windows launch-at-startup integration.
   - Clean uninstaller removing app registry entries on uninstall.

---

## Building Releases

### Prerequisites
- .NET 8.0 SDK
- PowerShell 7 or Windows PowerShell
- *(Optional, for Installer setup)* [Inno Setup 6+](https://jrsoftware.org/isinfo.php) installed at standard location (`C:\Program Files (x86)\Inno Setup 6\ISCC.exe`).

### One-Step Automated Build Script

Run the automated release script from the repository root:

```powershell
.\scripts\build-release.ps1 -Version "0.1.0"
```

The script performs the following tasks:
1. Runs all unit tests (`dotnet test`).
2. Publishes the self-contained app binaries to `artifacts/publish/win-x64`.
3. Packages `release/TrackDot-v0.1.0-Portable-x64.zip` (with `portable.dat` marker).
4. Invokes Inno Setup compiler to generate `release/TrackDot-Setup-v0.1.0-x64.exe` (if `ISCC.exe` is found).

---

## Manual Step-by-Step Instructions

### 1. Build & Publish Binaries
```powershell
dotnet publish TrackDot.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

### 2. Package Portable Edition
```powershell
Set-Content -Path "artifacts/publish/win-x64/portable.dat" -Value "TrackDot Portable Mode Marker"
Compress-Archive -Path "artifacts/publish/win-x64/*" -DestinationPath "release/TrackDot-v0.1.0-Portable-x64.zip" -Force
Remove-Item "artifacts/publish/win-x64/portable.dat"
```

### 3. Compile Installer Edition
```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=0.1.0 installer/installer.iss
```

---

## Release Verification Checklist

- [ ] **Unit Tests**: Confirm all unit tests pass clean (`dotnet test`).
- [ ] **Portable Edition**:
  1. Extract ZIP to a test directory.
  2. Launch `TrackDot.exe`.
  3. Verify settings changes (e.g. Pin to Top, Opacity) create and persist in `settings.json`.
  4. Confirm no Registry key (`HKCU\Software\TrackDot`) was populated.
- [ ] **Installer Edition**:
  1. Run `TrackDot-Setup-v0.1.0-x64.exe`.
  2. Verify desktop shortcut and start menu entry work correctly.
  3. Test launch at sign-in toggle in Settings.
  4. Perform Uninstall via Windows Installed Apps and verify cleanup.
