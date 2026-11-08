$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$root = $PSScriptRoot
$distDir = Join-Path $root "dist"
$releaseDir = Join-Path $distDir "NextXLR-v0.1.0-alpha"
$appDir = Join-Path $releaseDir "app"

# 0. Update GitBuildInfo.cs with current Git commit hash
try {
    $commitShort = (git -C $root rev-parse --short HEAD).Trim()
    $commitFull = (git -C $root rev-parse HEAD).Trim()
    Write-Host "Embedding Git Commit $commitShort into build info..."
    $buildInfoFile = Join-Path $root "apps\windows\Services\GitBuildInfo.cs"
    $buildInfoContent = @"
using System;
using System.IO;

namespace NextXLR.Services;

/// <summary>
/// Provides Git commit metadata embedded at build time or resolved dynamically from the repository.
/// </summary>
public static class GitBuildInfo
{
    private static string _commitHash = "$commitShort";
    private static string _fullCommitHash = "$commitFull";

    public static string CommitHash => _commitHash;
    public static string FullCommitHash => _fullCommitHash;

    static GitBuildInfo()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            DirectoryInfo? repoDir = null;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    repoDir = dir;
                    break;
                }
                dir = dir.Parent;
            }

            if (repoDir != null)
            {
                var headFile = Path.Combine(repoDir.FullName, ".git", "HEAD");
                if (File.Exists(headFile))
                {
                    var headContent = File.ReadAllText(headFile).Trim();
                    if (headContent.StartsWith("ref: ", StringComparison.OrdinalIgnoreCase))
                    {
                        var refPath = headContent.Substring(5).Trim();
                        var refFile = Path.Combine(repoDir.FullName, ".git", refPath.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(refFile))
                        {
                            var sha = File.ReadAllText(refFile).Trim();
                            if (sha.Length >= 7)
                            {
                                _fullCommitHash = sha;
                                _commitHash = sha.Substring(0, 7);
                            }
                        }
                    }
                    else if (headContent.Length >= 7)
                    {
                        _fullCommitHash = headContent;
                        _commitHash = headContent.Substring(0, 7);
                    }
                }
            }
        }
        catch
        {
        }
    }
}
"@
    Set-Content -Path $buildInfoFile -Value $buildInfoContent -Encoding UTF8
} catch {
    Write-Warning "Could not update GitBuildInfo from git: $_"
}

# 0b. Compile projects to ensure latest binaries
Write-Host "Building Windows Application (Self-Contained)..."
dotnet build (Join-Path $root "apps\windows\NextXLR.csproj") -c Debug -p:SelfContained=true --no-restore

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

# 3b. Copy LICENSE to app folder
Copy-Item (Join-Path $root "LICENSE") (Join-Path $appDir "LICENSE") -Force -ErrorAction SilentlyContinue

# 4. Create embedded payload.zip for standalone installer
$payloadZip = Join-Path $root "installer\payload.zip"
Write-Host "Packaging payload for embedded standalone installer..."
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Compress-Archive -Path "$appDir\*" -DestinationPath $payloadZip -Force
Write-Host "Payload archive created ($([math]::Round((Get-Item $payloadZip).Length / 1MB, 2)) MB)."

# 5. Build & Publish Standalone Single-File Installer with embedded payload
Write-Host "Building & Publishing Standalone Self-Contained Installer..."
$installerPublishDir = Join-Path $root "installer\bin\Publish"
if (Test-Path $installerPublishDir) { Remove-Item $installerPublishDir -Recurse -Force }
dotnet publish (Join-Path $root "installer\NextXLR.Installer.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --no-restore -o $installerPublishDir

$standaloneInstaller = Join-Path $installerPublishDir "NextXLR-Setup-0.1.0-alpha.exe"
if (-not (Test-Path $standaloneInstaller)) {
    throw "Standalone installer executable was not found at $standaloneInstaller"
}

# Copy standalone installer to dist and release dir
$installerDst = Join-Path $releaseDir "NextXLR-Setup-0.1.0-alpha.exe"
Copy-Item $standaloneInstaller $installerDst -Force
Copy-Item $standaloneInstaller (Join-Path $distDir "NextXLR-Setup-0.1.0-alpha.exe") -Force

# Place standalone uninstaller in app folder
Copy-Item $standaloneInstaller (Join-Path $appDir "NextXLR-Uninstall.exe") -Force

# Cleanup temporary payload.zip
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }

# 6. Copy Scripts and Meta
Copy-Item (Join-Path $root "install.bat") $releaseDir -Force
Copy-Item (Join-Path $root "LICENSE") $releaseDir -Force
Copy-Item (Join-Path $root "README.md") $releaseDir -Force

# 7. Create Zip archive
$zipPath = Join-Path $distDir "NextXLR-v0.1.0-alpha.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Start-Sleep -Milliseconds 800
Write-Host "Compressing to $zipPath..."
$compressed = $false
for ($attempt = 1; $attempt -le 5 -and -not $compressed; $attempt++) {
    try {
        Compress-Archive -Path "$releaseDir\*" -DestinationPath $zipPath -Force
        $compressed = $true
    } catch {
        Write-Host "Waiting for file unlock (attempt $attempt)..."
        Start-Sleep -Seconds 1
    }
}
if (-not $compressed) {
    Compress-Archive -Path "$releaseDir\*" -DestinationPath $zipPath -Force
}

Write-Host "Release package created successfully at: $zipPath"
Copy-Item $installerDst (Join-Path $distDir "NextXLR-Setup-0.1.0-alpha.exe") -Force
Write-Host "Standalone installer copied to dist/NextXLR-Setup-0.1.0-alpha.exe"
