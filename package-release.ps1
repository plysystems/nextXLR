$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$root = $PSScriptRoot
$distDir = Join-Path $root "dist"
$releaseDir = Join-Path $distDir "NextXLR-v0.1.0-alpha"
$appDir = Join-Path $releaseDir "app"

Write-Host "Creating release package directory structure..."
if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

# 1. Copy App binaries from Windows build
$binDir = Join-Path $root "apps\windows\bin\Debug\net8.0-windows10.0.19041.0\win-x64"
Write-Host "Copying Windows App binaries from $binDir..."
Copy-Item "$binDir\*" -Destination $appDir -Recurse -Exclude "*.pdb","*.old" -Force

# 2. Copy Daemon (mixforged & mixforge alias)
$daemonRelease = Join-Path $root "target\release\mixforged.exe"
if (-not (Test-Path $daemonRelease)) {
    $daemonRelease = Join-Path $root "target\debug\mixforged.exe"
}
Write-Host "Copying daemon from $daemonRelease..."
Copy-Item $daemonRelease (Join-Path $appDir "mixforged.exe") -Force
Copy-Item $daemonRelease (Join-Path $appDir "mixforge.exe") -Force

# 3. Copy Driver files
$drvDir = Join-Path $appDir "Assets\Driver"
if (-not (Test-Path $drvDir)) {
    New-Item -ItemType Directory -Path $drvDir -Force | Out-Null
    Copy-Item (Join-Path $root "apps\windows\Assets\Driver\*") $drvDir -Recurse -Force
}

# 4. Copy Installer setup executable & Uninstaller
$installerSrc = Join-Path $root "installer\bin\Debug\net8.0-windows\NextXLR-Setup-0.1.0-alpha.exe"
$installerDst = Join-Path $releaseDir "NextXLR-Setup-0.1.0-alpha.exe"
Write-Host "Copying installer setup..."
Copy-Item $installerSrc $installerDst -Force
Copy-Item (Join-Path $root "installer\bin\Debug\net8.0-windows\*.dll") $releaseDir -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "installer\bin\Debug\net8.0-windows\*.json") $releaseDir -Force -ErrorAction SilentlyContinue

# Place uninstaller in app folder
Copy-Item $installerSrc (Join-Path $appDir "NextXLR-Uninstall.exe") -Force
Copy-Item (Join-Path $root "installer\bin\Debug\net8.0-windows\*.dll") $appDir -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "installer\bin\Debug\net8.0-windows\*.json") $appDir -Force -ErrorAction SilentlyContinue

# 5. Copy Scripts and Meta
Copy-Item (Join-Path $root "install.bat") $releaseDir -Force
Copy-Item (Join-Path $root "LICENSE") $releaseDir -Force
Copy-Item (Join-Path $root "README.md") $releaseDir -Force

# 6. Create Zip archive
$zipPath = Join-Path $distDir "NextXLR-v0.1.0-alpha.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "Compressing to $zipPath..."
Compress-Archive -Path "$releaseDir\*" -DestinationPath $zipPath -Force

Write-Host "Release package created successfully at: $zipPath"
