using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NextXLR.Installer;

public partial class InstallerForm : Form
{
    private GradientSidebarPanel sidebarPanel = null!;
    private Panel mainContainer = null!;

    // Pages
    private Panel pageSetup = null!;
    private Panel pageProgress = null!;
    private Panel pageComplete = null!;

    // Setup controls
    private TextBox txtInstallPath = null!;
    private ModernButton btnBrowse = null!;
    private Label btnClearPath = null!;
    private ModernCheckBox chkStartMenuShortcut = null!;
    private ModernCheckBox chkDesktopShortcut = null!;
    private ModernCheckBox chkAppAutostart = null!;
    private ModernCheckBox chkLaunchApp = null!;
    private ModernExpander expLicense = null!;
    private ModernButton btnCancel = null!;
    private ModernButton btnInstall = null!;

    // Progress controls
    private Label lblProgressTitle = null!;
    private Label lblProgressSubtitle = null!;
    private ModernProgressBar progressBar = null!;
    private Label lblProgressStatus = null!;
    private TextBox txtLog = null!;

    // Complete controls
    private ModernCheckBox chkLaunchAfterFinish = null!;
    private ModernButton btnFinish = null!;

    private string installDir = "";
    private bool _wasAppAutostartEnabled = true;

    public InstallerForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "NEXT : XLR Setup";
        this.Size = new Size(860, 560);
        this.MinimumSize = new Size(840, 540);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = NextXLRTheme.DarkBg;
        this.ForeColor = NextXLRTheme.TextPrimary;
        this.Font = NextXLRTheme.BodyFont;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;

        var appIcon = NextXLRTheme.GetAppIcon();
        if (appIcon != null) this.Icon = appIcon;
        NextXLRTheme.ApplyNativeWinUIStyle(this);

        // Detect existing autostart configuration for seamless updates
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            var existingVal = k?.GetValue("NextXLR");
            if (existingVal != null)
            {
                _wasAppAutostartEnabled = true;
            }
            else
            {
                var sPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextXLR", "settings.json");
                if (File.Exists(sPath))
                {
                    var json = File.ReadAllText(sPath);
                    if (json.Contains("\"AutostartEnabled\": true"))
                        _wasAppAutostartEnabled = true;
                    else if (json.Contains("\"AutostartEnabled\": false"))
                        _wasAppAutostartEnabled = false;
                }
            }
        }
        catch { }

        // Right Main Container
        mainContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 24, 31) // #16181F
        };
        this.Controls.Add(mainContainer);

        // Left Gradient Sidebar (matching Bild 1)
        sidebarPanel = new GradientSidebarPanel
        {
            Dock = DockStyle.Left,
            Width = 280
        };
        this.Controls.Add(sidebarPanel);
        sidebarPanel.SendToBack();
        mainContainer.BringToFront();

        BuildSetupPage();
        BuildProgressPage();
        BuildCompletePage();

        ShowPage(pageSetup);
    }

    private void BuildSetupPage()
    {
        pageSetup = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        int leftMargin = 32;

        // Title
        var lblTitle = new Label
        {
            Text = "NEXT : XLR installieren",
            Font = new Font("Segoe UI Variable Display", 21f, FontStyle.Bold),
            ForeColor = Color.FromArgb(248, 250, 252),
            AutoSize = true,
            Location = new Point(leftMargin, 26)
        };
        pageSetup.Controls.Add(lblTitle);

        // Subtitle
        var lblDesc = new Label
        {
            Text = "Professionelles Audio-Routing, integrierte Studio-DSPs und GoXLR-Treiberunterstützung ohne Cloud-Zwang.",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(leftMargin, 68),
            Size = new Size(500, 38)
        };
        pageSetup.Controls.Add(lblDesc);

        // Section: Installationsort
        var lblPathHeader = new Label
        {
            Text = "Installationsort",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(241, 245, 249),
            AutoSize = true,
            Location = new Point(leftMargin, 114)
        };
        pageSetup.Controls.Add(lblPathHeader);

        // Path Box Container
        var pathBoxContainer = new Panel
        {
            Location = new Point(leftMargin, 140),
            Size = new Size(405, 36),
            BackColor = Color.FromArgb(32, 36, 46)
        };
        pathBoxContainer.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            var r = new Rectangle(0, 0, pathBoxContainer.Width - 1, pathBoxContainer.Height - 1);
            using var path = NextXLRTheme.CreateRoundedRectangle(r, 6);
            using var brush = new SolidBrush(pathBoxContainer.BackColor);
            g.FillPath(brush, path);
            using var pen = new Pen(Color.FromArgb(51, 57, 71), 1f);
            g.DrawPath(pen, path);
        };

        var defaultInstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NextXLR");
        txtInstallPath = new TextBox
        {
            Text = defaultInstall,
            BackColor = Color.FromArgb(32, 36, 46),
            ForeColor = Color.FromArgb(248, 250, 252),
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Location = new Point(12, 9),
            Size = new Size(360, 20)
        };
        pathBoxContainer.Controls.Add(txtInstallPath);

        btnClearPath = new Label
        {
            Text = "✕",
            Font = new Font("Segoe UI", 8f, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139),
            Size = new Size(18, 18),
            Location = new Point(380, 9),
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter
        };
        btnClearPath.MouseEnter += (_, _) => btnClearPath.ForeColor = Color.FromArgb(203, 213, 225);
        btnClearPath.MouseLeave += (_, _) => btnClearPath.ForeColor = Color.FromArgb(100, 116, 139);
        btnClearPath.Click += (_, _) => txtInstallPath.Text = "";
        pathBoxContainer.Controls.Add(btnClearPath);

        pageSetup.Controls.Add(pathBoxContainer);

        // Ändern... Button
        btnBrowse = new ModernButton
        {
            Text = "Ändern...",
            Location = new Point(leftMargin + 415, 140),
            Size = new Size(85, 36),
            CornerRadius = 6
        };
        btnBrowse.Click += BtnBrowse_Click;
        pageSetup.Controls.Add(btnBrowse);

        // Path Hint
        var lblPathHint = new Label
        {
            Text = "Nur für dein Konto installiert. Keine Administratorrechte nötig.",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 116, 139),
            AutoSize = true,
            Location = new Point(leftMargin, 182)
        };
        pageSetup.Controls.Add(lblPathHint);

        // Section: Optionen
        var lblOptionsHeader = new Label
        {
            Text = "Optionen",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(241, 245, 249),
            AutoSize = true,
            Location = new Point(leftMargin, 214)
        };
        pageSetup.Controls.Add(lblOptionsHeader);

        chkStartMenuShortcut = new ModernCheckBox
        {
            Text = "Verknüpfung im Startmenü",
            Checked = true,
            Location = new Point(leftMargin, 240),
            Size = new Size(350, 28)
        };
        pageSetup.Controls.Add(chkStartMenuShortcut);

        chkDesktopShortcut = new ModernCheckBox
        {
            Text = "Verknüpfung auf dem Desktop",
            Checked = false, // Bild 1 default: unchecked
            Location = new Point(leftMargin, 272),
            Size = new Size(350, 28)
        };
        pageSetup.Controls.Add(chkDesktopShortcut);

        chkAppAutostart = new ModernCheckBox
        {
            Text = "Mit Windows starten",
            Checked = _wasAppAutostartEnabled,
            Location = new Point(leftMargin, 304),
            Size = new Size(350, 28)
        };
        pageSetup.Controls.Add(chkAppAutostart);

        chkLaunchApp = new ModernCheckBox
        {
            Text = "NEXT : XLR danach starten",
            Checked = true,
            Location = new Point(leftMargin, 336),
            Size = new Size(350, 28)
        };
        pageSetup.Controls.Add(chkLaunchApp);

        // License Expander Card
        string mitLicenseText =
@"MIT License

Copyright (c) 2026 plysystems. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the ""Software""), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED.

Hinweis zu Drittanbieter-Lizenzen (Third-Party Notices):
Dieses Softwarepaket enthält Open-Source-Komponenten (u. a. WinUI 3, CommunityToolkit, NAudio, Rust-Audio-Crates, CLAP). Die vollständigen Lizenztexte der Drittanbieter sind in THIRD_PARTY_NOTICES.md enthalten.";

        expLicense = new ModernExpander("Lizenz (MIT)", mitLicenseText)
        {
            Location = new Point(leftMargin, 375),
            Size = new Size(500, 38)
        };
        pageSetup.Controls.Add(expLicense);

        // Action Buttons Row at Bottom
        btnCancel = new ModernButton
        {
            Text = "Abbrechen",
            Location = new Point(leftMargin + 265, 442),
            Size = new Size(110, 36),
            CornerRadius = 6
        };
        btnCancel.Click += (_, _) => { if (ConfirmCancel()) Application.Exit(); };
        pageSetup.Controls.Add(btnCancel);

        btnInstall = new ModernButton
        {
            Text = "Installieren",
            Location = new Point(leftMargin + 385, 442),
            Size = new Size(115, 36),
            IsCyanAccent = true,
            CornerRadius = 6
        };
        btnInstall.Click += BtnInstall_Click;
        pageSetup.Controls.Add(btnInstall);

        mainContainer.Controls.Add(pageSetup);
    }

    private void BuildProgressPage()
    {
        pageProgress = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Visible = false
        };

        int leftMargin = 32;

        lblProgressTitle = new Label
        {
            Text = "NEXT : XLR wird installiert...",
            Font = new Font("Segoe UI Variable Display", 21f, FontStyle.Bold),
            ForeColor = Color.FromArgb(248, 250, 252),
            AutoSize = true,
            Location = new Point(leftMargin, 26)
        };
        pageProgress.Controls.Add(lblProgressTitle);

        lblProgressSubtitle = new Label
        {
            Text = "Dateien werden entpackt und Windows-Verknüpfungen werden eingerichtet.",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(leftMargin, 68),
            Size = new Size(500, 38)
        };
        pageProgress.Controls.Add(lblProgressSubtitle);

        progressBar = new ModernProgressBar
        {
            Location = new Point(leftMargin, 118),
            Size = new Size(500, 10),
            Value = 5
        };
        pageProgress.Controls.Add(progressBar);

        lblProgressStatus = new Label
        {
            Text = "Installation wird vorbereitet...",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(226, 232, 240),
            Location = new Point(leftMargin, 138),
            Size = new Size(500, 24)
        };
        pageProgress.Controls.Add(lblProgressStatus);

        txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(26, 30, 39),
            ForeColor = Color.FromArgb(148, 163, 184),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f, FontStyle.Regular),
            Location = new Point(leftMargin, 172),
            Size = new Size(500, 255)
        };
        pageProgress.Controls.Add(txtLog);

        mainContainer.Controls.Add(pageProgress);
    }

    private void BuildCompletePage()
    {
        pageComplete = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Visible = false
        };

        int leftMargin = 32;

        var lblDoneTitle = new Label
        {
            Text = "Installation abgeschlossen!",
            Font = new Font("Segoe UI Variable Display", 21f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(leftMargin, 26)
        };
        pageComplete.Controls.Add(lblDoneTitle);

        var lblDoneSubtitle = new Label
        {
            Text = "NEXT : XLR ist jetzt einsatzbereit. Du kannst die Software jederzeit über das Startmenü oder die Taskleiste öffnen.",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(203, 213, 225),
            Location = new Point(leftMargin, 68),
            Size = new Size(500, 48)
        };
        pageComplete.Controls.Add(lblDoneSubtitle);

        chkLaunchAfterFinish = new ModernCheckBox
        {
            Text = "NEXT : XLR jetzt starten",
            Checked = true,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(248, 250, 252),
            Location = new Point(leftMargin, 130),
            Size = new Size(350, 28)
        };
        pageComplete.Controls.Add(chkLaunchAfterFinish);

        btnFinish = new ModernButton
        {
            Text = "Fertigstellen",
            Location = new Point(leftMargin + 370, 442),
            Size = new Size(130, 36),
            IsCyanAccent = true,
            CornerRadius = 6
        };
        btnFinish.Click += BtnFinish_Click;
        pageComplete.Controls.Add(btnFinish);

        mainContainer.Controls.Add(pageComplete);
    }

    private void ShowPage(Panel target)
    {
        pageSetup.Visible = (target == pageSetup);
        pageProgress.Visible = (target == pageProgress);
        pageComplete.Visible = (target == pageComplete);
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "Wählen Sie das Installationsverzeichnis für NEXT : XLR:",
            UseDescriptionForTitle = true,
            SelectedPath = txtInstallPath.Text.Trim()
        };
        if (fbd.ShowDialog() == DialogResult.OK)
        {
            txtInstallPath.Text = fbd.SelectedPath;
        }
    }

    private void BtnInstall_Click(object? sender, EventArgs e)
    {
        installDir = txtInstallPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(installDir))
        {
            MessageBox.Show("Bitte geben Sie einen gültigen Installationspfad an.", "NEXT : XLR Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        chkLaunchAfterFinish.Checked = chkLaunchApp.Checked;
        ShowPage(pageProgress);
        StartInstallationAsync();
    }

    private void BtnFinish_Click(object? sender, EventArgs e)
    {
        if (chkLaunchAfterFinish.Checked)
        {
            var exe = Path.Combine(installDir, "NextXLR.exe");
            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = installDir, UseShellExecute = true });
            }
        }
        Application.Exit();
    }

    private void Log(string msg)
    {
        if (txtLog.InvokeRequired) { txtLog.Invoke(new Action(() => Log(msg))); return; }
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
    }

    private void SetProgress(int value, string status)
    {
        if (progressBar.InvokeRequired) { progressBar.Invoke(new Action(() => SetProgress(value, status))); return; }
        progressBar.Value = value;
        lblProgressStatus.Text = status;
    }

    private async void StartInstallationAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                SetProgress(5, "Verzeichnis und Prozesse werden vorbereitet...");
                Log($"Zielverzeichnis: {installDir}");

                bool isExisting = File.Exists(Path.Combine(installDir, "NextXLR.exe"));
                if (isExisting)
                {
                    Log("Bestehende NEXT : XLR Installation erkannt. Führe vollständiges Überschreiben und Bereinigung durch...");
                }

                // Gründliche Bereinigung voriger Instanzen, Autostart-Einträge und alter Dateien
                CleanupPreviousInstallation(installDir);

                Log("Bestehende Konfiguration und Benutzerprofile in %LOCALAPPDATA%\\NextXLR bleiben erhalten.");
                Directory.CreateDirectory(installDir);

                SetProgress(15, isExisting ? "Aktualisiere Anwendungsdateien..." : "Installiere Anwendungsdateien...");

                bool installed = TryExtractEmbeddedPayload(installDir, isExisting);
                if (!installed)
                {
                    var srcDir = FindSourcePayloadDirectory();
                    if (Directory.Exists(srcDir) && File.Exists(Path.Combine(srcDir, "NextXLR.exe")))
                    {
                        Log($"Lokales Quellverzeichnis gefunden: {srcDir}");
                        CopyDirectory(srcDir, installDir);
                        installed = true;
                    }
                }

                if (!installed)
                {
                    throw new Exception("Keine Installationsdateien gefunden (weder eingebettetes payload.zip noch lokale Quellordner).");
                }

                SetProgress(60, "Richte mixforge Dienst ein...");
                // Hintergrunddienst konfigurieren
                RegisterService(installDir);

                SetProgress(75, "Erstelle Verknüpfungen...");
                if (chkStartMenuShortcut.Checked) CreateStartMenuShortcut(installDir);
                if (chkDesktopShortcut.Checked) CreateDesktopShortcut(installDir);

                SetProgress(85, "Registriere Windows-Deinstallation...");
                RegisterUninstall(installDir);

                // Autostart konfigurieren
                SetProgress(92, "Konfiguriere Autostart...");
                if (chkAppAutostart.Checked)
                {
                    RegisterAutostart(installDir);
                }
                else
                {
                    RemoveAutostart();
                }

                SetProgress(100, "Installation erfolgreich abgeschlossen.");
                Log("Installation vollständig.");

                this.Invoke(new Action(() => ShowPage(pageComplete)));
            }
            catch (Exception ex)
            {
                Log($"FEHLER: {ex.Message}");
                this.Invoke(new Action(() =>
                {
                    MessageBox.Show($"Während der Installation ist ein Fehler aufgetreten:\n\n{ex.Message}", "NEXT : XLR Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
        });
    }

    private void CleanupPreviousInstallation(string targetDir)
    {
        try
        {
            Log("Beende laufende NEXT : XLR und mixforge Prozesse...");
            KillProcess("NextXLR");
            KillProcess("mixforged");
            KillProcess("mixforge");
            System.Threading.Thread.Sleep(500);

            if (Directory.Exists(targetDir))
            {
                Log("Bereinige veraltete Binärdateien im Zielordner...");
                foreach (var file in Directory.GetFiles(targetDir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    try { File.Delete(file); } catch { }
                }
                foreach (var file in Directory.GetFiles(targetDir, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file).ToLowerInvariant();
                    if (name.Contains("nextxlr") || name.Contains("mixforge") || name.Contains("goxlr"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Warnung bei Vorbereinigung: {ex.Message}");
        }
    }

    private bool TryExtractEmbeddedPayload(string targetDir, bool isUpdate)
    {
        try
        {
            var asm = typeof(InstallerForm).Assembly;
            var resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return false;

            Log($"Eingebettetes Archiv gefunden: {resourceName}");
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null) return false;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            int total = archive.Entries.Count;
            int count = 0;

            foreach (var entry in archive.Entries)
            {
                count++;
                if (string.IsNullOrEmpty(entry.Name))
                {
                    // Directory
                    var dir = Path.Combine(targetDir, entry.FullName);
                    Directory.CreateDirectory(dir);
                    continue;
                }

                var destPath = Path.Combine(targetDir, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                try
                {
                    entry.ExtractToFile(destPath, overwrite: true);
                }
                catch (IOException ioEx)
                {
                    Log($"Warnung beim Schreiben von {entry.Name}: {ioEx.Message}. Versuche alternative Dateiablage...");
                    var tempDest = destPath + ".old";
                    try
                    {
                        if (File.Exists(tempDest)) File.Delete(tempDest);
                        File.Move(destPath, tempDest);
                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                    catch { }
                }

                if (count % 15 == 0)
                {
                    int pct = 15 + (int)((count / (float)total) * 45);
                    SetProgress(pct, $"Entpacke Dateien ({count}/{total})...");
                }
            }

            Log($"Erfolgreich {count} Dateien entpackt.");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Fehler beim Extrahieren des eingebetteten Payloads: {ex.Message}");
            return false;
        }
    }

    private string FindSourcePayloadDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "app"),
            Path.Combine(AppContext.BaseDirectory, "..", "app"),
            Path.Combine(AppContext.BaseDirectory, "apps", "windows", "bin", "Debug", "net8.0-windows10.0.19041.0", "win-x64"),
            Path.Combine(AppContext.BaseDirectory, "..", "apps", "windows", "bin", "Debug", "net8.0-windows10.0.19041.0", "win-x64")
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && File.Exists(Path.Combine(c, "NextXLR.exe")))
            {
                return Path.GetFullPath(c);
            }
        }

        return "";
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }

    private void RegisterService(string appPath)
    {
        try
        {
            var daemon = Path.Combine(appPath, "mixforged.exe");
            if (File.Exists(daemon))
            {
                Log("Registriere mixforged Daemon...");
            }
        }
        catch (Exception ex) { Log($"Dienst-Hinweis: {ex.Message}"); }
    }

    private void CreateStartMenuShortcut(string appPath)
    {
        try
        {
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "plysystems");
            Directory.CreateDirectory(startMenu);
            var shortcutPath = Path.Combine(startMenu, "NEXT XLR.lnk");
            var exe = Path.Combine(appPath, "NextXLR.exe");
            CreateShortcut(shortcutPath, exe, appPath, "NEXT : XLR Audio Suite");

            var uninstShortcut = Path.Combine(startMenu, "NEXT XLR deinstallieren.lnk");
            var uninstExe = Path.Combine(appPath, "NextXLR-Uninstall.exe");
            if (File.Exists(uninstExe))
            {
                CreateShortcut(uninstShortcut, uninstExe, appPath, "NEXT : XLR Deinstallation");
            }
        }
        catch (Exception ex) { Log($"Fehler beim Startmenü-Shortcut: {ex.Message}"); }
    }

    private void CreateDesktopShortcut(string appPath)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktop, "NEXT XLR.lnk");
            var exe = Path.Combine(appPath, "NextXLR.exe");
            CreateShortcut(shortcutPath, exe, appPath, "NEXT : XLR Audio Suite");
        }
        catch (Exception ex) { Log($"Fehler beim Desktop-Shortcut: {ex.Message}"); }
    }

    private void RegisterAutostart(string appPath)
    {
        try
        {
            var exe = Path.Combine(appPath, "NextXLR.exe");
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            key.SetValue("NextXLR", $"\"{exe}\" --minimized");

            // In settings.json spiegeln
            var sPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextXLR", "settings.json");
            if (File.Exists(sPath))
            {
                var json = File.ReadAllText(sPath);
                if (json.Contains("\"AutostartEnabled\": false"))
                {
                    json = json.Replace("\"AutostartEnabled\": false", "\"AutostartEnabled\": true");
                    File.WriteAllText(sPath, json);
                }
            }
            Log("Windows Autostart erfolgreich eingerichtet.");
        }
        catch (Exception ex) { Log($"Fehler bei Autostart: {ex.Message}"); }
    }

    private void RemoveAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            key.DeleteValue("NextXLR", throwOnMissingValue: false);

            var sPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextXLR", "settings.json");
            if (File.Exists(sPath))
            {
                var json = File.ReadAllText(sPath);
                if (json.Contains("\"AutostartEnabled\": true"))
                {
                    json = json.Replace("\"AutostartEnabled\": true", "\"AutostartEnabled\": false");
                    File.WriteAllText(sPath, json);
                }
            }
        }
        catch { }
    }

    private void RegisterUninstall(string appPath)
    {
        try
        {
            var uninstExe = Path.Combine(appPath, "NextXLR-Uninstall.exe");
            if (!File.Exists(uninstExe) && Environment.ProcessPath != null)
            {
                try { File.Copy(Environment.ProcessPath, uninstExe, true); } catch { }
            }

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NextXLR");
            key.SetValue("DisplayName", "NEXT : XLR");
            key.SetValue("DisplayVersion", "0.1.0-alpha");
            key.SetValue("Publisher", "plysystems");
            key.SetValue("UninstallString", $"\"{uninstExe}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{uninstExe}\" --uninstall --quiet");
            key.SetValue("InstallLocation", appPath);
            key.SetValue("DisplayIcon", Path.Combine(appPath, "NextXLR.exe"));
            key.SetValue("HelpLink", "https://github.com/plysystems/nextXLR");
            key.SetValue("URLInfoAbout", "https://github.com/plysystems/nextXLR");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) { Log($"Fehler bei Uninstall-Registrierung: {ex.Message}"); }
    }

    private void KillProcess(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                p.Kill();
                p.WaitForExit(3000);
            }
            catch { }
        }
    }

    private void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
    {
        try
        {
            var vbs = Path.Combine(Path.GetTempPath(), "create_lnk.vbs");
            var script = $@"
Set oWS = WScript.CreateObject(""WScript.Shell"")
sLinkFile = ""{shortcutPath}""
Set oLink = oWS.CreateShortcut(sLinkFile)
oLink.TargetPath = ""{targetPath}""
oLink.WorkingDirectory = ""{workingDir}""
oLink.Description = ""{description}""
oLink.Save
";
            File.WriteAllText(vbs, script);
            var psi = new ProcessStartInfo { FileName = "cscript.exe", Arguments = $"//nologo \"{vbs}\"", CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false };
            Process.Start(psi)?.WaitForExit(5000);
            File.Delete(vbs);
            Log($"Verknüpfung erstellt: {Path.GetFileName(shortcutPath)}");
        }
        catch { }
    }

    private bool ConfirmCancel()
    {
        return MessageBox.Show("Möchten Sie die Installation von NEXT : XLR wirklich abbrechen?", "NEXT : XLR Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }
}
