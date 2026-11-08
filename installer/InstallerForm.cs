using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NextXLR.Installer;

public partial class InstallerForm : Form
{
    private Panel headerPanel = null!;
    private Panel contentPanel = null!;
    private Panel footerPanel = null!;
    private Button btnBack = null!;
    private Button btnNext = null!;
    private Button btnCancel = null!;
    private Label lblTitle = null!;
    private Label lblSubtitle = null!;

    // Wizard pages
    private Panel pageWelcome = null!;
    private Panel pageOptions = null!;
    private Panel pageProgress = null!;
    private Panel pageComplete = null!;

    private CheckBox chkLicense = null!;
    private TextBox txtInstallPath = null!;
    private CheckBox chkInstallDriver = null!;
    private CheckBox chkInstallService = null!;
    private CheckBox chkDesktopShortcut = null!;
    private CheckBox chkStartMenuShortcut = null!;
    private CheckBox chkLaunchApp = null!;

    private ProgressBar progressBar = null!;
    private Label lblProgressStatus = null!;
    private TextBox txtLog = null!;

    private int currentPage = 0;
    private string installDir = "";

    public InstallerForm()
    {
        InitializeComponent();
        SetPage(0);
    }

    private void InitializeComponent()
    {
        this.Text = "NEXT : XLR v0.1.0-alpha Setup — plysystems";
        this.Size = new Size(720, 560);
        this.MinimumSize = new Size(700, 540);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(15, 23, 42); // #0F172A
        this.ForeColor = Color.White;
        this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;

        // Header
        headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = Color.FromArgb(30, 41, 59), // #1E293B
            Padding = new Padding(24, 12, 24, 12)
        };

        lblTitle = new Label
        {
            Text = "NEXT : XLR Installation (v0.1.0-alpha)",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 210, 180), // Accent Cyan
            AutoSize = true,
            Location = new Point(24, 14)
        };
        lblSubtitle = new Label
        {
            Text = "Open-Source Audiosteuerung für TC-Helicon GoXLR & GoXLR Mini — plysystems",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Location = new Point(24, 40)
        };
        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(lblSubtitle);

        // Footer
        footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = Color.FromArgb(20, 29, 47),
            Padding = new Padding(24, 14, 24, 14)
        };

        btnCancel = new Button
        {
            Text = "Abbrechen",
            Size = new Size(100, 36),
            Location = new Point(24, 14),
            BackColor = Color.FromArgb(51, 65, 85),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (s, e) => { if (ConfirmCancel()) Application.Exit(); };

        btnBack = new Button
        {
            Text = "< Zurück",
            Size = new Size(100, 36),
            Location = new Point(480, 14),
            BackColor = Color.FromArgb(51, 65, 85),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        btnBack.FlatAppearance.BorderSize = 0;
        btnBack.Click += (s, e) => { if (currentPage > 0) SetPage(currentPage - 1); };

        btnNext = new Button
        {
            Text = "Weiter >",
            Size = new Size(110, 36),
            Location = new Point(590, 14),
            BackColor = Color.FromArgb(0, 210, 180),
            ForeColor = Color.FromArgb(15, 23, 42),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        btnNext.FlatAppearance.BorderSize = 0;
        btnNext.Click += BtnNext_Click;

        footerPanel.Controls.Add(btnCancel);
        footerPanel.Controls.Add(btnBack);
        footerPanel.Controls.Add(btnNext);

        // Content Host
        contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 20, 28, 10),
            BackColor = Color.FromArgb(15, 23, 42)
        };

        this.Controls.Add(contentPanel);
        this.Controls.Add(headerPanel);
        this.Controls.Add(footerPanel);

        // Build Pages
        BuildWelcomePage();
        BuildOptionsPage();
        BuildProgressPage();
        BuildCompletePage();
    }

    private void BuildWelcomePage()
    {
        pageWelcome = new Panel { Dock = DockStyle.Fill, Visible = false };

        var lblWelcome = new Label
        {
            Text = "Lizenzvereinbarung (plysystems)",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(4, 4)
        };

        var txtLicense = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Size = new Size(648, 260),
            Location = new Point(4, 32),
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.FromArgb(226, 232, 240),
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.FixedSingle,
            Text = @"NEXT : XLR License Agreement
Copyright (c) 2026 plysystems. Alle Rechte vorbehalten / All rights reserved.

==============================================================================
LIZENZBEDINGUNGEN:
==============================================================================

1. ERLAUBT:
   - Private Nutzung (vollständig kostenfrei und uneingeschränkt)
   - Kommerzielle Nutzung:
     * Live-Streaming (Twitch, Kick, YouTube etc.)
     * Content Creation (YouTube-Videos, Podcasts, Broadcast-Produktion)
   - Firmeninterne Nutzung in Unternehmen, Studios und Organisationen
   - Änderungen, Anpassungen und Erweiterungen am Quellcode
   - Weiterverteilung der Original- oder modifizierten Software

2. PFLICHT:
   - Der Copyright-Hinweis 'Copyright (c) plysystems' sowie der vollständige 
     Lizenztext müssen in allen Kopien, Distributionen oder wesentlichen 
     Teilen der Software beibehalten werden.

3. HAFTUNGSAUSSCHLUSS:
   - DIE SOFTWARE WIRD OHNE JEDE AUSDRÜCKLICHE ODER STILLSCHWEIGENDE 
     GEWÄHRLEISTUNG BEREITGESTELLT. DIE AUTOREN ODER RECHTEINHABER HAFTEN 
     NICHT FÜR SCHÄDEN AUS DER NUTZUNG DIESER SOFTWARE."
        };

        chkLicense = new CheckBox
        {
            Text = "Ich akzeptiere die Lizenzvereinbarung von plysystems",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 210, 180),
            AutoSize = true,
            Location = new Point(6, 304)
        };
        chkLicense.CheckedChanged += (s, e) => { btnNext.Enabled = chkLicense.Checked; };

        pageWelcome.Controls.Add(lblWelcome);
        pageWelcome.Controls.Add(txtLicense);
        pageWelcome.Controls.Add(chkLicense);
        contentPanel.Controls.Add(pageWelcome);
    }

    private void BuildOptionsPage()
    {
        pageOptions = new Panel { Dock = DockStyle.Fill, Visible = false };

        var lblDir = new Label
        {
            Text = "Installationsverzeichnis:",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(4, 4)
        };

        installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NextXLR");

        txtInstallPath = new TextBox
        {
            Text = installDir,
            Size = new Size(520, 28),
            Location = new Point(4, 30),
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnBrowse = new Button
        {
            Text = "Durchsuchen...",
            Size = new Size(116, 28),
            Location = new Point(532, 29),
            BackColor = Color.FromArgb(51, 65, 85),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnBrowse.FlatAppearance.BorderSize = 0;
        btnBrowse.Click += (s, e) =>
        {
            using var dlg = new FolderBrowserDialog();
            dlg.SelectedPath = txtInstallPath.Text;
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                txtInstallPath.Text = dlg.SelectedPath;
                installDir = dlg.SelectedPath;
            }
        };

        var lblComponents = new Label
        {
            Text = "Komponenten & Einstellungen:",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(4, 76)
        };

        chkInstallDriver = new CheckBox
        {
            Text = "TC-Helicon GoXLR USB-Audiotreiber installieren (empfohlen für 8 Audiokanäle)",
            Checked = true,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(6, 106)
        };

        chkInstallService = new CheckBox
        {
            Text = "mixforge Hintergrunddienst einrichten (Automatischer Start mit Windows)",
            Checked = true,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(6, 138)
        };

        chkDesktopShortcut = new CheckBox
        {
            Text = "Desktop-Verknüpfung erstellen",
            Checked = true,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(6, 170)
        };

        chkStartMenuShortcut = new CheckBox
        {
            Text = "Startmenü-Eintrag erstellen",
            Checked = true,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(6, 202)
        };

        pageOptions.Controls.Add(lblDir);
        pageOptions.Controls.Add(txtInstallPath);
        pageOptions.Controls.Add(btnBrowse);
        pageOptions.Controls.Add(lblComponents);
        pageOptions.Controls.Add(chkInstallDriver);
        pageOptions.Controls.Add(chkInstallService);
        pageOptions.Controls.Add(chkDesktopShortcut);
        pageOptions.Controls.Add(chkStartMenuShortcut);
        contentPanel.Controls.Add(pageOptions);
    }

    private void BuildProgressPage()
    {
        pageProgress = new Panel { Dock = DockStyle.Fill, Visible = false };

        lblProgressStatus = new Label
        {
            Text = "Bereite Installation vor...",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 210, 180),
            AutoSize = true,
            Location = new Point(4, 10)
        };

        progressBar = new ProgressBar
        {
            Size = new Size(648, 22),
            Location = new Point(4, 38),
            Style = ProgressBarStyle.Continuous,
            Value = 0
        };

        txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Size = new Size(648, 240),
            Location = new Point(4, 72),
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Consolas", 8.5f),
            BorderStyle = BorderStyle.FixedSingle
        };

        pageProgress.Controls.Add(lblProgressStatus);
        pageProgress.Controls.Add(progressBar);
        pageProgress.Controls.Add(txtLog);
        contentPanel.Controls.Add(pageProgress);
    }

    private void BuildCompletePage()
    {
        pageComplete = new Panel { Dock = DockStyle.Fill, Visible = false };

        var lblSuccess = new Label
        {
            Text = "Installation erfolgreich abgeschlossen!",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(16, 185, 129), // Emerald Green
            AutoSize = true,
            Location = new Point(4, 16)
        };

        var lblDesc = new Label
        {
            Text = "NEXT : XLR v0.1.0-alpha wurde erfolgreich auf Ihrem System eingerichtet.\n" +
                   "Der mixforge Hintergrunddienst ist aktiv und steuert Ihre GoXLR Hardware.",
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(226, 232, 240),
            AutoSize = true,
            Location = new Point(4, 56)
        };

        chkLaunchApp = new CheckBox
        {
            Text = "NEXT : XLR jetzt starten",
            Checked = true,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 210, 180),
            AutoSize = true,
            Location = new Point(6, 120)
        };

        pageComplete.Controls.Add(lblSuccess);
        pageComplete.Controls.Add(lblDesc);
        pageComplete.Controls.Add(chkLaunchApp);
        contentPanel.Controls.Add(pageComplete);
    }

    private void SetPage(int page)
    {
        currentPage = page;
        pageWelcome.Visible = (page == 0);
        pageOptions.Visible = (page == 1);
        pageProgress.Visible = (page == 2);
        pageComplete.Visible = (page == 3);

        btnBack.Enabled = (page == 1);
        btnCancel.Visible = (page != 3);

        if (page == 0)
        {
            lblTitle.Text = "NEXT : XLR Lizenzvereinbarung";
            btnNext.Text = "Weiter >";
            btnNext.Enabled = chkLicense.Checked;
        }
        else if (page == 1)
        {
            lblTitle.Text = "Installationsoptionen";
            btnNext.Text = "Installieren";
            btnNext.Enabled = true;
        }
        else if (page == 2)
        {
            lblTitle.Text = "Installation läuft...";
            btnNext.Enabled = false;
            btnBack.Enabled = false;
            btnCancel.Enabled = false;
            StartInstallationAsync();
        }
        else if (page == 3)
        {
            lblTitle.Text = "Fertigstellung";
            btnNext.Text = "Fertigstellen";
            btnNext.Enabled = true;
        }
    }

    private void BtnNext_Click(object? sender, EventArgs e)
    {
        if (currentPage == 0)
        {
            SetPage(1);
        }
        else if (currentPage == 1)
        {
            installDir = txtInstallPath.Text.Trim();
            SetPage(2);
        }
        else if (currentPage == 3)
        {
            if (chkLaunchApp.Checked)
            {
                var exe = Path.Combine(installDir, "NextXLR.exe");
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        WorkingDirectory = installDir,
                        UseShellExecute = true
                    });
                }
            }
            Application.Exit();
        }
    }

    private void Log(string msg)
    {
        if (txtLog.InvokeRequired)
        {
            txtLog.Invoke(new Action(() => Log(msg)));
            return;
        }
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
    }

    private void SetProgress(int value, string status)
    {
        if (progressBar.InvokeRequired)
        {
            progressBar.Invoke(new Action(() => SetProgress(value, status)));
            return;
        }
        progressBar.Value = Math.Min(100, Math.Max(0, value));
        lblProgressStatus.Text = status;
    }

    private async void StartInstallationAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                SetProgress(5, "Verzeichnis wird vorbereitet...");
                Log($"Zielverzeichnis: {installDir}");
                Directory.CreateDirectory(installDir);

                // Locate source files
                var srcDir = FindSourcePayloadDirectory();
                Log($"Quellverzeichnis: {srcDir}");

                SetProgress(15, "Kopiere Anwendungsdateien...");
                CopyDirectory(srcDir, installDir);

                // Ensure mixforge.exe and mixforged.exe both exist
                var mixforgedPath = Path.Combine(installDir, "mixforged.exe");
                var mixforgePath = Path.Combine(installDir, "mixforge.exe");
                if (File.Exists(mixforgedPath) && !File.Exists(mixforgePath))
                {
                    File.Copy(mixforgedPath, mixforgePath, true);
                    Log("Alias mixforge.exe erstellt.");
                }

                SetProgress(50, "Installiere Treiber...");
                if (chkInstallDriver.Checked)
                {
                    InstallDrivers(installDir);
                }

                SetProgress(75, "Richte Hintergrunddienst ein...");
                if (chkInstallService.Checked)
                {
                    SetupBackgroundService(installDir);
                }

                SetProgress(90, "Erstelle Verknüpfungen...");
                if (chkDesktopShortcut.Checked)
                {
                    CreateShortcut(
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NEXT XLR.lnk"),
                        Path.Combine(installDir, "NextXLR.exe"),
                        installDir,
                        "NEXT : XLR Audiosteuerung für GoXLR"
                    );
                }
                if (chkStartMenuShortcut.Checked)
                {
                    var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "NEXT XLR");
                    Directory.CreateDirectory(startMenu);
                    CreateShortcut(
                        Path.Combine(startMenu, "NEXT XLR.lnk"),
                        Path.Combine(installDir, "NextXLR.exe"),
                        installDir,
                        "NEXT : XLR Audiosteuerung für GoXLR"
                    );
                }

                SetProgress(100, "Installation abgeschlossen!");
                Log("Alle Schritte erfolgreich abgeschlossen.");

                this.Invoke(new Action(() => SetPage(3)));
            }
            catch (Exception ex)
            {
                Log($"FEHLER: {ex.Message}");
                MessageBox.Show($"Während der Installation ist ein Fehler aufgetreten:\n{ex.Message}", "Installationsfehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Invoke(new Action(() =>
                {
                    btnBack.Enabled = true;
                    btnCancel.Enabled = true;
                }));
            }
        });
    }

    private string FindSourcePayloadDirectory()
    {
        // Check adjacent "app" directory first (for redistributable package)
        var adjacentApp = Path.Combine(AppContext.BaseDirectory, "app");
        if (Directory.Exists(adjacentApp) && File.Exists(Path.Combine(adjacentApp, "NextXLR.exe")))
            return adjacentApp;

        // Check current directory
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "NextXLR.exe")))
            return AppContext.BaseDirectory;

        // Check debug output in repository
        var repoBin = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "apps", "windows", "bin", "Debug", "net8.0-windows10.0.19041.0", "win-x64"));
        if (Directory.Exists(repoBin) && File.Exists(Path.Combine(repoBin, "NextXLR.exe")))
            return repoBin;

        var repoRelease = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "apps", "windows", "bin", "Release", "net8.0-windows10.0.19041.0", "win-x64"));
        if (Directory.Exists(repoRelease) && File.Exists(Path.Combine(repoRelease, "NextXLR.exe")))
            return repoRelease;

        return AppContext.BaseDirectory;
    }

    private void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".pdb") || name.EndsWith(".old")) continue;
            var dest = Path.Combine(targetDir, name);
            try
            {
                File.Copy(file, dest, true);
                Log($"Kopiert: {name}");
            }
            catch (Exception ex)
            {
                Log($"Übersprungen: {name} ({ex.Message})");
            }
        }

        foreach (var sub in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(sub);
            if (name.Equals("obj", StringComparison.OrdinalIgnoreCase) || name.Equals("bin", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDirectory(sub, Path.Combine(targetDir, name));
        }

        // Copy daemon binary if not yet present in target
        var daemonSrc = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "target", "debug", "mixforged.exe"));
        if (!File.Exists(daemonSrc))
            daemonSrc = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "target", "release", "mixforged.exe"));

        if (File.Exists(daemonSrc))
        {
            var destDaemon = Path.Combine(targetDir, "mixforged.exe");
            try
            {
                File.Copy(daemonSrc, destDaemon, true);
                Log("mixforged.exe kopiert.");
            }
            catch { }
        }

        // Copy LICENSE
        var licSrc = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "LICENSE"));
        if (File.Exists(licSrc))
        {
            try { File.Copy(licSrc, Path.Combine(targetDir, "LICENSE"), true); } catch { }
        }
    }

    private void InstallDrivers(string dir)
    {
        var driverDir = Path.Combine(dir, "Assets", "Driver");
        if (!Directory.Exists(driverDir))
        {
            driverDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "apps", "windows", "Assets", "Driver"));
        }

        if (!Directory.Exists(driverDir))
        {
            Log("Treiberverzeichnis nicht gefunden. Übersprungen.");
            return;
        }

        Log("Installiere GoXLR Audiotreiber via pnputil...");
        var infs = new[] { "goxlr_audio.inf", "goxlr_audioks.inf" };
        foreach (var inf in infs)
        {
            var fullPath = Path.Combine(driverDir, inf);
            if (File.Exists(fullPath))
            {
                RunElevatedCommand("pnputil.exe", $"/add-driver \"{fullPath}\" /install");
                Log($"Treiber {inf} installiert.");
            }
        }
    }

    private void SetupBackgroundService(string dir)
    {
        var exe = Path.Combine(dir, "mixforged.exe");
        if (!File.Exists(exe))
            exe = Path.Combine(dir, "mixforge.exe");

        if (File.Exists(exe))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key != null)
                {
                    key.SetValue("NextXLR_BackgroundService", $"\"{exe}\"");
                    Log("mixforge Hintergrunddienst in Windows Autostart registriert.");
                }
            }
            catch (Exception ex)
            {
                Log($"Autostart-Registrierung fehlgeschlagen: {ex.Message}");
            }

            // Start mixforge service now in the background
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = dir,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                });
                Log("mixforge Hintergrunddienst gestartet.");
            }
            catch (Exception ex)
            {
                Log($"Dienststart: {ex.Message}");
            }
        }
    }

    private void RunElevatedCommand(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var p = Process.Start(psi);
            p?.WaitForExit(15000);
        }
        catch (Exception ex)
        {
            Log($"Befehl fehlgeschlagen ({file}): {ex.Message}");
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
            var psi = new ProcessStartInfo
            {
                FileName = "cscript.exe",
                Arguments = $"//nologo \"{vbs}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(5000);
            File.Delete(vbs);
            Log($"Verknüpfung erstellt: {Path.GetFileName(shortcutPath)}");
        }
        catch (Exception ex)
        {
            Log($"Verknüpfungsfehler: {ex.Message}");
        }
    }

    private bool ConfirmCancel()
    {
        return MessageBox.Show("Möchten Sie die Installation wirklich abbrechen?", "NEXT : XLR Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }
}
