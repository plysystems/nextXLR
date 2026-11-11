using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Win32;

namespace NextXLR.Installer;

/// <summary>
/// Headless runner for in-app updates and unattended silent installations.
/// </summary>
public sealed class SilentInstallRunner
{
    private readonly string _targetDir;
    private readonly int _waitPid;
    private readonly bool _restart;

    public SilentInstallRunner(string? targetDir, int waitPid, bool restart)
    {
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NextXLR");
                var loc = key?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(loc) && Directory.Exists(loc))
                {
                    targetDir = loc;
                }
            }
            catch { }
        }

        _targetDir = !string.IsNullOrWhiteSpace(targetDir)
            ? targetDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NextXLR");
        _waitPid = waitPid;
        _restart = restart;
    }

    public void Execute()
    {
        try
        {
            // 1. Wait for caller process to exit
            if (_waitPid > 0)
            {
                try
                {
                    var p = Process.GetProcessById(_waitPid);
                    p.WaitForExit(4000);
                }
                catch { }
            }

            // 2. Terminate running instances
            KillProcess("NextXLR");
            KillProcess("mixforged");
            KillProcess("mixforge");
            System.Threading.Thread.Sleep(1200);

            // 3. Ensure target directory exists
            Directory.CreateDirectory(_targetDir);

            // 4. Extract embedded payload
            var asm = typeof(SilentInstallRunner).Assembly;
            var resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(Path.Combine(_targetDir, entry.FullName));
                            continue;
                        }

                        var destPath = Path.Combine(_targetDir, entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                        bool written = false;
                        for (int attempt = 1; attempt <= 12 && !written; attempt++)
                        {
                            try
                            {
                                entry.ExtractToFile(destPath, overwrite: true);
                                written = true;
                            }
                            catch (IOException)
                            {
                                if (attempt < 12)
                                {
                                    System.Threading.Thread.Sleep(200);
                                    try
                                    {
                                        var tempDest = destPath + ".old." + Guid.NewGuid().ToString("N");
                                        if (File.Exists(tempDest)) File.Delete(tempDest);
                                        File.Move(destPath, tempDest);
                                        entry.ExtractToFile(destPath, overwrite: true);
                                        written = true;
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
            }

            // 5. Cleanup .old files
            try
            {
                foreach (var old in Directory.GetFiles(_targetDir, "*.old*", SearchOption.AllDirectories))
                {
                    try { File.Delete(old); } catch { }
                }
            }
            catch { }

            // 5b. Trust code signing certificate in TrustedPublisher (silent, no modal popup)
            try
            {
                var certPath = Path.Combine(_targetDir, "plysystems.cer");
                if (File.Exists(certPath))
                {
                    using var x509 = new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath);
                    using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                        System.Security.Cryptography.X509Certificates.StoreName.TrustedPublisher,
                        System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
                    store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);
                    store.Add(x509);
                    store.Close();
                }
            }
            catch { }

            // 6. Register Uninstaller & Registry
            try
            {
                var uninstExe = Path.Combine(_targetDir, "NextXLR-Uninstall.exe");
                var currentExe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                {
                    try { File.Copy(currentExe, uninstExe, true); } catch { }
                }

                using var uninstKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NextXLR");
                uninstKey.SetValue("DisplayName", "NEXT : XLR");
                uninstKey.SetValue("DisplayVersion", "0.1.8.9-alpha");
                uninstKey.SetValue("Publisher", "plysystems");
                uninstKey.SetValue("UninstallString", $"\"{uninstExe}\" --uninstall");
                uninstKey.SetValue("QuietUninstallString", $"\"{uninstExe}\" --uninstall --quiet");
                uninstKey.SetValue("InstallLocation", _targetDir);
                uninstKey.SetValue("DisplayIcon", Path.Combine(_targetDir, "NextXLR.exe"));
                uninstKey.SetValue("HelpLink", "https://github.com/plysystems/nextXLR");
                uninstKey.SetValue("URLInfoAbout", "https://github.com/plysystems/nextXLR");
                uninstKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                uninstKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);

                using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                runKey?.DeleteValue("NextXLR_BackgroundService", throwOnMissingValue: false);
                runKey?.DeleteValue("mixforge", throwOnMissingValue: false);
                runKey?.DeleteValue("mixforged", throwOnMissingValue: false);
            }
            catch { }

            // 7. Restart app if requested
            if (_restart)
            {
                System.Threading.Thread.Sleep(600);
                var exe = Path.Combine(_targetDir, "NextXLR.exe");
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        WorkingDirectory = _targetDir,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                var logFile = Path.Combine(Path.GetTempPath(), "nextxlr_silent_update_error.log");
                File.WriteAllText(logFile, ex.ToString());
            }
            catch { }
        }
    }

    private static void KillProcess(string processName)
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                proc.Kill();
                proc.WaitForExit(2000);
            }
        }
        catch { }
    }
}

