namespace Quick;

internal sealed class CodexUsageView : Control
{
    private enum ViewState
    {
        Loading,
        Ready,
        Error
    }

    private static readonly Color Canvas = Color.FromArgb(24, 22, 30);
    private static readonly Color Panel = Color.FromArgb(34, 30, 42);
    private static readonly Color PanelMuted = Color.FromArgb(30, 27, 38);
    private static readonly Color Border = Color.FromArgb(61, 52, 70);
    private static readonly Color TextColor = Color.FromArgb(247, 242, 234);
    private static readonly Color Muted = Color.FromArgb(164, 155, 172);
    private static readonly Color Dim = Color.FromArgb(121, 113, 132);
    private static readonly Color Mint = Color.FromArgb(131, 224, 181);
    private static readonly Color Coral = Color.FromArgb(255, 151, 121);

    private ViewState _state = ViewState.Loading;
    private CodexUsageSnapshot? _snapshot;
    private string _message = string.Empty;
    private bool _refreshing;
    private bool _cached;

    public CodexUsageView()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Canvas;
    }

    public void SetLoading()
    {
        _state = ViewState.Loading;
        _snapshot = null;
        _message = string.Empty;
        _refreshing = false;
        _cached = false;
        Invalidate();
    }

    public void SetSnapshot(CodexUsageSnapshot snapshot)
    {
        _state = ViewState.Ready;
        _snapshot = snapshot;
        _message = string.Empty;
        _refreshing = false;
        _cached = false;
        Invalidate();
    }

    public void SetRefreshing(CodexUsageSnapshot snapshot)
    {
        _state = ViewState.Ready;
        _snapshot = snapshot;
        _message = string.Empty;
        _refreshing = true;
        _cached = false;
        Invalidate();
    }

    public void SetCached(CodexUsageSnapshot snapshot)
    {
        _state = ViewState.Ready;
        _snapshot = snapshot;
        _message = string.Empty;
        _refreshing = false;
        _cached = true;
        Invalidate();
    }

    public void SetError(string message)
    {
        _state = ViewState.Error;
        _snapshot = null;
        _message = message;
        _refreshing = false;
        _cached = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var background = new SolidBrush(Canvas);
        e.Graphics.FillRectangle(background, ClientRectangle);
        DrawHeader(e.Graphics);

        if (_state == ViewState.Loading)
        {
            DrawLoading(e.Graphics);
        }
        else if (_state == ViewState.Error)
        {
            DrawError(e.Graphics);
        }
        else if (_snapshot is { } snapshot)
        {
            DrawSnapshot(e.Graphics, snapshot);
        }

    }

    private void DrawHeader(Graphics graphics)
    {
        using var titleFont = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var detailFont = new Font("Segoe UI", 8.5f);
        using var titleBrush = new SolidBrush(TextColor);
        using var detailBrush = new SolidBrush(Muted);

        graphics.DrawString("Codex usage", titleFont, titleBrush, new PointF(24, 17));
        graphics.DrawString("Your local rate limits", detailFont, detailBrush, new PointF(24, 39));

        var status = _state switch
        {
            ViewState.Loading => "Reading…",
            ViewState.Error => "Unavailable",
            _ when _refreshing => "Updating…",
            _ when _cached => "Cached",
            _ => "Connected"
        };
        var statusWidth = status switch
        {
            "Unavailable" => 106,
            "Updating…" => 99,
            "Reading…" => 92,
            "Cached" => 82,
            _ => 96
        };
        var statusBounds = new Rectangle(Width - statusWidth - 24, 17, statusWidth, 24);
        var statusColor = _state switch
        {
            ViewState.Loading => Color.FromArgb(241, 184, 103),
            ViewState.Error => Coral,
            _ when _refreshing => Color.FromArgb(241, 184, 103),
            _ when _cached => Color.FromArgb(188, 166, 255),
            _ => Mint
        };

        using var pillBrush = new SolidBrush(Color.FromArgb(46, 41, 54));
        EditorScrollbar.FillRoundedRectangle(graphics, pillBrush, statusBounds, 12);
        using var statusBrush = new SolidBrush(statusColor);
        graphics.FillEllipse(statusBrush, statusBounds.Left + 11, statusBounds.Top + 8, 8, 8);
        using var statusFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var statusTextBrush = new SolidBrush(TextColor);
        graphics.DrawString(status, statusFont, statusTextBrush, statusBounds.Left + 25, statusBounds.Top + 4);
    }

    private void DrawSnapshot(Graphics graphics, CodexUsageSnapshot snapshot)
    {
        var bounds = new Rectangle(22, 64, Math.Max(1, Width - 44), 58);
        if (snapshot.Primary is { } primary)
        {
            DrawQuotaWindow(graphics, primary, bounds, AccentFor(primary.UsedPercent));
        }
        else
        {
            DrawMissingWindow(graphics, "Session", bounds);
        }

        bounds.Y += 80;
        if (snapshot.Secondary is { } secondary)
        {
            DrawQuotaWindow(graphics, secondary, bounds, AccentFor(secondary.UsedPercent));
        }
        else
        {
            DrawMissingWindow(graphics, "Weekly", bounds);
        }
    }

    private static void DrawQuotaWindow(Graphics graphics, CodexUsageWindow window, Rectangle bounds, Color accent)
    {
        var remaining = 100 - window.UsedPercent;
        using var labelFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var metaFont = new Font("Segoe UI", 8.5f);
        using var labelBrush = new SolidBrush(TextColor);
        using var metaBrush = new SolidBrush(Muted);

        graphics.DrawString(
            LauncherForm.FormatCodexWindowLabel(window.WindowDurationMins),
            labelFont,
            labelBrush,
            new PointF(bounds.Left, bounds.Top));
        DrawMeter(graphics, new Rectangle(bounds.Left, bounds.Top + 22, bounds.Width, 7), remaining, accent);
        graphics.DrawString($"{remaining}% left", metaFont, metaBrush, new PointF(bounds.Left, bounds.Top + 35));
        DrawRightAligned(
            graphics,
            window.ResetsAt is long timestamp ? LauncherForm.FormatCodexReset(timestamp) : "Reset unavailable",
            metaFont,
            metaBrush,
            new RectangleF(bounds.Left + bounds.Width / 2f, bounds.Top + 35, bounds.Width / 2f, 18));
    }

    private static void DrawMissingWindow(Graphics graphics, string label, Rectangle bounds)
    {
        using var labelFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var metaFont = new Font("Segoe UI", 8.5f);
        using var labelBrush = new SolidBrush(Muted);
        using var metaBrush = new SolidBrush(Dim);
        graphics.DrawString(label, labelFont, labelBrush, new PointF(bounds.Left, bounds.Top));
        DrawMeter(graphics, new Rectangle(bounds.Left, bounds.Top + 22, bounds.Width, 7), 0, Dim);
        graphics.DrawString("No data", metaFont, metaBrush, new PointF(bounds.Left, bounds.Top + 35));
    }

    private void DrawLoading(Graphics graphics)
    {
        var bounds = new Rectangle(22, 64, Width - 44, 185);
        DrawPanel(graphics, bounds, Panel, Border);
        using var accentPen = new Pen(Color.FromArgb(241, 184, 103), 3f);
        graphics.DrawArc(accentPen, bounds.Left + 24, bounds.Top + 51, 30, 30, 35, 285);
        using var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 9f);
        using var titleBrush = new SolidBrush(TextColor);
        using var bodyBrush = new SolidBrush(Muted);
        graphics.DrawString("Reading local Codex limits", titleFont, titleBrush, new PointF(bounds.Left + 70, bounds.Top + 48));
        graphics.DrawString("Checking the local CLI response", bodyFont, bodyBrush, new PointF(bounds.Left + 70, bounds.Top + 77));
    }

    private void DrawError(Graphics graphics)
    {
        var bounds = new Rectangle(22, 64, Width - 44, 185);
        DrawPanel(graphics, bounds, Panel, Border);
        using var accentBrush = new SolidBrush(Coral);
        graphics.FillEllipse(accentBrush, bounds.Left + 25, bounds.Top + 49, 28, 28);
        using var iconFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 9f);
        using var footerFont = new Font("Segoe UI", 8f);
        using var iconBrush = new SolidBrush(Canvas);
        using var titleBrush = new SolidBrush(TextColor);
        using var bodyBrush = new SolidBrush(Muted);
        using var footerBrush = new SolidBrush(Dim);
        graphics.DrawString("!", iconFont, iconBrush, new PointF(bounds.Left + 33, bounds.Top + 52));
        graphics.DrawString("Usage unavailable", titleFont, titleBrush, new PointF(bounds.Left + 70, bounds.Top + 47));
        graphics.DrawString(_message, bodyFont, bodyBrush, new RectangleF(bounds.Left + 70, bounds.Top + 77, bounds.Width - 92, 42));
        graphics.DrawString("Switch tools and return to retry", footerFont, footerBrush, new PointF(bounds.Left + 70, bounds.Bottom - 35));
    }

    private static void DrawPanel(Graphics graphics, Rectangle bounds, Color fillColor, Color borderColor)
    {
        using var fillBrush = new SolidBrush(fillColor);
        EditorScrollbar.FillRoundedRectangle(graphics, fillBrush, bounds, 12);
        using var borderPen = new Pen(borderColor);
        using var path = CreateRoundedPath(bounds, 12);
        graphics.DrawPath(borderPen, path);
    }

    private static void DrawMeter(Graphics graphics, Rectangle bounds, int value, Color fillColor)
    {
        var clampedValue = Math.Clamp(value, 0, 100);
        using var trackBrush = new SolidBrush(Color.FromArgb(58, 51, 66));
        EditorScrollbar.FillRoundedRectangle(graphics, trackBrush, bounds, bounds.Height / 2);

        var filledWidth = (int)Math.Round(bounds.Width * clampedValue / 100d);
        if (filledWidth <= 0)
        {
            return;
        }

        using var fillBrush = new SolidBrush(fillColor);
        var fillBounds = new Rectangle(bounds.Left, bounds.Top, Math.Min(bounds.Width, filledWidth), bounds.Height);
        if (fillBounds.Width < bounds.Height)
        {
            graphics.FillRectangle(fillBrush, fillBounds);
        }
        else
        {
            EditorScrollbar.FillRoundedRectangle(graphics, fillBrush, fillBounds, bounds.Height / 2);
        }
    }

    private static void DrawRightAligned(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static Color AccentFor(int usedPercent) => usedPercent switch
    {
        >= 85 => Coral,
        >= 65 => Color.FromArgb(241, 184, 103),
        _ => Mint
    };

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, radius * 2, radius * 2, 180, 90);
        path.AddArc(bounds.Right - radius * 2, bounds.Top, radius * 2, radius * 2, 270, 90);
        path.AddArc(bounds.Right - radius * 2, bounds.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        return path;
    }
}
