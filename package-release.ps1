$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$root = $PSScriptRoot
$distDir = Join-Path $root "dist"
$version = "0.1.8.1-alpha"
$releaseDir = Join-Path $distDir "NextXLR-v$version"
$appDir = Join-Path $releaseDir "app"
$gitSha = try { (git rev-parse HEAD).Trim() } catch { "release" }

# Code signing helper setup
$pfxFile = Join-Path $root "certificates\plysystems-codesign.pfx"
$codeCert = $null
if (Test-Path $pfxFile) {
    try {
        $codeCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfxFile, "plysystems2026", [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
        Write-Host "Code signing certificate loaded ($($codeCert.Subject))."
    } catch {
        Write-Warning "Could not load code signing certificate: $_"
    }
}

function Sign-Binary([string]$targetFile) {
    if (-not (Test-Path $targetFile) -or -not $codeCert) { return }
    try {
        Set-AuthenticodeSignature -FilePath $targetFile -Certificate $codeCert -HashAlgorithm SHA256 -ErrorAction SilentlyContinue | Out-Null
        Write-Host "  [Code-Signed] $(Split-Path $targetFile -Leaf)"
    } catch {
        Write-Warning "Failed to sign $targetFile : $_"
    }
}

# 0a. Compile daemon in release mode
Write-Host "Building Daemon in Release Mode..."
cargo build --release --bin mixforged

# 0b. Compile projects to ensure latest binaries
Write-Host "Building Windows Application in Release Mode..."
dotnet build (Join-Path $root "apps\windows\NextXLR.csproj") -c Release -p:SourceRevisionId=$gitSha -p:InformationalVersion="$version+$gitSha" --no-restore

Write-Host "Creating release package directory structure..."
if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

# 1. Copy App binaries from Windows build
$binDir = Join-Path $root "apps\windows\bin\Release\net8.0-windows10.0.19041.0\win-x64"
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

# 3b. Copy LICENSE & THIRD_PARTY_NOTICES & Certificate to app folder
Copy-Item (Join-Path $root "LICENSE") (Join-Path $appDir "LICENSE") -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") (Join-Path $appDir "THIRD_PARTY_NOTICES.md") -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "certificates\plysystems.cer") (Join-Path $appDir "plysystems.cer") -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "certificates\plysystems.cer") (Join-Path $releaseDir "plysystems.cer") -Force -ErrorAction SilentlyContinue

# 3c. Code-sign app binaries before embedding into installer payload
Write-Host "Signing core executables and libraries..."
Sign-Binary (Join-Path $appDir "NextXLR.exe")
Sign-Binary (Join-Path $appDir "NextXLR.dll")
Sign-Binary (Join-Path $appDir "mixforged.exe")
Sign-Binary (Join-Path $appDir "mixforge.exe")

# 4. Create embedded payload.zip for standalone installer
$payloadZip = Join-Path $root "installer\payload.zip"
Write-Host "Packaging payload for embedded standalone installer..."
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($appDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Payload archive created ($([math]::Round((Get-Item $payloadZip).Length / 1MB, 2)) MB)."

# 5. Build & Publish Standalone Single-File Installer with embedded payload
Write-Host "Building & Publishing Standalone Self-Contained Installer..."
$installerPublishDir = Join-Path $root "installer\bin\Publish"
if (Test-Path $installerPublishDir) { Remove-Item $installerPublishDir -Recurse -Force }
dotnet publish (Join-Path $root "installer\NextXLR.Installer.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:AssemblyName="NextXLR-Setup-$version" -p:SourceRevisionId=$gitSha -p:InformationalVersion="$version+$gitSha" --no-restore -o $installerPublishDir

$standaloneInstaller = Join-Path $installerPublishDir "NextXLR-Setup-$version.exe"
if (-not (Test-Path $standaloneInstaller)) {
    throw "Standalone installer executable was not found at $standaloneInstaller"
}

# Sign Standalone Installer
Write-Host "Signing standalone setup executable..."
Sign-Binary $standaloneInstaller

# Copy standalone installer to dist and release dir
$installerDst = Join-Path $releaseDir "NextXLR-Setup-$version.exe"
Copy-Item $standaloneInstaller $installerDst -Force
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        Copy-Item $standaloneInstaller (Join-Path $distDir "NextXLR-Setup-$version.exe") -Force
        break
    } catch {
        Start-Sleep -Seconds 1
    }
}

# Place standalone uninstaller in app folder and sign it
$uninstDst = Join-Path $appDir "NextXLR-Uninstall.exe"
Copy-Item $standaloneInstaller $uninstDst -Force
Sign-Binary $uninstDst

# Cleanup temporary payload.zip
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }

# 6. Copy Scripts and Meta
Copy-Item (Join-Path $root "install.bat") $releaseDir -Force
Copy-Item (Join-Path $root "LICENSE") $releaseDir -Force
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $releaseDir -Force
Copy-Item (Join-Path $root "README.md") $releaseDir -Force

# 7. Create Zip archive
$zipPath = Join-Path $distDir "NextXLR-v$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Start-Sleep -Milliseconds 800
Write-Host "Compressing to $zipPath..."
$compressed = $false
for ($attempt = 1; $attempt -le 5 -and -not $compressed; $attempt++) {
    try {
        [System.IO.Compression.ZipFile]::CreateFromDirectory($releaseDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
        $compressed = $true
    } catch {
        Write-Host "Waiting for file unlock (attempt $attempt)..."
        Start-Sleep -Seconds 1
    }
}

Write-Host "Release package created successfully at: $zipPath"
Copy-Item $installerDst (Join-Path $distDir "NextXLR-Setup-$version.exe") -Force
Write-Host "Standalone installer copied to dist/NextXLR-Setup-$version.exe"
