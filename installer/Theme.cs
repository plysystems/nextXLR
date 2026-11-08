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

    public static readonly Font TitleFont = new Font("Segoe UI Variable Display", 15f, FontStyle.Bold);
    public static readonly Font SubtitleFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
    public static readonly Font BodyFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
    public static readonly Font BoldFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
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

    public static Icon? GetAppIcon()
    {
        try
        {
            var asm = typeof(NextXLRTheme).Assembly;
            var stream = asm.GetManifestResourceStream("NextXLR.Installer.nextXLR_icon.ico")
                      ?? asm.GetManifestResourceStream("nextXLR_icon.ico");
            if (stream != null) return new Icon(stream);

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "nextXLR_icon.ico"),
                Path.Combine(AppContext.BaseDirectory, "Assets", "nextXLR_icon.ico"),
                Path.Combine(AppContext.BaseDirectory, "..", "nextXLR_icon.ico")
            };
            foreach (var path in candidates)
            {
                if (File.Exists(path)) return new Icon(path);
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
}

public class RoundedCard : Panel
{
    public int CornerRadius { get; set; } = 14;
    public Color BorderColor { get; set; } = NextXLRTheme.Stroke;
    public int BorderWidth { get; set; } = 1;

    public RoundedCard()
    {
        this.DoubleBuffered = true;
        this.BackColor = NextXLRTheme.SurfaceL2;
        this.Padding = new Padding(16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

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
    public bool IsAccent { get; set; } = false;
    public bool IsDanger { get; set; } = false;
    public int CornerRadius { get; set; } = 10;

    private bool _isHovered = false;
    private bool _isPressed = false;

    public ModernButton()
    {
        this.DoubleBuffered = true;
        this.FlatStyle = FlatStyle.Flat;
        this.FlatAppearance.BorderSize = 0;
        this.Cursor = Cursors.Hand;
        this.Font = NextXLRTheme.BoldFont;
        this.Size = new Size(120, 38);
    }

    protected override void OnMouseEnter(EventArgs e) { _isHovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _isHovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _isPressed = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _isPressed = false; Invalidate(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = NextXLRTheme.CreateRoundedRectangle(rect, CornerRadius);

        Color bg;
        Color fg;
        Color border;

        if (!Enabled)
        {
            bg = NextXLRTheme.SurfaceL2;
            fg = NextXLRTheme.TextSubtle;
            border = NextXLRTheme.StrokeSubtle;
        }
        else if (IsDanger)
        {
            bg = _isPressed ? NextXLRTheme.DangerDark : (_isHovered ? Color.FromArgb(250, 90, 115) : NextXLRTheme.Danger);
            fg = Color.White;
            border = bg;
        }
        else if (IsAccent)
        {
            bg = _isPressed ? NextXLRTheme.BrandDark : (_isHovered ? NextXLRTheme.Brand : NextXLRTheme.BrandAccent);
            fg = NextXLRTheme.DarkBg;
            border = bg;
        }
        else
        {
            bg = _isPressed ? NextXLRTheme.SurfaceL1 : (_isHovered ? NextXLRTheme.SurfaceL4 : NextXLRTheme.SurfaceL3);
            fg = NextXLRTheme.TextPrimary;
            border = _isHovered ? NextXLRTheme.Brand : NextXLRTheme.Stroke;
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
        this.DoubleBuffered = true;
        this.Height = 14;
        this.BackColor = NextXLRTheme.DarkBg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var bgPath = NextXLRTheme.CreateRoundedRectangle(rect, Height / 2);
        using var bgBrush = new SolidBrush(NextXLRTheme.SurfaceL1);
        g.FillPath(bgBrush, bgPath);

        using var strokePen = new Pen(NextXLRTheme.Stroke, 1f);
        g.DrawPath(strokePen, bgPath);

        if (_value > 0)
        {
            int fillW = (int)((Width - 2) * (_value / 100f));
            if (fillW > Height)
            {
                var fillRect = new Rectangle(1, 1, fillW, Height - 2);
                using var fillPath = NextXLRTheme.CreateRoundedRectangle(fillRect, (Height - 2) / 2);
                using var fillBrush = new LinearGradientBrush(fillRect, NextXLRTheme.BrandAccent, NextXLRTheme.Brand, LinearGradientMode.Horizontal);
                g.FillPath(fillBrush, fillPath);
            }
        }
    }
}
