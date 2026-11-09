using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NextXLR.Installer;

public partial class InstallerForm : Form
{
    private Panel headerPanel = null!;
    private Panel stepPanel = null!;
    private Panel contentPanel = null!;
    private Panel footerPanel = null!;

    private Label lblTitle = null!;
    private Label lblSubtitle = null!;
    private StepPill[] stepPills = new StepPill[4];

    private ModernButton btnBack = null!;
    private ModernButton btnNext = null!;
    private ModernButton btnCancel = null!;

    // Pages
    private Panel pageWelcome = null!;
    private Panel pageOptions = null!;
    private Panel pageProgress = null!;
    private Panel pageComplete = null!;

    private ModernCheckBox chkLicense = null!;
    private TextBox txtInstallPath = null!;
    private ModernCheckBox chkInstallDriver = null!;
    private ModernCheckBox chkInstallService = null!;
    private ModernCheckBox chkAppAutostart = null!;
    private ModernCheckBox chkDesktopShortcut = null!;
    private ModernCheckBox chkStartMenuShortcut = null!;
    private ModernCheckBox chkLaunchApp = null!;

    private ModernProgressBar progressBar = null!;
    private Label lblProgressStatus = null!;
    private TextBox txtLog = null!;

    private int currentPage = 0;
    private string installDir = "";
    private bool _wasAppAutostartEnabled = true; // Default true, detects existing setting on update

    public InstallerForm()
    {
        InitializeComponent();
        SetPage(0);
    }

    private void InitializeComponent()
    {
        this.Text = "NEXT : XLR Setup (v0.1.0-alpha) — plysystems";
        this.Size = new Size(760, 600);
        this.MinimumSize = new Size(740, 560);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = NextXLRTheme.DarkBg;
        this.ForeColor = NextXLRTheme.TextPrimary;
        this.Font = NextXLRTheme.BodyFont;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;

        var appIcon = NextXLRTheme.GetAppIcon();
        if (appIcon != null) this.Icon = appIcon;
        NextXLRTheme.ApplyNativeWinUIStyle(this);

        // Vorhandene Autostart-Einstellung erkennen (für Updates)
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

        // Header Panel
        headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 76,
            BackColor = NextXLRTheme.SurfaceL1,
            Padding = new Padding(28, 12, 28, 12)
        };

        var logoImg = NextXLRTheme.GetLogoImage();
        if (logoImg != null)
        {
            var logoBox = new PictureBox
            {
                Image = logoImg,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(160, 48),
                Location = new Point(this.ClientSize.Width - 190, 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.Transparent
            };
            headerPanel.Controls.Add(logoBox);
        }

        lblTitle = new Label
        {
            Text = "NEXT : XLR",
            Font = NextXLRTheme.TitleFont,
            ForeColor = NextXLRTheme.Brand,
            AutoSize = true,
            Location = new Point(28, 14)
        };

        lblSubtitle = new Label
        {
            Text = "Setup & Installationsassistent (v0.1.0-alpha) — plysystems",
            Font = NextXLRTheme.SubtitleFont,
            ForeColor = NextXLRTheme.TextMuted,
            AutoSize = true,
            Location = new Point(28, 42)
        };

        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(lblSubtitle);

        // Stepper Navigation
        stepPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            BackColor = NextXLRTheme.DarkBg,
            Padding = new Padding(28, 6, 28, 6)
        };
        BuildStepBar();

        // Footer
        footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 68,
            BackColor = NextXLRTheme.SurfaceL1,
            Padding = new Padding(28, 14, 28, 14)
        };

        btnCancel = new ModernButton
        {
            Text = "Abbrechen",
            Size = new Size(110, 38),
            Location = new Point(28, 15)
        };
        btnCancel.Click += (s, e) => { if (ConfirmCancel()) Application.Exit(); };

        btnBack = new ModernButton
        {
            Text = "< Zurück",
            Size = new Size(110, 38),
            Location = new Point(480, 15),
            Enabled = false
        };
        btnBack.Click += (s, e) => { if (currentPage > 0) SetPage(currentPage - 1); };

        btnNext = new ModernButton
        {
            Text = "Weiter >",
            IsAccent = true,
            Size = new Size(130, 38),
            Location = new Point(602, 15),
            Enabled = false
        };
        btnNext.Click += BtnNext_Click;

        footerPanel.Controls.Add(btnCancel);
        footerPanel.Controls.Add(btnBack);
        footerPanel.Controls.Add(btnNext);

        // Content Area
        contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 10, 28, 10),
            BackColor = NextXLRTheme.DarkBg
        };

        this.Controls.Add(contentPanel);
        this.Controls.Add(stepPanel);
        this.Controls.Add(headerPanel);
        this.Controls.Add(footerPanel);

        BuildWelcomePage();
        BuildOptionsPage();
        BuildProgressPage();
        BuildCompletePage();
    }

    private void BuildStepBar()
    {
        string[] titles = { "1. Lizenz", "2. Optionen", "3. Installation", "4. Fertig" };
        int x = 28;
        for (int i = 0; i < 4; i++)
        {
            var pill = new StepPill(titles[i])
            {
                Size = new Size(130, 26),
                Location = new Point(x, 6),
                Active = (i == 0),
                Completed = false
            };

            stepPills[i] = pill;
            stepPanel.Controls.Add(pill);

            x += 140;
        }
    }

    private void UpdateStepBar(int activeStep)
    {
        for (int i = 0; i < 4; i++)
        {
            stepPills[i].Active = (i == activeStep);
            stepPills[i].Completed = (i < activeStep);
        }
    }

    private void BuildWelcomePage()
    {
        pageWelcome = new Panel { Dock = DockStyle.Fill, Visible = false };

        var card = new RoundedCard
        {
            Location = new Point(4, 4),
            Size = new Size(696, 360),
            BackColor = NextXLRTheme.SurfaceL2,
            CornerRadius = 14
        };

        var lblHeader = new Label
        {
            Text = "Lizenzvereinbarung (plysystems)",
            Font = NextXLRTheme.TitleFont,
            ForeColor = NextXLRTheme.Brand,
            AutoSize = true,
            Location = new Point(18, 14)
        };

        var txtLicense = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Size = new Size(658, 260),
            Location = new Point(18, 44),
            BackColor = NextXLRTheme.DarkBg,
            ForeColor = NextXLRTheme.TextPrimary,
            Font = NextXLRTheme.MonospaceFont,
            BorderStyle = BorderStyle.None,
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

        chkLicense = new ModernCheckBox
        {
            Text = "Ich akzeptiere die Lizenzvereinbarung von plysystems",
            Font = NextXLRTheme.BoldFont,
            ForeColor = NextXLRTheme.Brand,
            Size = new Size(540, 26),
            Location = new Point(20, 318)
        };
        chkLicense.CheckedChanged += (s, e) => { btnNext.Enabled = chkLicense.Checked; };

        card.Controls.Add(lblHeader);
        card.Controls.Add(txtLicense);
        card.Controls.Add(chkLicense);

        pageWelcome.Controls.Add(card);
        contentPanel.Controls.Add(pageWelcome);
    }

    private void BuildOptionsPage()
    {
        pageOptions = new Panel { Dock = DockStyle.Fill, Visible = false };

        var card = new RoundedCard
        {
            Location = new Point(4, 4),
            Size = new Size(696, 360),
            BackColor = NextXLRTheme.SurfaceL2,
            CornerRadius = 14
        };

        var lblDir = new Label
        {
            Text = "Installationsverzeichnis:",
            Font = NextXLRTheme.BoldFont,
            ForeColor = NextXLRTheme.Brand,
            AutoSize = true,
            Location = new Point(18, 14)
        };

        installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NextXLR");

        txtInstallPath = new TextBox
        {
            Text = installDir,
            Size = new Size(520, 26),
            Location = new Point(20, 40),
            BackColor = NextXLRTheme.DarkBg,
            ForeColor = NextXLRTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnBrowse = new ModernButton
        {
            Text = "Durchsuchen...",
            Size = new Size(128, 30),
            Location = new Point(548, 38)
        };
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

        var subCard = new RoundedCard
        {
            Location = new Point(20, 80),
            Size = new Size(656, 266),
            BackColor = NextXLRTheme.SurfaceL3,
            CornerRadius = 12
        };

        var lblComponents = new Label
        {
            Text = "Komponenten & System-Integration:",
            Font = NextXLRTheme.BoldFont,
            ForeColor = NextXLRTheme.Brand,
            AutoSize = true,
            Location = new Point(16, 12)
        };

        chkInstallDriver = new ModernCheckBox
        {
            Text = "TC-Helicon GoXLR USB-Audiotreiber installieren (für 8 Windows-Audiokanäle)",
            Checked = true,
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextPrimary,
            Size = new Size(610, 24),
            Location = new Point(18, 40)
        };

        chkInstallService = new ModernCheckBox
        {
            Text = "mixforge Hintergrunddienst einrichten (Automatischer Start mit Windows)",
            Checked = true,
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextPrimary,
            Size = new Size(610, 24),
            Location = new Point(18, 70)
        };

        chkAppAutostart = new ModernCheckBox
        {
            Text = "NEXT : XLR Hauptanwendung bei Windows-Start minimiert starten",
            Checked = _wasAppAutostartEnabled,
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextPrimary,
            Size = new Size(610, 24),
            Location = new Point(18, 100)
        };

        chkDesktopShortcut = new ModernCheckBox
        {
            Text = "Desktop-Verknüpfung erstellen",
            Checked = true,
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextPrimary,
            Size = new Size(610, 24),
            Location = new Point(18, 130)
        };

        chkStartMenuShortcut = new ModernCheckBox
        {
            Text = "Startmenü-Eintrag erstellen",
            Checked = true,
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextPrimary,
            Size = new Size(610, 24),
            Location = new Point(18, 160)
        };

        subCard.Controls.Add(lblComponents);
        subCard.Controls.Add(chkInstallDriver);
        subCard.Controls.Add(chkInstallService);
        subCard.Controls.Add(chkAppAutostart);
        subCard.Controls.Add(chkDesktopShortcut);
        subCard.Controls.Add(chkStartMenuShortcut);

        card.Controls.Add(lblDir);
        card.Controls.Add(txtInstallPath);
        card.Controls.Add(btnBrowse);
        card.Controls.Add(subCard);

        pageOptions.Controls.Add(card);
        contentPanel.Controls.Add(pageOptions);
    }

    private void BuildProgressPage()
    {
        pageProgress = new Panel { Dock = DockStyle.Fill, Visible = false };

        var card = new RoundedCard
        {
            Location = new Point(4, 4),
            Size = new Size(696, 360),
            BackColor = NextXLRTheme.SurfaceL2,
            CornerRadius = 14
        };

        lblProgressStatus = new Label
        {
            Text = "Bereite Installation vor...",
            Font = NextXLRTheme.TitleFont,
            ForeColor = NextXLRTheme.Brand,
            AutoSize = true,
            Location = new Point(18, 14)
        };

        progressBar = new ModernProgressBar
        {
            Size = new Size(658, 14),
            Location = new Point(20, 46),
            Value = 0
        };

        txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Size = new Size(658, 270),
            Location = new Point(20, 72),
            BackColor = NextXLRTheme.DarkBg,
            ForeColor = NextXLRTheme.TextMuted,
            Font = NextXLRTheme.MonospaceFont,
            BorderStyle = BorderStyle.None
        };

        card.Controls.Add(lblProgressStatus);
        card.Controls.Add(progressBar);
        card.Controls.Add(txtLog);

        pageProgress.Controls.Add(card);
        contentPanel.Controls.Add(pageProgress);
    }

    private void BuildCompletePage()
    {
        pageComplete = new Panel { Dock = DockStyle.Fill, Visible = false };

        var card = new RoundedCard
        {
            Location = new Point(4, 4),
            Size = new Size(696, 360),
            BackColor = NextXLRTheme.SurfaceL2,
            CornerRadius = 14
        };

        var lblSuccess = new Label
        {
            Text = "NEXT : XLR erfolgreich eingerichtet!",
            Font = NextXLRTheme.TitleFont,
            ForeColor = NextXLRTheme.Success,
            AutoSize = true,
            Location = new Point(20, 20)
        };

        var lblDesc = new Label
        {
            Text = "NEXT : XLR v0.1.0-alpha wurde erfolgreich auf Ihrem System installiert.\n" +
                   "Der mixforge Hintergrunddienst ist aktiv und steuert Ihre GoXLR Hardware.\n\n" +
                   "Sie können NEXT : XLR jederzeit über die Windows-Systemsteuerung oder den Deinstallationsassistenten deinstallieren.",
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextPrimary,
            Size = new Size(650, 100),
            Location = new Point(22, 60)
        };

        chkLaunchApp = new ModernCheckBox
        {
            Text = "NEXT : XLR jetzt starten",
            Checked = true,
            Font = NextXLRTheme.BoldFont,
            ForeColor = NextXLRTheme.Brand,
            Size = new Size(300, 26),
            Location = new Point(24, 180)
        };

        card.Controls.Add(lblSuccess);
        card.Controls.Add(lblDesc);
        card.Controls.Add(chkLaunchApp);

        pageComplete.Controls.Add(card);
        contentPanel.Controls.Add(pageComplete);
    }

    private void SetPage(int page)
    {
        currentPage = page;
        UpdateStepBar(page);

        pageWelcome.Visible = (page == 0);
        pageOptions.Visible = (page == 1);
        pageProgress.Visible = (page == 2);
        pageComplete.Visible = (page == 3);

        btnBack.Enabled = (page == 1);
        btnCancel.Visible = (page != 3);

        if (page == 0)
        {
            btnNext.Text = "Weiter >";
            btnNext.Enabled = chkLicense.Checked;
        }
        else if (page == 1)
        {
            btnNext.Text = "Installieren";
            btnNext.Enabled = true;
        }
        else if (page == 2)
        {
            btnNext.Enabled = false;
            btnBack.Enabled = false;
            btnCancel.Enabled = false;
            StartInstallationAsync();
        }
        else if (page == 3)
        {
            btnNext.Text = "Fertigstellen";
            btnNext.Enabled = true;
        }
    }

    private void BtnNext_Click(object? sender, EventArgs e)
    {
        if (currentPage == 0) SetPage(1);
        else if (currentPage == 1) { installDir = txtInstallPath.Text.Trim(); SetPage(2); }
        else if (currentPage == 3)
        {
            if (chkLaunchApp.Checked)
            {
                var exe = Path.Combine(installDir, "NextXLR.exe");
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = installDir, UseShellExecute = true });
                }
            }
            Application.Exit();
        }
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
                    Log("Kein lokales Anwendungsverzeichnis gefunden. Lade Release-Paket von GitHub herunter...");
                    DownloadAndExtractFromGitHub(installDir);
                }

                // Setup mixforge alias
                var mixforgedPath = Path.Combine(installDir, "mixforged.exe");
                var mixforgePath = Path.Combine(installDir, "mixforge.exe");
                if (File.Exists(mixforgedPath) && !File.Exists(mixforgePath))
                {
                    File.Copy(mixforgedPath, mixforgePath, true);
                    Log("Alias mixforge.exe erstellt.");
                }

                // Copy uninstaller executable
                try
                {
                    var uninstallDest = Path.Combine(installDir, "NextXLR-Uninstall.exe");
                    var currentExe = Environment.ProcessPath ?? Application.ExecutablePath;
                    if (File.Exists(currentExe) && !string.Equals(currentExe, uninstallDest, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(currentExe, uninstallDest, true);
                        Log("Deinstallationsprogramm (NextXLR-Uninstall.exe) bereitgestellt.");
                    }
                }
                catch { }

                SetProgress(50, "Installiere Treiber...");
                if (chkInstallDriver.Checked)
                {
                    InstallDrivers(installDir);
                }

                SetProgress(75, "Richte Hintergrunddienst & Autostart ein...");
                if (chkInstallService.Checked)
                {
                    SetupBackgroundService(installDir);
                }
                if (chkAppAutostart.Checked)
                {
                    SetupAppAutostart(installDir);
                }

                SetProgress(88, "Erstelle Verknüpfungen...");
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

                SetProgress(95, "Registriere Windows Deinstallationsprogramm...");
                RegisterUninstallEntry(installDir);

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

    private bool TryExtractEmbeddedPayload(string targetDir, bool isExisting)
    {
        try
        {
            var asm = typeof(InstallerForm).Assembly;
            var resNames = asm.GetManifestResourceNames();
            var payloadRes = resNames.FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
            if (payloadRes == null)
            {
                return false;
            }

            Log($"Eingebettetes Software-Paket gefunden ({payloadRes}). Starte Entpacken...");
            using var stream = asm.GetManifestResourceStream(payloadRes);
            if (stream == null)
            {
                Log("Fehler: Konnte Ressourcenstream für payload.zip nicht öffnen.");
                return false;
            }

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            int totalEntries = archive.Entries.Count;
            int currentEntry = 0;

            SetProgress(15, isExisting ? "Aktualisiere Anwendungsdateien aus Paket..." : "Installiere Anwendungsdateien aus Paket...");

            foreach (var entry in archive.Entries)
            {
                currentEntry++;
                if (string.IsNullOrEmpty(entry.Name) && (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")))
                {
                    var dir = Path.Combine(targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(dir);
                    continue;
                }

                var dest = Path.Combine(targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                var parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                bool copied = false;
                for (int r = 0; r < 5 && !copied; r++)
                {
                    try
                    {
                        entry.ExtractToFile(dest, overwrite: true);
                        copied = true;
                    }
                    catch
                    {
                        Thread.Sleep(150);
                    }
                }

                if (currentEntry % 10 == 0 || currentEntry == totalEntries)
                {
                    int pct = 15 + (int)((double)currentEntry / totalEntries * 35.0);
                    SetProgress(pct, $"Kopiere {entry.Name} ({currentEntry}/{totalEntries})...");
                }
            }

            Log("Alle Anwendungsdateien aus dem Installer-Paket erfolgreich entpackt.");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Hinweis bei eingebetteter Extraktion: {ex.Message}");
            return false;
        }
    }

    private void DownloadAndExtractFromGitHub(string targetDir)
    {
        try
        {
            var zipUrl = "https://github.com/plysystems/nextXLR/releases/download/v0.1.0-alpha/NextXLR-v0.1.0-alpha.zip";
            var tempZip = Path.Combine(Path.GetTempPath(), "NextXLR-Download-" + Guid.NewGuid().ToString("N") + ".zip");
            Log($"Lade {zipUrl} herunter...");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "NextXLR-Setup");
                var response = client.GetAsync(zipUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
                response.Content.CopyToAsync(fs).GetAwaiter().GetResult();
            }

            Log("Download abgeschlossen. Entpacke Paket...");
            using (var zip = ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in zip.Entries)
                {
                    string relPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    if (relPath.StartsWith("app\\", StringComparison.OrdinalIgnoreCase))
                    {
                        relPath = relPath.Substring(4);
                    }
                    else if (relPath.StartsWith("app/", StringComparison.OrdinalIgnoreCase))
                    {
                        relPath = relPath.Substring(4);
                    }

                    if (string.IsNullOrEmpty(relPath)) continue;

                    var dest = Path.Combine(targetDir, relPath);
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(dest);
                        continue;
                    }

                    var p = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(p)) Directory.CreateDirectory(p);

                    entry.ExtractToFile(dest, overwrite: true);
                }
            }

            try { File.Delete(tempZip); } catch { }
            Log("Download und Extraktion erfolgreich abgeschlossen.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Weder lokales Paket noch Online-Download verfügbar: {ex.Message}", ex);
        }
    }

    private string FindSourcePayloadDirectory()
    {
        var adjacentApp = Path.Combine(AppContext.BaseDirectory, "app");
        if (Directory.Exists(adjacentApp) && File.Exists(Path.Combine(adjacentApp, "NextXLR.exe"))) return adjacentApp;

        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "NextXLR.exe"))) return AppContext.BaseDirectory;

        var repoBin = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "apps", "windows", "bin", "Debug", "net8.0-windows10.0.19041.0", "win-x64"));
        if (Directory.Exists(repoBin) && File.Exists(Path.Combine(repoBin, "NextXLR.exe"))) return repoBin;

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
            bool copied = false;
            for (int r = 0; r < 5 && !copied; r++)
            {
                try
                {
                    File.Copy(file, dest, true);
                    copied = true;
                    Log($"Kopiert: {name}");
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
        }

        foreach (var sub in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(sub);
            if (name.Equals("obj", StringComparison.OrdinalIgnoreCase) || name.Equals("bin", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDirectory(sub, Path.Combine(targetDir, name));
        }

        var daemonSrc = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "target", "release", "mixforged.exe"));
        if (!File.Exists(daemonSrc)) daemonSrc = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "target", "debug", "mixforged.exe"));
        if (File.Exists(daemonSrc))
        {
            try { File.Copy(daemonSrc, Path.Combine(targetDir, "mixforged.exe"), true); Log("mixforged.exe kopiert."); } catch { }
        }

        var licSrc = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "LICENSE"));
        if (File.Exists(licSrc))
        {
            try { File.Copy(licSrc, Path.Combine(targetDir, "LICENSE"), true); } catch { }
        }
    }

    private void InstallDrivers(string dir)
    {
        var driverDir = Path.Combine(dir, "Assets", "Driver");
        if (!Directory.Exists(driverDir)) driverDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "apps", "windows", "Assets", "Driver"));
        if (!Directory.Exists(driverDir)) { Log("Treiberverzeichnis nicht gefunden."); return; }

        Log("Installiere GoXLR Audiotreiber via pnputil...");
        foreach (var inf in new[] { "goxlr_audio.inf", "goxlr_audioks.inf" })
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
        if (!File.Exists(exe)) exe = Path.Combine(dir, "mixforge.exe");

        if (File.Exists(exe))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                key?.SetValue("NextXLR_BackgroundService", $"\"{exe}\"");
                Log("mixforge Hintergrunddienst in Windows Autostart registriert.");
            }
            catch { }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = dir, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false });
                Log("mixforge Hintergrunddienst gestartet.");
            }
            catch { }
        }
    }

    private void SetupAppAutostart(string dir)
    {
        var exe = Path.Combine(dir, "NextXLR.exe");
        if (File.Exists(exe))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                key?.SetValue("NextXLR", $"\"{exe}\" --minimized");
                Log("NEXT : XLR Hauptanwendung in Windows Autostart registriert (--minimized).");
            }
            catch (Exception ex)
            {
                Log($"Warnung: Autostart-Eintrag konnte nicht gesetzt werden: {ex.Message}");
            }

            try
            {
                var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextXLR");
                Directory.CreateDirectory(appDataDir);
                var settingsPath = Path.Combine(appDataDir, "settings.json");
                if (File.Exists(settingsPath))
                {
                    var content = File.ReadAllText(settingsPath);
                    if (content.Contains("\"AutostartEnabled\""))
                    {
                        content = System.Text.RegularExpressions.Regex.Replace(content, "\"AutostartEnabled\":\\s*(true|false)", "\"AutostartEnabled\": true");
                    }
                    else
                    {
                        content = content.TrimEnd();
                        if (content.EndsWith("}"))
                        {
                            content = content.Substring(0, content.Length - 1) + ",\n  \"AutostartEnabled\": true\n}";
                        }
                    }
                    File.WriteAllText(settingsPath, content);
                }
                else
                {
                    File.WriteAllText(settingsPath, "{\n  \"AutostartEnabled\": true\n}");
                }
                Log("Autostart-Zustand dauerhaft in settings.json gespeichert.");
            }
            catch { }
        }
    }

    private void RegisterUninstallEntry(string dir)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NextXLR");
            if (key != null)
            {
                key.SetValue("DisplayName", "NEXT : XLR");
                key.SetValue("DisplayVersion", "0.1.0-alpha");
                key.SetValue("Publisher", "plysystems");
                key.SetValue("InstallLocation", dir);
                key.SetValue("UninstallString", $"\"{Path.Combine(dir, "NextXLR-Uninstall.exe")}\"");
                key.SetValue("DisplayIcon", $"\"{Path.Combine(dir, "NextXLR.exe")}\",0");
                Log("Windows App-Deinstallationseintrag registriert.");
            }
        }
        catch { }
    }

    private void CleanupPreviousInstallation(string dir)
    {
        Log("Führe gründliche Bereinigung voriger Versionen und Autostart-Einträge durch...");

        // 1. Terminate all running processes
        foreach (var procName in new[] { "NextXLR", "mixforged", "mixforge", "GoXLRApp", "GoXLRAudioCplApp" })
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    Log($"Beende laufenden Prozess {p.ProcessName} (PID {p.Id})...");
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch { }
        }

        // 2. Cleanup old Windows Services if any existed
        try
        {
            RunElevatedCommand("sc.exe", "stop mixforge");
            RunElevatedCommand("sc.exe", "delete mixforge");
            RunElevatedCommand("sc.exe", "stop NextXLRService");
            RunElevatedCommand("sc.exe", "delete NextXLRService");
        }
        catch { }

        // 3. Autostart cleanup (Registry HKCU and HKLM)
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key != null)
            {
                foreach (var name in new[] { "NextXLR", "NextXLR_BackgroundService", "mixforge", "mixforged", "GoXLR" })
                {
                    if (key.GetValue(name) != null)
                    {
                        key.DeleteValue(name, false);
                        Log($"Alter Autostart-Eintrag (HKCU) bereinigt: {name}");
                    }
                }
            }
        }
        catch { }

        try
        {
            using var keyHklm = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (keyHklm != null)
            {
                foreach (var name in new[] { "NextXLR", "NextXLR_BackgroundService", "mixforge", "mixforged" })
                {
                    if (keyHklm.GetValue(name) != null)
                    {
                        keyHklm.DeleteValue(name, false);
                        Log($"Alter Autostart-Eintrag (HKLM) bereinigt: {name}");
                    }
                }
            }
        }
        catch { }

        // 4. Startup folder cleanup
        try
        {
            var startupFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };
            foreach (var sf in startupFolders)
            {
                if (!Directory.Exists(sf)) continue;
                foreach (var shortcut in new[] { "NEXT XLR.lnk", "NextXLR.lnk", "mixforge.lnk" })
                {
                    var file = Path.Combine(sf, shortcut);
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        Log($"Alte Autostart-Verknüpfung entfernt: {shortcut}");
                    }
                }
            }
        }
        catch { }

        // 5. Clean up old installation directory files (overwrite cleanly)
        if (Directory.Exists(dir))
        {
            try
            {
                var currentProc = Environment.ProcessPath ?? "";
                var currentApp = Application.ExecutablePath;
                foreach (var file in Directory.GetFiles(dir))
                {
                    var name = Path.GetFileName(file);
                    if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(currentProc) && string.Equals(file, currentProc, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(currentApp) && string.Equals(file, currentApp, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private void RunElevatedCommand(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = file, Arguments = args, UseShellExecute = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            Process.Start(psi)?.WaitForExit(15000);
        }
        catch { }
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
        return MessageBox.Show("Möchten Sie die Installation wirklich abbrechen?", "NEXT : XLR Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }
}
