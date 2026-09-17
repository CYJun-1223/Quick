namespace Quick;

internal sealed class EditorScrollbar : Control
{
    private const int EmGetFirstVisibleLine = 0x00CE;
    private const int EmLineScroll = 0x00B6;

    private TextBox? _editor;
    private int _firstLine;
    private int _maxFirstLine;
    private int _trackHeight;
    private int _thumbTop;
    private int _thumbHeight;
    private int _dragStartY;
    private int _dragStartLine;
    private bool _dragging;
    private bool _hovered;

    public EditorScrollbar()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.FromArgb(31, 29, 34);
        Cursor = Cursors.Hand;
    }

    public void Attach(TextBox editor)
    {
        _editor = editor;
    }

    public bool HasOverflow => _maxFirstLine > 0;

    public void UpdateFromEditor()
    {
        if (_editor is null || !_editor.IsHandleCreated)
        {
            return;
        }

        var totalLines = Math.Max(1, _editor.Lines.Length);
        var visibleLines = GetVisibleLines(totalLines);
        _maxFirstLine = Math.Max(0, totalLines - visibleLines);
        _firstLine = Math.Clamp(GetFirstVisibleLine(), 0, _maxFirstLine);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new Rectangle(5, 4, 6, Math.Max(1, Height - 8));
        _trackHeight = track.Height;
        using var trackBrush = new SolidBrush(Color.FromArgb(58, 53, 62));
        FillRoundedRectangle(e.Graphics, trackBrush, track, 3);

        var totalLines = Math.Max(1, _editor?.Lines.Length ?? 1);
        var visibleLines = GetVisibleLines(totalLines);
        _thumbHeight = _maxFirstLine == 0
            ? Math.Min(track.Height, 18)
            : Math.Max(18, (int)Math.Round(track.Height * visibleLines / (double)totalLines));
        var travel = Math.Max(0, track.Height - _thumbHeight);
        _thumbTop = track.Top + (_maxFirstLine == 0
            ? 0
            : (int)Math.Round(travel * _firstLine / (double)_maxFirstLine));

        using var thumbBrush = new SolidBrush(_hovered || _dragging
            ? Color.FromArgb(240, 166, 106)
            : Color.FromArgb(174, 103, 91));
        FillRoundedRectangle(
            e.Graphics,
            thumbBrush,
            new Rectangle(4, _thumbTop, 8, _thumbHeight),
            4);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_dragging)
        {
            _hovered = false;
            Invalidate();
        }

        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _maxFirstLine == 0)
        {
            return;
        }

        if (e.Y >= _thumbTop && e.Y <= _thumbTop + _thumbHeight)
        {
            _dragging = true;
            _dragStartY = e.Y;
            _dragStartLine = _firstLine;
            Capture = true;
            Invalidate();
            return;
        }

        var page = GetVisibleLines(Math.Max(1, _editor?.Lines.Length ?? 1));
        ScrollBy(e.Y < _thumbTop ? -page : page);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        var travel = Math.Max(1, _trackHeight - _thumbHeight);
        var line = _dragStartLine +
                   (int)Math.Round((e.Y - _dragStartY) * _maxFirstLine / (double)travel);
        ScrollToLine(line);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = false;
            Capture = false;
            Invalidate();
        }

        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollFromWheel(e.Delta);
        base.OnMouseWheel(e);
    }

    public void ScrollFromWheel(int delta)
    {
        ScrollBy(delta > 0 ? -3 : 3);
    }

    private void ScrollBy(int delta)
    {
        ScrollToLine(_firstLine + delta);
    }

    private void ScrollToLine(int line)
    {
        if (_editor is null || !_editor.IsHandleCreated)
        {
            return;
        }

        var target = Math.Clamp(line, 0, _maxFirstLine);
        if (target == _firstLine)
        {
            return;
        }

        SendMessage(_editor.Handle, EmLineScroll, IntPtr.Zero, (IntPtr)(target - _firstLine));
        UpdateFromEditor();
    }

    private int GetVisibleLines(int totalLines) =>
        Math.Max(1, Math.Min(totalLines, (Height - 8) / Math.Max(1, _editor?.Font.Height ?? 1)));

    private int GetFirstVisibleLine() =>
        _editor is not null && _editor.IsHandleCreated
            ? SendMessage(_editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32()
            : 0;

    internal static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, radius * 2, radius * 2, 180, 90);
        path.AddArc(bounds.Right - radius * 2, bounds.Top, radius * 2, radius * 2, 270, 90);
        path.AddArc(bounds.Right - radius * 2, bounds.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
