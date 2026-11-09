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
    private GradientSidebarPanel sidebarPanel = null!;
    private Panel mainContainer = null!;

    // Pages
    private Panel pageConfirm = null!;
    private Panel pageProgress = null!;
    private Panel pageDone = null!;

    private ModernCheckBox chkRemoveData = null!;
    private ModernButton btnCancel = null!;
    private ModernButton btnUninstall = null!;
    private ModernButton btnFinish = null!;

    private Label lblProgressTitle = null!;
    private Label lblProgressSubtitle = null!;
    private ModernProgressBar progressBar = null!;
    private Label lblProgressStatus = null!;
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
        this.Text = "NEXT : XLR deinstallieren";
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

        // Right Main Container
        mainContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 24, 31) // #16181F
        };
        this.Controls.Add(mainContainer);

        // Left Gradient Sidebar (matching Bild 2)
        sidebarPanel = new GradientSidebarPanel
        {
            Dock = DockStyle.Left,
            Width = 280
        };
        this.Controls.Add(sidebarPanel);
        sidebarPanel.SendToBack();
        mainContainer.BringToFront();

        BuildConfirmPage();
        BuildProgressPage();
        BuildDonePage();

        ShowPage(pageConfirm);
    }

    private void BuildConfirmPage()
    {
        pageConfirm = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        int leftMargin = 32;

        var lblTitle = new Label
        {
            Text = "NEXT : XLR deinstallieren",
            Font = new Font("Segoe UI Variable Display", 21f, FontStyle.Bold),
            ForeColor = Color.FromArgb(248, 250, 252),
            AutoSize = true,
            Location = new Point(leftMargin, 26)
        };
        pageConfirm.Controls.Add(lblTitle);

        var lblDesc = new Label
        {
            Text = "NEXT : XLR wird beendet und vollständig von deinem Computer entfernt. Deine Windows-Standard-Audiogeräte werden wiederhergestellt.",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(leftMargin, 68),
            Size = new Size(500, 48)
        };
        pageConfirm.Controls.Add(lblDesc);

        chkRemoveData = new ModernCheckBox
        {
            Text = "Auch meine Einstellungen und Profile löschen",
            SubText = "Deine Mediendateien und Samples selbst werden nie gelöscht, nur die Konfiguration von NEXT : XLR.",
            Checked = false, // Bild 2 default: unchecked
            Location = new Point(leftMargin, 130),
            Size = new Size(500, 52)
        };
        pageConfirm.Controls.Add(chkRemoveData);

        // Action Buttons Row at Bottom
        btnCancel = new ModernButton
        {
            Text = "Abbrechen",
            Location = new Point(leftMargin + 265, 442),
            Size = new Size(110, 36),
            CornerRadius = 6
        };
        btnCancel.Click += (_, _) => Application.Exit();
        pageConfirm.Controls.Add(btnCancel);

        btnUninstall = new ModernButton
        {
            Text = "Deinstallieren",
            Location = new Point(leftMargin + 385, 442),
            Size = new Size(115, 36),
            IsCyanAccent = true, // matching Bild 2 cyan button
            CornerRadius = 6
        };
        btnUninstall.Click += (_, _) =>
        {
            ShowPage(pageProgress);
            StartUninstallAsync();
        };
        pageConfirm.Controls.Add(btnUninstall);

        mainContainer.Controls.Add(pageConfirm);
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
            Text = "NEXT : XLR wird deinstalliert...",
            Font = new Font("Segoe UI Variable Display", 21f, FontStyle.Bold),
            ForeColor = Color.FromArgb(248, 250, 252),
            AutoSize = true,
            Location = new Point(leftMargin, 26)
        };
        pageProgress.Controls.Add(lblProgressTitle);

        lblProgressSubtitle = new Label
        {
            Text = "Laufende Hintergrunddienste und Prozesse werden beendet...",
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
            Text = "Deinstallation wird vorbereitet...",
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

    private void BuildDonePage()
    {
        pageDone = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Visible = false
        };

        int leftMargin = 32;

        var lblDoneTitle = new Label
        {
            Text = "Deinstallation abgeschlossen",
            Font = new Font("Segoe UI Variable Display", 21f, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 189, 248),
            AutoSize = true,
            Location = new Point(leftMargin, 26)
        };
        pageDone.Controls.Add(lblDoneTitle);

        var lblDoneSubtitle = new Label
        {
            Text = "NEXT : XLR wurde erfolgreich von deinem Computer entfernt. Deine Windows-Standardeinstellungen wurden wiederhergestellt.",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(203, 213, 225),
            Location = new Point(leftMargin, 68),
            Size = new Size(500, 48)
        };
        pageDone.Controls.Add(lblDoneSubtitle);

        btnFinish = new ModernButton
        {
            Text = "Schließen",
            Location = new Point(leftMargin + 370, 442),
            Size = new Size(130, 36),
            IsCyanAccent = true,
            CornerRadius = 6
        };
        btnFinish.Click += (_, _) => Application.Exit();
        pageDone.Controls.Add(btnFinish);

        mainContainer.Controls.Add(pageDone);
    }

    private void ShowPage(Panel target)
    {
        pageConfirm.Visible = (target == pageConfirm);
        pageProgress.Visible = (target == pageProgress);
        pageDone.Visible = (target == pageDone);
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

    private async void StartUninstallAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                SetProgress(10, "Beende laufende Prozesse...");
                Log("Beende NEXT : XLR und mixforge Prozesse...");
                KillProcess("NextXLR");
                KillProcess("mixforged");
                KillProcess("mixforge");

                SetProgress(30, "Entferne Autostart...");
                Log("Entferne Windows Autostart...");
                RemoveAutostart();

                SetProgress(50, "Lösche Verknüpfungen...");
                Log("Lösche Startmenü- und Desktop-Verknüpfungen...");
                RemoveShortcuts();

                SetProgress(70, "Entferne Windows-Registrierung...");
                Log("Entferne Deinstallations-Eintrag aus der Registrierung...");
                RemoveUninstallRegistration();

                SetProgress(85, "Lösche Anwendungsdateien...");
                Log($"Lösche Anwendungsdateien in: {installDir}");
                DeleteAppDirectory(installDir);

                if (chkRemoveData.Checked)
                {
                    SetProgress(92, "Lösche Konfigurations- und Profildaten...");
                    Log("Lösche NEXT : XLR Benutzerordner in %LOCALAPPDATA%...");
                    RemoveUserData();
                }
                else
                {
                    Log("Benutzerkonfiguration und Profile bleiben in %LOCALAPPDATA%\\NextXLR erhalten.");
                }

                SetProgress(100, "Deinstallation erfolgreich abgeschlossen.");
                Log("Deinstallation abgeschlossen.");

                this.Invoke(new Action(() => ShowPage(pageDone)));
            }
            catch (Exception ex)
            {
                Log($"FEHLER: {ex.Message}");
                this.Invoke(new Action(() =>
                {
                    MessageBox.Show($"Während der Deinstallation ist ein Fehler aufgetreten:\n\n{ex.Message}", "NEXT : XLR Deinstallation", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
        });
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

    private void RemoveAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            key.DeleteValue("NextXLR", throwOnMissingValue: false);
            key.DeleteValue("GoXLRNext", throwOnMissingValue: false);
            key.DeleteValue("NextXLR_BackgroundService", throwOnMissingValue: false);
            key.DeleteValue("mixforge", throwOnMissingValue: false);
            key.DeleteValue("mixforged", throwOnMissingValue: false);
        }
        catch { }
    }

    private void RemoveShortcuts()
    {
        try
        {
            var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NEXT XLR.lnk");
            if (File.Exists(desktop)) File.Delete(desktop);

            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "plysystems");
            if (Directory.Exists(startMenu))
            {
                Directory.Delete(startMenu, true);
            }
        }
        catch { }
    }

    private void RemoveUninstallRegistration()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NextXLR", throwOnMissingSubKey: false);
        }
        catch { }
    }

    private void DeleteAppDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;

        try
        {
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch { }
            }

            foreach (var sub in Directory.GetDirectories(dir))
            {
                try { Directory.Delete(sub, true); } catch { }
            }

            try { Directory.Delete(dir, false); } catch { }
        }
        catch { }
    }

    private void RemoveUserData()
    {
        try
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextXLR");
            if (Directory.Exists(appData)) Directory.Delete(appData, true);

            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoXLRNext");
            if (Directory.Exists(legacy)) Directory.Delete(legacy, true);
        }
        catch { }
    }
}
