using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NextXLR.Installer;

public static class NextXLRTheme
{
    // Design Tokens from NEXT : XLR
    public static readonly Color DarkBg = Color.FromArgb(12, 15, 20);          // #0C0F14 (Deep window background)
    public static readonly Color SurfaceL1 = Color.FromArgb(21, 24, 30);        // #15181E
    public static readonly Color SurfaceL2 = Color.FromArgb(27, 31, 39);        // #1B1F27 (Cards)
    public static readonly Color SurfaceL3 = Color.FromArgb(35, 40, 51);        // #232833 (Sub-cards, Inputs)
    public static readonly Color SurfaceL4 = Color.FromArgb(45, 52, 66);        // #2D3442 (Hover)

    public static readonly Color Stroke = Color.FromArgb(42, 48, 60);           // #2A303C
    public static readonly Color StrokeSubtle = Color.FromArgb(32, 37, 46);     // #20252E

    public static readonly Color Brand = Color.FromArgb(79, 224, 184);          // #4FE0B8
    public static readonly Color BrandAccent = Color.FromArgb(31, 199, 154);    // #1FC79A
    public static readonly Color BrandDark = Color.FromArgb(18, 138, 106);      // #128A6A

    public static readonly Color TextPrimary = Color.FromArgb(240, 243, 246);  // #F0F3F6
    public static readonly Color TextMuted = Color.FromArgb(139, 147, 163);     // #8B93A3
    public static readonly Color TextSubtle = Color.FromArgb(90, 98, 112);

    public static readonly Color Danger = Color.FromArgb(240, 80, 106);         // #F0506A
    public static readonly Color DangerDark = Color.FromArgb(160, 40, 60);
    public static readonly Color Warning = Color.FromArgb(245, 185, 66);        // #F5B942
    public static readonly Color Success = Color.FromArgb(31, 199, 154);        // #1FC79A

    public static readonly Color CyanAccent = Color.FromArgb(56, 189, 248);       // #38BDF8
    public static readonly Color CyanAccentHover = Color.FromArgb(14, 165, 233);  // #0EA5E9
    public static readonly Color CyanAccentPressed = Color.FromArgb(2, 132, 199); // #0284C7
    public static readonly Color CyanAccentText = Color.FromArgb(15, 23, 42);      // #0F172A

    public static readonly Font TitleFont = new Font("Segoe UI Variable Display", 13.5f, FontStyle.Bold);
    public static readonly Font SubtitleFont = new Font("Segoe UI", 9f, FontStyle.Regular);
    public static readonly Font BodyFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
    public static readonly Font BoldFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
    public static readonly Font ButtonFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
    public static readonly Font MonospaceFont = new Font("Consolas", 8.5f, FontStyle.Regular);

    public static Image? GetLogoImage()
    {
        try
        {
            var asm = typeof(NextXLRTheme).Assembly;
            var stream = asm.GetManifestResourceStream("NextXLR.Installer.nextXLR_logo.png")
                      ?? asm.GetManifestResourceStream("nextXLR_logo.png");
            if (stream != null) return Image.FromStream(stream);

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "nextXLR_logo.png"),
                Path.Combine(AppContext.BaseDirectory, "Assets", "nextXLR_logo.png"),
                Path.Combine(AppContext.BaseDirectory, "..", "nextXLR_logo.png")
            };
            foreach (var path in candidates)
            {
                if (File.Exists(path)) return Image.FromFile(path);
            }
        }
        catch { }
        return null;
    }

    public static Icon? GetAppIcon(int size = 128)
    {
        try
        {
            var asm = typeof(NextXLRTheme).Assembly;
            var stream = asm.GetManifestResourceStream("NextXLR.Installer.nextXLR_icon.ico")
                      ?? asm.GetManifestResourceStream("nextXLR_icon.ico");
            if (stream != null) return new Icon(stream, size, size);

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "nextXLR_icon.ico"),
                Path.Combine(AppContext.BaseDirectory, "Assets", "nextXLR_icon.ico"),
                Path.Combine(AppContext.BaseDirectory, "..", "nextXLR_icon.ico")
            };
            foreach (var path in candidates)
            {
                if (File.Exists(path)) return new Icon(path, size, size);
            }
        }
        catch { }
        return null;
    }

    public static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Applies native Windows 11/10 Immersive Dark Mode, rounded corners, and Mica backdrop to the Form.
    /// </summary>
    public static void ApplyNativeWinUIStyle(Form form)
    {
        try
        {
            int darkMode = 1;
            DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref darkMode, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 19 /* DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 */, ref darkMode, sizeof(int));

            int corner = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(form.Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref corner, sizeof(int));

            int backdrop = 2; // DWMSBT_MAINWINDOW (Mica)
            DwmSetWindowAttribute(form.Handle, 38 /* DWMWA_SYSTEMBACKDROP_TYPE */, ref backdrop, sizeof(int));
        }
        catch { }
    }
}

public class GradientSidebarPanel : Panel
{
    private Image? _appIconImage;

    public GradientSidebarPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        this.DoubleBuffered = true;
        this.Dock = DockStyle.None;
        this.Width = 280;

        var icon = NextXLRTheme.GetAppIcon(128);
        if (icon != null)
        {
            _appIconImage = icon.ToBitmap();
        }
        else
        {
            _appIconImage = NextXLRTheme.GetLogoImage();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 1. Rich vertical-diagonal gradient matching Bild 1 & Bild 2
        var rect = new Rectangle(0, 0, Width, Height);
        using (var brush = new LinearGradientBrush(rect, Color.Empty, Color.Empty, 105f))
        {
            var blend = new ColorBlend(4)
            {
                Positions = [0.0f, 0.35f, 0.70f, 1.0f],
                Colors = [
                    Color.FromArgb(244, 91, 61),   // Coral Orange / Red (#F45B3D)
                    Color.FromArgb(223, 47, 121),  // Berry Magenta (#DF2F79)
                    Color.FromArgb(123, 57, 237),  // Purple / Violet (#7B39ED)
                    Color.FromArgb(59, 70, 185)    // Deep Twilight Indigo (#3B46B9)
                ]
            };
            brush.InterpolationColors = blend;
            g.FillRectangle(brush, rect);
        }

        // 2. Subtle translucent glass box for app icon
        int iconBoxSize = 96;
        int iconBoxX = (Width - iconBoxSize) / 2;
        int iconBoxY = 135;
        var iconBoxRect = new Rectangle(iconBoxX, iconBoxY, iconBoxSize, iconBoxSize);

        using (var iconPath = NextXLRTheme.CreateRoundedRectangle(iconBoxRect, 22))
        {
            using (var glassBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            {
                g.FillPath(glassBrush, iconPath);
            }
            using (var glassPen = new Pen(Color.FromArgb(55, 255, 255, 255), 1.2f))
            {
                g.DrawPath(glassPen, iconPath);
            }
        }

        // 3. Draw App Icon centered
        if (_appIconImage != null)
        {
            int drawSize = 72;
            int drawX = iconBoxX + (iconBoxSize - drawSize) / 2;
            int drawY = iconBoxY + (iconBoxSize - drawSize) / 2;
            g.DrawImage(_appIconImage, new Rectangle(drawX, drawY, drawSize, drawSize));
        }

        // 4. Draw Title "NEXT : XLR"
        using (var titleFont = new Font("Segoe UI Variable Display", 22f, FontStyle.Bold))
        {
            var titleRect = new Rectangle(12, iconBoxY + iconBoxSize + 16, Width - 24, 40);
            TextRenderer.DrawText(g, "NEXT : XLR", titleFont, titleRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
        }

        // 5. Draw Subtitle
        using (var subFont = new Font("Segoe UI", 9.5f, FontStyle.Regular))
        {
            var subRect = new Rectangle(12, iconBoxY + iconBoxSize + 56, Width - 24, 25);
            TextRenderer.DrawText(g, "Live-Audio für Windows", subFont, subRect, Color.FromArgb(235, 255, 255, 255), TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
        }

        // 6. Draw Footer Info at Bottom
        int footerX = 26;
        int footerY1 = Height - 52;
        int footerY2 = Height - 32;

        using (var verFont = new Font("Segoe UI", 9f, FontStyle.Bold))
        {
            TextRenderer.DrawText(g, "Version 0.1.5-alpha", verFont, new Point(footerX, footerY1), Color.FromArgb(245, 255, 255, 255), TextFormatFlags.NoPrefix);
        }

        using (var copyFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
        {
            TextRenderer.DrawText(g, "Kostenlos & Open Source · MIT-Lizenz", copyFont, new Point(footerX, footerY2), Color.FromArgb(205, 255, 255, 255), TextFormatFlags.NoPrefix);
        }
    }
}

public class ModernCheckBox : Control
{
    private bool _checked = false;
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                CheckedChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    public string? SubText { get; set; }
    public event EventHandler? CheckedChanged;
    private bool _isHovered = false;

    public ModernCheckBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        this.DoubleBuffered = true;
        this.Cursor = Cursors.Hand;
        this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        this.ForeColor = Color.FromArgb(241, 245, 249);
        this.Height = 28;
    }

    protected override void OnMouseEnter(EventArgs e) { _isHovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _isHovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int boxSize = 18;
        int boxY = string.IsNullOrWhiteSpace(SubText) ? (Height - boxSize) / 2 : 4;
        var boxRect = new Rectangle(0, boxY, boxSize, boxSize);

        using (var boxPath = NextXLRTheme.CreateRoundedRectangle(boxRect, 4))
        {
            if (_checked)
            {
                using var fill = new SolidBrush(NextXLRTheme.CyanAccent); // #38BDF8
                g.FillPath(fill, boxPath);

                // Draw dark checkmark
                using var pen = new Pen(NextXLRTheme.CyanAccentText, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(pen, new[]
                {
                    new PointF(boxRect.X + 4.2f, boxRect.Y + 9.5f),
                    new PointF(boxRect.X + 7.5f, boxRect.Y + 13f),
                    new PointF(boxRect.X + 13.8f, boxRect.Y + 5.5f)
                });
            }
            else
            {
                using var fill = new SolidBrush(_isHovered ? Color.FromArgb(45, 52, 66) : Color.FromArgb(27, 31, 39));
                g.FillPath(fill, boxPath);
                using var stroke = new Pen(_isHovered ? NextXLRTheme.CyanAccent : Color.FromArgb(65, 75, 95), 1.2f);
                g.DrawPath(stroke, boxPath);
            }
        }

        int textX = boxSize + 12;
        if (string.IsNullOrWhiteSpace(SubText))
        {
            var textRect = new Rectangle(textX, 0, Width - textX, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        else
        {
            var textRect = new Rectangle(textX, 2, Width - textX, 22);
            TextRenderer.DrawText(g, Text, Font, textRect, ForeColor, TextFormatFlags.Left | TextFormatFlags.Top);

            using var subFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            var subRect = new Rectangle(textX, 25, Width - textX, Height - 25);
            TextRenderer.DrawText(g, SubText, subFont, subRect, Color.FromArgb(148, 163, 184), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }
}

public class RoundedCard : Panel
{
    public int CornerRadius { get; set; } = 12;
    public Color BorderColor { get; set; } = NextXLRTheme.Stroke;
    public int BorderWidth { get; set; } = 1;

    public RoundedCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        this.DoubleBuffered = true;
        this.BackColor = NextXLRTheme.SurfaceL2;
        this.Padding = new Padding(16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = NextXLRTheme.CreateRoundedRectangle(rect, CornerRadius);

        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);

        if (BorderWidth > 0)
        {
            using var pen = new Pen(BorderColor, BorderWidth);
            e.Graphics.DrawPath(pen, path);
        }
    }
}

public class ModernButton : Button
{
    public bool IsCyanAccent { get; set; } = false;
    public bool IsAccent { get; set; } = false;
    public bool IsDanger { get; set; } = false;
    public int CornerRadius { get; set; } = 6;

    private bool _isHovered = false;
    private bool _isPressed = false;

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        this.DoubleBuffered = true;
        this.FlatStyle = FlatStyle.Flat;
        this.FlatAppearance.BorderSize = 0;
        this.Cursor = Cursors.Hand;
        this.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        this.Size = new Size(125, 36);
    }

    protected override void OnMouseEnter(EventArgs e) { _isHovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _isHovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _isPressed = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _isPressed = false; Invalidate(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = NextXLRTheme.CreateRoundedRectangle(rect, CornerRadius);

        Color bg;
        Color fg;
        Color border;

        if (!Enabled)
        {
            bg = Color.FromArgb(26, 30, 38);
            fg = Color.FromArgb(85, 93, 107);
            border = Color.FromArgb(36, 42, 52);
        }
        else if (IsCyanAccent || IsAccent)
        {
            // Vibrant Cyan Accent matching Bild 1 & Bild 2
            bg = _isPressed ? NextXLRTheme.CyanAccentPressed : (_isHovered ? NextXLRTheme.CyanAccentHover : NextXLRTheme.CyanAccent);
            fg = NextXLRTheme.CyanAccentText;
            border = bg;
        }
        else if (IsDanger)
        {
            bg = _isPressed ? Color.FromArgb(180, 35, 45) : (_isHovered ? Color.FromArgb(235, 65, 75) : Color.FromArgb(220, 50, 60));
            fg = Color.White;
            border = bg;
        }
        else
        {
            // Dark secondary button matching Bild 1 & Bild 2
            bg = _isPressed ? Color.FromArgb(28, 31, 39) : (_isHovered ? Color.FromArgb(50, 56, 69) : Color.FromArgb(38, 41, 51));
            fg = Color.FromArgb(241, 245, 249);
            border = _isHovered ? Color.FromArgb(75, 85, 105) : Color.FromArgb(55, 62, 77);
        }

        using (var brush = new SolidBrush(bg)) g.FillPath(brush, path);
        using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);

        TextRenderer.DrawText(g, Text, Font, rect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

public class ModernProgressBar : Control
{
    private int _value = 0;
    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    public ModernProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        this.DoubleBuffered = true;
        this.Height = 10;
        this.BackColor = NextXLRTheme.DarkBg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var bgPath = NextXLRTheme.CreateRoundedRectangle(rect, Height / 2);
        using var bgBrush = new SolidBrush(Color.FromArgb(20, 24, 32));
        g.FillPath(bgBrush, bgPath);

        using var strokePen = new Pen(Color.FromArgb(38, 45, 58), 1f);
        g.DrawPath(strokePen, bgPath);

        if (_value > 0)
        {
            int fillW = (int)((Width - 2) * (_value / 100f));
            if (fillW >= Height)
            {
                var fillRect = new Rectangle(1, 1, fillW, Height - 2);
                using var fillPath = NextXLRTheme.CreateRoundedRectangle(fillRect, (Height - 2) / 2);
                using var fillBrush = new LinearGradientBrush(fillRect, NextXLRTheme.BrandAccent, NextXLRTheme.Brand, LinearGradientMode.Horizontal);
                g.FillPath(fillBrush, fillPath);
            }
        }
    }
}

public class ModernExpander : Panel
{
    private bool _isExpanded = false;
    private readonly Label _headerLabel;
    private readonly Label _chevronLabel;
    private readonly Panel _headerBar;
    private readonly TextBox _contentBox;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                _chevronLabel.Text = _isExpanded ? "⌃" : "⌵";
                this.Height = _isExpanded ? 160 : 38;
                _contentBox.Visible = _isExpanded;
                if (_isExpanded)
                {
                    this.BringToFront();
                }
                Invalidate();
            }
        }
    }

    public ModernExpander(string headerText, string bodyText)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        this.DoubleBuffered = true;
        this.BackColor = Color.FromArgb(32, 36, 46);
        this.Height = 38;
        this.Cursor = Cursors.Hand;

        _headerBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        _headerBar.Click += (_, _) => IsExpanded = !IsExpanded;

        _headerLabel = new Label
        {
            Text = headerText,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(226, 232, 240),
            AutoSize = false,
            Location = new Point(14, 8),
            Size = new Size(380, 22),
            Cursor = Cursors.Hand
        };
        _headerLabel.Click += (_, _) => IsExpanded = !IsExpanded;

        _chevronLabel = new Label
        {
            Text = "⌵",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = false,
            Dock = DockStyle.Right,
            Width = 36,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        _chevronLabel.Click += (_, _) => IsExpanded = !IsExpanded;

        _headerBar.Controls.Add(_headerLabel);
        _headerBar.Controls.Add(_chevronLabel);
        this.Controls.Add(_headerBar);

        _contentBox = new TextBox
        {
            Text = bodyText,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(26, 30, 39),
            ForeColor = Color.FromArgb(203, 213, 225),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f, FontStyle.Regular),
            Location = new Point(14, 42),
            Size = new Size(Width - 28, 105),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Visible = false
        };
        this.Controls.Add(_contentBox);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = NextXLRTheme.CreateRoundedRectangle(rect, 6);
        using var brush = new SolidBrush(BackColor);
        g.FillPath(brush, path);

        using var pen = new Pen(Color.FromArgb(51, 57, 71), 1f);
        g.DrawPath(pen, path);
    }
}

public class StepPill : Control
{
    private bool _active;
    private bool _completed;

    public bool Active { get => _active; set { _active = value; Invalidate(); } }
    public bool Completed { get => _completed; set { _completed = value; Invalidate(); } }

    public StepPill() : this("") { }
    public StepPill(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        this.DoubleBuffered = true;
        this.Text = text;
        this.Height = 26;
        this.Width = 145;
        this.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = NextXLRTheme.CreateRoundedRectangle(rect, Height / 2);

        Color bg;
        Color border;
        Color fg;

        if (_active)
        {
            bg = Color.FromArgb(20, 50, 42);
            border = NextXLRTheme.BrandAccent;
            fg = NextXLRTheme.Brand;
        }
        else if (_completed)
        {
            bg = Color.FromArgb(26, 32, 42);
            border = Color.FromArgb(45, 54, 70);
            fg = Color.FromArgb(210, 218, 230);
        }
        else
        {
            bg = Color.FromArgb(16, 19, 25);
            border = Color.FromArgb(30, 35, 45);
            fg = Color.FromArgb(95, 105, 120);
        }

        using (var brush = new SolidBrush(bg)) g.FillPath(brush, path);
        using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);

        TextRenderer.DrawText(g, Text, Font, rect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
