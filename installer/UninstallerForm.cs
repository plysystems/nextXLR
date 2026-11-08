using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NextXLR.Installer;

public partial class UninstallerForm : Form
{
    private Panel headerPanel = null!;
    private Panel contentPanel = null!;
    private Panel footerPanel = null!;

    private Label lblTitle = null!;
    private Label lblSubtitle = null!;
    private ModernButton btnCancel = null!;
    private ModernButton btnUninstall = null!;
    private ModernButton btnFinish = null!;

    private Panel pageConfirm = null!;
    private Panel pageProgress = null!;
    private Panel pageDone = null!;

    private CheckBox chkRemoveService = null!;
    private CheckBox chkRemoveShortcuts = null!;
    private CheckBox chkRemoveData = null!;
    private CheckBox chkRemoveDrivers = null!;

    private Label lblProgressStatus = null!;
    private ModernProgressBar progressBar = null!;
    private TextBox txtLog = null!;

    private string installDir = "";

    public UninstallerForm()
    {
        InitializeComponent();
        DetectInstallDirectory();
    }

    private void DetectInstallDirectory()
    {
        // 1. Directory of this executable
        var exeDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        if (File.Exists(Path.Combine(exeDir, "NextXLR.exe")))
        {
            installDir = exeDir;
            return;
        }

        // 2. LocalAppData standard directory
        var defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NextXLR");
        if (Directory.Exists(defaultDir))
        {
            installDir = defaultDir;
            return;
        }

        installDir = exeDir;
    }

    private void InitializeComponent()
    {
        this.Text = "NEXT : XLR Deinstallation — plysystems";
        this.Size = new Size(720, 560);
        this.MinimumSize = new Size(700, 520);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = NextXLRTheme.DarkBg;
        this.ForeColor = NextXLRTheme.TextPrimary;
        this.Font = NextXLRTheme.BodyFont;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;

        // Header
        headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 76,
            BackColor = NextXLRTheme.SurfaceL1,
            Padding = new Padding(28, 14, 28, 14)
        };

        lblTitle = new Label
        {
            Text = "NEXT : XLR Deinstallation",
            Font = NextXLRTheme.TitleFont,
            ForeColor = NextXLRTheme.Danger,
            AutoSize = true,
            Location = new Point(28, 14)
        };

        lblSubtitle = new Label
        {
            Text = "Entfernen von NEXT : XLR, Hintergrunddiensten und Komponenten",
            Font = NextXLRTheme.SubtitleFont,
            ForeColor = NextXLRTheme.TextMuted,
            AutoSize = true,
            Location = new Point(28, 42)
        };

        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(lblSubtitle);

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
        btnCancel.Click += (s, e) => Application.Exit();

        btnUninstall = new ModernButton
        {
            Text = "Deinstallieren",
            IsDanger = true,
            Size = new Size(140, 38),
            Location = new Point(550, 15)
        };
        btnUninstall.Click += (s, e) => StartUninstallAsync();

        btnFinish = new ModernButton
        {
            Text = "Schließen",
            IsAccent = true,
            Size = new Size(120, 38),
            Location = new Point(570, 15),
            Visible = false
        };
        btnFinish.Click += (s, e) => Application.Exit();

        footerPanel.Controls.Add(btnCancel);
        footerPanel.Controls.Add(btnUninstall);
        footerPanel.Controls.Add(btnFinish);

        // Content Host
        contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 20, 28, 10),
            BackColor = NextXLRTheme.DarkBg
        };

        this.Controls.Add(contentPanel);
        this.Controls.Add(headerPanel);
        this.Controls.Add(footerPanel);

        BuildConfirmPage();
        BuildProgressPage();
        BuildDonePage();
    }

    private void BuildConfirmPage()
    {
        pageConfirm = new Panel { Dock = DockStyle.Fill, Visible = true };

        var card = new RoundedCard
        {
            Location = new Point(4, 4),
            Size = new Size(656, 330),
            BackColor = NextXLRTheme.SurfaceL2,
            CornerRadius = 16
        };

        var lblPrompt = new Label
        {
            Text = "Möchten Sie NEXT : XLR wirklich von Ihrem Computer deinstallieren?",
            Font = NextXLRTheme.TitleFont,
            ForeColor = NextXLRTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(20, 18)
        };

        var lblPath = new Label
        {
            Text = $"Installationspfad: {installDir}",
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextMuted,
            AutoSize = true,
            Location = new Point(20, 52)
        };

        var subCard = new RoundedCard
        {
            Location = new Point(20, 84),
            Size = new Size(616, 220),
            BackColor = NextXLRTheme.SurfaceL3,
            CornerRadius = 12
        };

        var lblOptions = new Label
        {
            Text = "Folgende Aktionen werden ausgeführt:",
            Font = NextXLRTheme.BoldFont,
            ForeColor = NextXLRTheme.Brand,
            AutoSize = true,
            Location = new Point(16, 12)
        };

        chkRemoveService = new CheckBox
        {
            Text = "mixforge Hintergrunddienst stoppen & aus Windows-Autostart austragen",
            Checked = true,
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(18, 44)
        };

        chkRemoveShortcuts = new CheckBox
        {
            Text = "Desktop- und Startmenü-Verknüpfungen entfernen",
            Checked = true,
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(18, 76)
        };

        chkRemoveData = new CheckBox
        {
            Text = "Benutzerprofile, Equalizer-Setups & Konfigurationen ebenfalls löschen",
            Checked = false,
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(18, 108)
        };

        chkRemoveDrivers = new CheckBox
        {
            Text = "TC-Helicon GoXLR USB-Audiotreiber deinstallieren (pnputil)",
            Checked = false,
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextMuted,
            AutoSize = true,
            Location = new Point(18, 140)
        };

        var lblDriverHint = new Label
        {
            Text = "(Hinweis: Deaktiviere Treiber nur, wenn du kein anderes GoXLR-Tool mehr nutzt)",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = NextXLRTheme.TextSubtle,
            AutoSize = true,
            Location = new Point(38, 168)
        };

        subCard.Controls.Add(lblOptions);
        subCard.Controls.Add(chkRemoveService);
        subCard.Controls.Add(chkRemoveShortcuts);
        subCard.Controls.Add(chkRemoveData);
        subCard.Controls.Add(chkRemoveDrivers);
        subCard.Controls.Add(lblDriverHint);

        card.Controls.Add(lblPrompt);
        card.Controls.Add(lblPath);
        card.Controls.Add(subCard);

        pageConfirm.Controls.Add(card);
        contentPanel.Controls.Add(pageConfirm);
    }

    private void BuildProgressPage()
    {
        pageProgress = new Panel { Dock = DockStyle.Fill, Visible = false };

        var card = new RoundedCard
        {
            Location = new Point(4, 4),
            Size = new Size(656, 330),
            BackColor = NextXLRTheme.SurfaceL2,
            CornerRadius = 16
        };

        lblProgressStatus = new Label
        {
            Text = "Deinstallation läuft...",
            Font = NextXLRTheme.TitleFont,
            ForeColor = NextXLRTheme.Brand,
            AutoSize = true,
            Location = new Point(20, 16)
        };

        progressBar = new ModernProgressBar
        {
            Size = new Size(616, 14),
            Location = new Point(20, 48),
            Value = 0
        };

        txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Size = new Size(616, 230),
            Location = new Point(20, 74),
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

    private void BuildDonePage()
    {
        pageDone = new Panel { Dock = DockStyle.Fill, Visible = false };

        var card = new RoundedCard
        {
            Location = new Point(4, 4),
            Size = new Size(656, 330),
            BackColor = NextXLRTheme.SurfaceL2,
            CornerRadius = 16
        };

        var lblDoneTitle = new Label
        {
            Text = "Deinstallation erfolgreich abgeschlossen",
            Font = NextXLRTheme.TitleFont,
            ForeColor = NextXLRTheme.Success,
            AutoSize = true,
            Location = new Point(20, 24)
        };

        var lblDoneDesc = new Label
        {
            Text = "NEXT : XLR und die zugehörigen Komponenten wurden erfolgreich von Ihrem Computer entfernt.\n\n" +
                   "Vielen Dank, dass Sie NEXT : XLR von plysystems verwendet haben.",
            Font = NextXLRTheme.BodyFont,
            ForeColor = NextXLRTheme.TextPrimary,
            Size = new Size(616, 120),
            Location = new Point(20, 64)
        };

        card.Controls.Add(lblDoneTitle);
        card.Controls.Add(lblDoneDesc);

        pageDone.Controls.Add(card);
        contentPanel.Controls.Add(pageDone);
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

    private void SetProgress(int val, string status)
    {
        if (progressBar.InvokeRequired)
        {
            progressBar.Invoke(new Action(() => SetProgress(val, status)));
            return;
        }
        progressBar.Value = val;
        lblProgressStatus.Text = status;
    }

    private async void StartUninstallAsync()
    {
        pageConfirm.Visible = false;
        pageProgress.Visible = true;
        btnCancel.Enabled = false;
        btnUninstall.Enabled = false;

        await Task.Run(() =>
        {
            try
            {
                SetProgress(10, "Beende laufende Prozesse...");
                KillProcess("NextXLR");
                KillProcess("mixforged");
                KillProcess("mixforge");

                if (chkRemoveService.Checked)
                {
                    SetProgress(30, "Entferne Autostart & Hintergrunddienst...");
                    RemoveAutostart();
                }

                if (chkRemoveShortcuts.Checked)
                {
                    SetProgress(50, "Lösche Verknüpfungen...");
                    RemoveShortcuts();
                }

                if (chkRemoveDrivers.Checked)
                {
                    SetProgress(65, "Deinstalliere GoXLR Audiotreiber...");
                    RemoveDrivers();
                }

                if (chkRemoveData.Checked)
                {
                    SetProgress(75, "Lösche Benutzerkonfigurationen...");
                    RemoveUserData();
                }

                SetProgress(85, "Entferne Programmdateien...");
                RemoveAppFiles();

                SetProgress(95, "Entferne Windows-Registrierungseinträge...");
                RemoveUninstallRegistry();

                SetProgress(100, "Fertig!");
                Log("NEXT : XLR wurde vollständig deinstalliert.");

                this.Invoke(new Action(() =>
                {
                    pageProgress.Visible = false;
                    pageDone.Visible = true;
                    btnCancel.Visible = false;
                    btnUninstall.Visible = false;
                    btnFinish.Visible = true;
                }));
            }
            catch (Exception ex)
            {
                Log($"Fehler während der Deinstallation: {ex.Message}");
                MessageBox.Show($"Fehler: {ex.Message}", "Deinstallation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.Invoke(new Action(() =>
                {
                    btnCancel.Enabled = true;
                }));
            }
        });
    }

    private void KillProcess(string name)
    {
        try
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                    Log($"Prozess {name} (PID: {p.Id}) beendet.");
                }
                catch { }
            }
        }
        catch { }
    }

    private void RemoveAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key != null)
            {
                if (key.GetValue("NextXLR_BackgroundService") != null)
                {
                    key.DeleteValue("NextXLR_BackgroundService");
                    Log("Autostart 'NextXLR_BackgroundService' entfernt.");
                }
                if (key.GetValue("NextXLR") != null)
                {
                    key.DeleteValue("NextXLR");
                    Log("Autostart 'NextXLR' entfernt.");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Autostart-Entfernung: {ex.Message}");
        }
    }

    private void RemoveShortcuts()
    {
        try
        {
            var desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NEXT XLR.lnk");
            if (File.Exists(desktopLnk)) { File.Delete(desktopLnk); Log("Desktop-Verknüpfung gelöscht."); }

            var startDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "NEXT XLR");
            if (Directory.Exists(startDir)) { Directory.Delete(startDir, true); Log("Startmenü-Eintrag gelöscht."); }
        }
        catch (Exception ex)
        {
            Log($"Verknüpfungen löschen: {ex.Message}");
        }
    }

    private void RemoveDrivers()
    {
        try
        {
            // Execute pnputil to remove goxlr_audio.inf
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = "/delete-driver goxlr_audio.inf /uninstall",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi)?.WaitForExit(10000);
            Log("Audiotreiber-Deinstallation angefordert.");
        }
        catch (Exception ex)
        {
            Log($"Treiber-Deinstallation: {ex.Message}");
        }
    }

    private void RemoveUserData()
    {
        try
        {
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextXLR");
            if (Directory.Exists(dataDir))
            {
                Directory.Delete(dataDir, true);
                Log($"Benutzerdatenverzeichnis gelöscht: {dataDir}");
            }
        }
        catch (Exception ex)
        {
            Log($"Benutzerdaten löschen: {ex.Message}");
        }
    }

    private void RemoveAppFiles()
    {
        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) return;

        try
        {
            foreach (var file in Directory.GetFiles(installDir))
            {
                // Don't delete the running uninstaller binary immediately
                if (file.Equals(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    File.Delete(file);
                }
                catch { }
            }

            foreach (var dir in Directory.GetDirectories(installDir))
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch { }
            }
            Log($"Programmdateien in {installDir} bereinigt.");
        }
        catch (Exception ex)
        {
            Log($"Programmdateien: {ex.Message}");
        }
    }

    private void RemoveUninstallRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
            key?.DeleteSubKeyTree("NextXLR", false);
            Log("Windows Deinstallations-Eintrag entfernt.");
        }
        catch { }
    }
}

