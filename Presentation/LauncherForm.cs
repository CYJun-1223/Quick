namespace Quick;

internal sealed partial class LauncherForm : Form
{
    private const int HotkeyId = 1001;
    private const int WmHotkey = 0x0312;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const int BaseFormHeight = 420;
    private const int BaseTopBarHeight = 58;
    private const int BaseContentTop = 84;
    private const int BaseFooterTop = 388;
    private const int SingleLineEditorHeight = 40;
    private const int MaxEditorLines = 5;
    // ponytail: 60s polling avoids a long-lived app-server connection.
    // Upgrade to account/rateLimits/updated for true realtime updates.
    private const int CodexRefreshIntervalMs = 60_000;
    private const string RateUrl = "https://open.er-api.com/v6/latest/MYR";

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly PictureBox _logo = new();
    private readonly TextBox _input = new();
    private readonly Label _inputPlaceholder = new();
    private readonly Panel _topBar = new();
    private readonly Panel _toolPane = new();
    private readonly Panel _resultSurface = new();
    private readonly System.Windows.Forms.Timer _editorResizeTimer = new();
    private readonly System.Windows.Forms.Timer _codexRefreshTimer = new();
    private readonly EditorScrollbar _editorScrollbar = new();
    private readonly Label _resultIcon = new();
    private readonly Label _result = new();
    private readonly Label _mode = new();
    private readonly Label _hint = new();
    private readonly Panel _calculatorRow = new();
    private readonly Panel _exchangeRow = new();
    private readonly Label _calculatorTitle = new();
    private readonly Label _calculatorHint = new();
    private readonly Label _exchangeTitle = new();
    private readonly Label _exchangeHint = new();
    private readonly Panel _codexRow = new();
    private readonly Label _codexTitle = new();
    private readonly Label _codexHint = new();
    private readonly CodexUsageView _codexUsageView = new();
    private readonly Label _footer = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, decimal> _ratesFromMyr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MYR"] = 1m,
        ["USD"] = 0.213m,
        ["SGD"] = 0.286m
    };

    private bool _hotkeyRegistered;
    private bool _ratesRequested;
    private bool _liveRates;
    private bool _closing;
    private Keys _hotkeyModifiers = Keys.Alt;
    private Keys _hotkeyKey = Keys.Space;
    private int _editorHeight = SingleLineEditorHeight;
    private int _targetEditorHeight = SingleLineEditorHeight;
    private bool _editorExpanded;
    private int _selectedFeatureIndex;
    private CodexUsageSnapshot? _codexUsageCache;
    private bool _codexRefreshInFlight;

    public LauncherForm()
    {
        Text = "Quick";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(760, 420);
        BackColor = Color.FromArgb(29, 29, 31);
        KeyPreview = true;
        DoubleBuffered = true;

        BuildUi();

        LoadShortcut();
        CenterOnCurrentScreen();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Quick", null, (_, _) => ShowLauncher());
        menu.Items.Add("Settings…", null, (_, _) => ShowShortcutSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Close());

        _notifyIcon = new NotifyIcon
        {
            Text = "Quick",
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowLauncher();

        Deactivate += (_, _) =>
        {
            if (!_closing)
            {
                HideLauncher();
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (!_hotkeyRegistered)
        {
            RegisterCurrentHotkey();
        }

        if (!_ratesRequested)
        {
            _ratesRequested = true;
            _ = LoadRatesAsync();
        }

        if (QuickSettingsStore.IsAutoUpdateEnabled())
        {
            SetAutoUpdateEnabled(true);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            ToggleLauncher();
        }

        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            HideLauncher();
            return true;
        }

        if (_input.TextLength == 0 && keyData is Keys.Up or Keys.Down)
        {
            MoveFeature(keyData == Keys.Down ? 1 : -1);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing = true;
        _shutdown.Cancel();
        _editorResizeTimer.Stop();
        _editorResizeTimer.Dispose();
        _codexRefreshTimer.Stop();
        _codexRefreshTimer.Dispose();
        _updateTimer.Stop();
        _updateTimer.Dispose();

        if (_hotkeyRegistered)
        {
            UnregisterHotKey(Handle, HotkeyId);
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var border = new Pen(Color.FromArgb(55, 48, 57));
        using var separator = new Pen(Color.FromArgb(52, 46, 54));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var contentTop = _toolPane.Top > 0 ? _toolPane.Top : BaseContentTop;
        e.Graphics.DrawLine(separator, 10, contentTop - 8, Width - 10, contentTop - 8);
        e.Graphics.DrawLine(separator, 264, contentTop, 264, Height - 44);
    }

    private void BuildUi()
    {
        var topBar = _topBar;
        topBar.Location = new Point(10, 10);
        topBar.Size = new Size(740, BaseTopBarHeight);
        topBar.BackColor = Color.FromArgb(31, 29, 34);
        topBar.MouseDown += BeginWindowDrag;

        _input.Location = new Point(20, 10);
        _input.Size = new Size(700, SingleLineEditorHeight);
        _input.Multiline = true;
        _input.AcceptsReturn = false;
        _input.ScrollBars = ScrollBars.None;
        _input.BorderStyle = BorderStyle.None;
        _input.BackColor = topBar.BackColor;
        _input.ForeColor = Color.FromArgb(245, 241, 235);
        _input.Font = new Font("Segoe UI", 19f, FontStyle.Regular);
        _input.PlaceholderText = "Search or type a command...";
        _input.MaxLength = 250;
        _input.TextChanged += (_, _) =>
        {
            UpdateInputPlaceholder();
            UpdateResult();
            QueueEditorResize();
            UpdateEditorScrollbar();
        };
        _input.KeyDown += HandleInputKeyDown;
        _input.KeyUp += (_, _) => UpdateEditorScrollbar();
        _input.MouseWheel += (_, e) => _editorScrollbar.ScrollFromWheel(e.Delta);
        topBar.Controls.Add(_input);

        _inputPlaceholder.Location = new Point(20, 10);
        _inputPlaceholder.Size = new Size(660, SingleLineEditorHeight);
        _inputPlaceholder.AutoEllipsis = true;
        _inputPlaceholder.BackColor = topBar.BackColor;
        _inputPlaceholder.ForeColor = Color.FromArgb(126, 120, 136);
        _inputPlaceholder.Font = new Font("Segoe UI", 17f);
        _inputPlaceholder.TextAlign = ContentAlignment.MiddleLeft;
        _inputPlaceholder.Text = "Search, calculate, or check Codex usage…";
        _inputPlaceholder.Click += (_, _) => _input.Focus();
        topBar.Controls.Add(_inputPlaceholder);
        _inputPlaceholder.BringToFront();

        _editorScrollbar.Attach(_input);
        _editorScrollbar.Location = new Point(_input.Right - 16, _input.Top);
        _editorScrollbar.Size = new Size(16, _input.Height);
        _editorScrollbar.Visible = false;
        topBar.Controls.Add(_editorScrollbar);
        _editorScrollbar.BringToFront();

        ConfigureToolRow(_calculatorRow, "∑", _calculatorTitle, _calculatorHint);
        _calculatorTitle.Text = "Calculator";
        _calculatorHint.Text = "Calculate as you type";
        _calculatorRow.Location = new Point(8, 12);
        _calculatorRow.Size = new Size(232, 60);

        ConfigureToolRow(_exchangeRow, "↔", _exchangeTitle, _exchangeHint);
        _exchangeTitle.Text = "Exchange rate";
        _exchangeHint.Text = "Convert MYR, USD, SGD";
        _exchangeRow.Location = new Point(8, 78);
        _exchangeRow.Size = new Size(232, 60);

        ConfigureToolRow(_codexRow, "◌", _codexTitle, _codexHint);
        _codexTitle.Text = "Codex usage";
        _codexHint.Text = "Show usage in panel";
        _codexRow.Location = new Point(8, 144);
        _codexRow.Size = new Size(232, 60);

        var toolPane = _toolPane;
        toolPane.Location = new Point(10, BaseContentTop);
        toolPane.Size = new Size(248, 290);
        toolPane.BackColor = Color.FromArgb(15, 13, 17);
        toolPane.Controls.Add(_calculatorRow);
        toolPane.Controls.Add(_exchangeRow);
        toolPane.Controls.Add(_codexRow);

        var resultSurface = _resultSurface;
        resultSurface.Location = new Point(270, BaseContentTop);
        resultSurface.Size = new Size(480, 290);
        resultSurface.BackColor = Color.FromArgb(28, 26, 32);

        _resultIcon.Location = new Point(28, 70);
        _resultIcon.Size = new Size(58, 58);
        _resultIcon.Text = "∑";
        _resultIcon.TextAlign = ContentAlignment.MiddleCenter;
        _resultIcon.ForeColor = Color.FromArgb(240, 166, 106);
        _resultIcon.Font = new Font("Segoe UI", 21f, FontStyle.Bold);

        _mode.Location = new Point(105, 52);
        _mode.Size = new Size(300, 20);
        _mode.TextAlign = ContentAlignment.MiddleLeft;
        _mode.ForeColor = Color.FromArgb(170, 165, 175);
        _mode.Font = new Font("Segoe UI", 9f);
        _mode.Text = "Calculator";

        _result.Location = new Point(105, 78);
        _result.Size = new Size(340, 58);
        _result.AutoEllipsis = true;
        _result.ForeColor = Color.FromArgb(245, 241, 235);
        _result.Font = new Font("Segoe UI", 25f, FontStyle.Bold);
        _result.Text = "—";

        _hint.Location = new Point(105, 145);
        _hint.Size = new Size(340, 74);
        _hint.AutoEllipsis = false;
        _hint.ForeColor = Color.FromArgb(142, 138, 148);
        _hint.Font = new Font("Segoe UI", 9f);

        _logo.Location = new Point(ClientSize.Width - 145, ClientSize.Height - 48);
        _logo.Size = new Size(128, 43);
        _logo.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _logo.BackColor = Color.Transparent;
        _logo.SizeMode = PictureBoxSizeMode.Zoom;
        _logo.Image = LoadEmbeddedLogo();

        resultSurface.Controls.AddRange([_resultIcon, _mode, _result, _hint]);
        _codexUsageView.Dock = DockStyle.Fill;
        _codexUsageView.Visible = false;
        resultSurface.Controls.Add(_codexUsageView);

        _footer.Location = new Point(28, 388);
        _footer.Size = new Size(220, 18);
        _footer.ForeColor = Color.FromArgb(126, 120, 130);
        _footer.Font = new Font("Segoe UI", 8.5f);
        _footer.Text = "Esc   Close";

        Controls.AddRange([topBar, toolPane, resultSurface, _footer, _logo]);

        _editorResizeTimer.Interval = 15;
        _editorResizeTimer.Tick += (_, _) => AnimateEditorResize();
        _codexRefreshTimer.Interval = CodexRefreshIntervalMs;
        _codexRefreshTimer.Tick += (_, _) => RefreshCodexUsage();
        _updateTimer.Interval = UpdateCheckIntervalMs;
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync();

        UpdateInputPlaceholder();
        UpdateResult();
        ApplyEditorLayout();
        ApplyRoundedCorners();
    }

    internal static Image LoadEmbeddedLogo()
    {
        using var stream = typeof(LauncherForm).Assembly.GetManifestResourceStream("Quick.Assets.logo.png")
            ?? throw new InvalidOperationException("Embedded Quick logo was not found.");
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static void ConfigureToolRow(Panel row, string icon, Label title, Label hint)
    {
        row.BackColor = Color.FromArgb(20, 17, 22);

        row.Controls.Add(new Label
        {
            Location = new Point(14, 12),
            Size = new Size(30, 34),
            Text = icon,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(215, 210, 220),
            Font = new Font("Segoe UI", 15f, FontStyle.Bold)
        });

        title.Location = new Point(56, 11);
        title.Size = new Size(162, 18);
        title.ForeColor = Color.FromArgb(245, 241, 235);
        title.Font = new Font("Segoe UI", 9f);

        hint.Location = new Point(56, 33);
        hint.Size = new Size(162, 17);
        hint.ForeColor = Color.FromArgb(135, 129, 141);
        hint.Font = new Font("Segoe UI", 8f);

        row.Controls.Add(title);
        row.Controls.Add(hint);
    }

    private void LoadShortcut()
    {
        var shortcut = QuickSettingsStore.LoadShortcut(_hotkeyModifiers, _hotkeyKey);
        _hotkeyModifiers = shortcut.Modifiers;
        _hotkeyKey = shortcut.Key;
    }

    private string FormatShortcut() => FormatShortcut(_hotkeyModifiers, _hotkeyKey);

    internal static string FormatShortcut(Keys modifiers, Keys key)
    {
        var parts = new List<string>();
        if ((modifiers & Keys.Control) != Keys.None)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & Keys.Alt) != Keys.None)
        {
            parts.Add("Alt");
        }

        if ((modifiers & Keys.Shift) != Keys.None)
        {
            parts.Add("Shift");
        }

        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    private void ShowShortcutSettings()
    {
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _hotkeyRegistered = false;
        }

        using var settings = new ShortcutSettingsForm(this, _hotkeyModifiers, _hotkeyKey);
        settings.ShowDialog();

        if (!_hotkeyRegistered)
        {
            RegisterCurrentHotkey();
        }
    }

    internal bool TrySetShortcut(Keys modifiers, Keys key, out string error)
    {
        error = string.Empty;
        if (modifiers == Keys.None || key == Keys.None)
        {
            error = "Choose a key with Alt, Ctrl, or Shift.";
            return false;
        }

        var previousModifiers = _hotkeyModifiers;
        var previousKey = _hotkeyKey;
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _hotkeyRegistered = false;
        }

        _hotkeyModifiers = modifiers;
        _hotkeyKey = key;
        _hotkeyRegistered = RegisterCurrentHotkey();
        if (!_hotkeyRegistered)
        {
            _hotkeyModifiers = previousModifiers;
            _hotkeyKey = previousKey;
            RegisterCurrentHotkey();
            error = "Windows could not register that shortcut. Choose another one.";
            return false;
        }

        if (!QuickSettingsStore.TrySaveShortcut(_hotkeyModifiers, _hotkeyKey, out error))
        {
            if (_hotkeyRegistered)
            {
                UnregisterHotKey(Handle, HotkeyId);
                _hotkeyRegistered = false;
            }

            _hotkeyModifiers = previousModifiers;
            _hotkeyKey = previousKey;
            _hotkeyRegistered = RegisterCurrentHotkey();
        }

        return error.Length == 0;
    }

    private bool RegisterCurrentHotkey()
    {
        _hotkeyRegistered = RegisterHotKey(
            Handle,
            HotkeyId,
            GetNativeModifiers(_hotkeyModifiers) | ModNoRepeat,
            (uint)_hotkeyKey);
        return _hotkeyRegistered;
    }

    private static uint GetNativeModifiers(Keys modifiers)
    {
        var nativeModifiers = 0u;
        if ((modifiers & Keys.Alt) != Keys.None)
        {
            nativeModifiers |= ModAlt;
        }

        if ((modifiers & Keys.Control) != Keys.None)
        {
            nativeModifiers |= ModControl;
        }

        if ((modifiers & Keys.Shift) != Keys.None)
        {
            nativeModifiers |= ModShift;
        }

        return nativeModifiers;
    }

    private void BeginWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    private void HandleInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        if (e.Shift)
        {
            _editorExpanded = true;
            _input.ScrollBars = ScrollBars.None;
            _input.SelectedText = Environment.NewLine;
            QueueEditorResize();
            _input.ScrollToCaret();
        }
    }

    private void QueueEditorResize()
    {
        _targetEditorHeight = _editorExpanded
            ? SingleLineEditorHeight + (MaxEditorLines - 1) * Math.Max(1, _input.Font.Height)
            : SingleLineEditorHeight;
        if (_targetEditorHeight != _editorHeight)
        {
            if (_editorExpanded && !_editorResizeTimer.Enabled)
            {
                Region = null;
            }

            _editorResizeTimer.Start();
        }
    }

    private void UpdateInputPlaceholder() => _inputPlaceholder.Visible = string.IsNullOrWhiteSpace(_input.Text);

    private void AnimateEditorResize()
    {
        var remaining = _targetEditorHeight - _editorHeight;
        if (remaining == 0)
        {
            _editorResizeTimer.Stop();
            ApplyRoundedCorners();
            UpdateEditorScrollbar();
            return;
        }

        var step = Math.Max(2, (int)Math.Ceiling(Math.Abs(remaining) * 0.24));
        _editorHeight += Math.Sign(remaining) * Math.Min(Math.Abs(remaining), step);
        ApplyEditorLayout();

        if (_editorHeight == _targetEditorHeight)
        {
            _editorResizeTimer.Stop();
            ApplyRoundedCorners();
            UpdateEditorScrollbar();
        }
    }

    private void ApplyEditorLayout()
    {
        var delta = _editorHeight - SingleLineEditorHeight;
        SuspendLayout();
        try
        {
            _input.Height = _editorHeight;
            _editorScrollbar.Location = new Point(_input.Right - 16, _input.Top);
            _editorScrollbar.Height = _input.Height;
            _topBar.Height = BaseTopBarHeight + delta;
            _toolPane.Top = BaseContentTop + delta;
            _resultSurface.Top = BaseContentTop + delta;
            _footer.Top = BaseFooterTop + delta;
            ClientSize = new Size(ClientSize.Width, BaseFormHeight + delta);
        }
        finally
        {
            ResumeLayout(false);
        }

        UpdateEditorScrollbar();
        Invalidate();
    }

    private void UpdateEditorScrollbar()
    {
        _editorScrollbar.UpdateFromEditor();
        _editorScrollbar.Visible = _editorExpanded &&
                                    !_editorResizeTimer.Enabled &&
                                    _editorScrollbar.HasOverflow;
    }

    private void MoveFeature(int delta)
    {
        var next = (_selectedFeatureIndex + delta + 3) % 3;
        SelectFeature(next);
    }

    private void SelectFeature(int index)
    {
        _selectedFeatureIndex = index;
        if (index == 2)
        {
            SetMode("Codex usage", "◌");
            if (_codexUsageCache is { } cached)
            {
                _codexUsageView.SetRefreshing(cached);
            }
            else
            {
                _codexUsageView.SetLoading();
            }

            RefreshCodexUsage();
            return;
        }

        if (index == 1)
        {
            SetMode("Exchange", "↔");
            _result.Text = string.Empty;
            _hint.Text = string.Empty;
            return;
        }

        UpdateResult();
    }

    private void RefreshCodexUsage()
    {
        if (_closing || _selectedFeatureIndex != 2 || _codexRefreshInFlight)
        {
            return;
        }

        _codexRefreshInFlight = true;
        if (_codexUsageCache is { } cached && _selectedFeatureIndex == 2)
        {
            _codexUsageView.SetRefreshing(cached);
        }

        _ = LoadCodexUsageAsync();
    }

    private async Task LoadCodexUsageAsync()
    {
        try
        {
            var read = await CodexUsageReader.ReadAsync(_shutdown.Token);
            if (read.Snapshot is { } snapshot)
            {
                _codexUsageCache = snapshot;
                if (_selectedFeatureIndex == 2)
                {
                    _codexUsageView.SetSnapshot(snapshot);
                }

                return;
            }

            if (_selectedFeatureIndex == 2)
            {
                if (_codexUsageCache is { } cached)
                {
                    _codexUsageView.SetCached(cached);
                }
                else
                {
                    _codexUsageView.SetError(read.Error.Length > 0 ? read.Error : "Sign in to Codex CLI to read usage");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _codexRefreshInFlight = false;
        }
    }

    internal static string FormatCodexWindow(CodexUsageWindow? window)
    {
        if (window is not { } value)
        {
            return "quota unavailable";
        }

        var name = FormatCodexWindowName(value.WindowDurationMins);
        var remaining = 100 - value.UsedPercent;
        var reset = value.ResetsAt is long timestamp
            ? FormatCodexReset(timestamp)
            : "reset time unavailable";
        return $"{name} {remaining}% remaining · {reset}";
    }

    internal static string FormatCodexWindowName(long? windowDurationMins) => windowDurationMins switch
    {
        300 => "5h",
        10080 => "weekly",
        _ => "quota"
    };

    internal static string FormatCodexWindowLabel(long? windowDurationMins) => windowDurationMins switch
    {
        300 => "Session",
        10080 => "Weekly",
        _ => "Quota"
    };

    internal static string FormatCodexReset(long timestamp)
    {
        try
        {
            var remaining = DateTimeOffset.FromUnixTimeSeconds(timestamp) - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return "Resets now";
            }

            var totalMinutes = Math.Max(1, (int)remaining.TotalMinutes);
            if (totalMinutes < 60)
            {
                return $"Resets in {totalMinutes}m";
            }

            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            if (hours < 24)
            {
                return minutes == 0 ? $"Resets in {hours}h" : $"Resets in {hours}h {minutes}m";
            }

            var days = hours / 24;
            var remainingHours = hours % 24;
            return remainingHours == 0 ? $"Resets in {days}d" : $"Resets in {days}d {remainingHours}h";
        }
        catch (ArgumentOutOfRangeException)
        {
            return "Reset time unavailable";
        }
    }

    private void ToggleLauncher()
    {
        if (Visible)
        {
            HideLauncher();
            return;
        }

        ShowLauncher();
    }

    private void ShowLauncher()
    {
        CenterOnCurrentScreen();
        Show();
        WindowState = FormWindowState.Normal;
        TopMost = true;
        Activate();
        SetForegroundWindow(Handle);
        _input.Focus();
        _input.SelectAll();
    }

    private void CenterOnCurrentScreen()
    {
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            screen.Left + (screen.Width - Width) / 2,
            screen.Top + (screen.Height - Height) / 2);
    }

    private void HideLauncher()
    {
        if (!Visible)
        {
            return;
        }

        // Keep text, editor size and feature selection so the next open shows the last state.
        Hide();
    }

    private void UpdateResult()
    {
        var text = _input.Text.Trim();

        if (text.Length == 0)
        {
            SetMode("Calculator", "∑");
            _result.Text = "—";
            _result.ForeColor = Color.FromArgb(245, 241, 235);
            _hint.Text = string.Empty;
            return;
        }

        if (ExchangeQuery.TryParse(text, out var query))
        {
            UpdateCurrencyResult(query);
            return;
        }

        if (CurrencyExpressionEvaluator.TryEvaluate(text, _ratesFromMyr, out var currencyValue, out var currencyTarget))
        {
            SetMode("Exchange", "↔");
            _result.Text = $"{currencyTarget} {FormatCurrency(currencyValue)}";
            _result.ForeColor = Color.FromArgb(240, 166, 106);
            _hint.Text = $"Mixed currency result → {currencyTarget}";
            return;
        }

        if (ExpressionEvaluator.TryEvaluateScript(text, out var value))
        {
            SetMode("Calculator", "∑");
            _result.Text = FormatCalculatorNumber(value);
            _result.ForeColor = Color.FromArgb(245, 241, 235);
            _hint.Text = string.Empty;
            return;
        }

        SetMode("Calculator", "∑");
        _result.Text = string.Empty;
        _hint.Text = string.Empty;
    }

    private void UpdateCurrencyResult(CurrencyQuery query)
    {
        if (!ExchangeCalculator.TryConvert(query, _ratesFromMyr, out var converted, out _))
        {
            SetMode("Exchange", "↔");
            _result.Text = $"No rate for {query.From} → {query.To}";
            _result.ForeColor = Color.FromArgb(245, 241, 235);
            _hint.Text = _liveRates ? "Check the currency code and try again" : "Offline mode supports MYR, USD, and SGD";
            return;
        }

        SetMode("Exchange", "↔");
        _result.Text = $"{query.To} {FormatCurrency(converted)}";
        _result.ForeColor = Color.FromArgb(240, 166, 106);
        _hint.Text = $"{FormatCurrency(query.Amount)} {query.From} → {query.To}";
    }

    private void SetMode(string mode, string icon)
    {
        _mode.Text = mode;
        _resultIcon.Text = icon;
        _selectedFeatureIndex = mode switch
        {
            "Exchange" => 1,
            "Codex usage" => 2,
            _ => 0
        };

        var selected = Color.FromArgb(48, 41, 58);
        var normal = Color.FromArgb(20, 17, 22);
        _calculatorRow.BackColor = _selectedFeatureIndex == 0 ? selected : normal;
        _exchangeRow.BackColor = _selectedFeatureIndex == 1 ? selected : normal;
        _codexRow.BackColor = _selectedFeatureIndex == 2 ? selected : normal;
        _codexUsageView.Visible = _selectedFeatureIndex == 2;
        if (_selectedFeatureIndex == 2)
        {
            _codexRefreshTimer.Start();
        }
        else
        {
            _codexRefreshTimer.Stop();
        }
        _resultIcon.Visible = _selectedFeatureIndex != 2;
        _mode.Visible = _selectedFeatureIndex != 2;
        _result.Visible = _selectedFeatureIndex != 2;
        _hint.Visible = _selectedFeatureIndex != 2;
        _calculatorTitle.ForeColor = _selectedFeatureIndex == 0
            ? Color.FromArgb(245, 241, 235)
            : Color.FromArgb(185, 180, 190);
        _exchangeTitle.ForeColor = _selectedFeatureIndex == 1
            ? Color.FromArgb(245, 241, 235)
            : Color.FromArgb(185, 180, 190);
        _codexTitle.ForeColor = _selectedFeatureIndex == 2
            ? Color.FromArgb(245, 241, 235)
            : Color.FromArgb(185, 180, 190);
    }

    private async Task LoadRatesAsync()
    {
        try
        {
            using var response = await Http.GetAsync(RateUrl, _shutdown.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(_shutdown.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: _shutdown.Token);
            var root = document.RootElement;

            if (!root.TryGetProperty("result", out var result) ||
                !string.Equals(result.GetString(), "success", StringComparison.OrdinalIgnoreCase) ||
                !root.TryGetProperty("rates", out var rates))
            {
                throw new InvalidOperationException("The exchange-rate response was not successful.");
            }

            var loadedRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var rate in rates.EnumerateObject())
            {
                if (rate.Value.TryGetDecimal(out var value) && value > 0)
                {
                    loadedRates[rate.Name.ToUpperInvariant()] = value;
                }
            }

            if (!loadedRates.ContainsKey("MYR") || !loadedRates.ContainsKey("USD") || !loadedRates.ContainsKey("SGD"))
            {
                throw new InvalidOperationException("The response did not contain the required currencies.");
            }

            PublishRates(loadedRates);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            PublishRates(null);
        }
    }

    private void PublishRates(Dictionary<string, decimal>? loadedRates)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => PublishRates(loadedRates)));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        if (loadedRates is not null)
        {
            _ratesFromMyr.Clear();
            foreach (var rate in loadedRates)
            {
                _ratesFromMyr[rate.Key] = rate.Value;
            }

            _liveRates = true;
        }

        UpdateResult();
    }

    private void ApplyRoundedCorners()
    {
        if (Width < 10 || Height < 10)
        {
            return;
        }

        using var path = new GraphicsPath();
        var bounds = new Rectangle(0, 0, Width, Height);
        const int radius = 18;
        path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
        path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
        path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Quick", "1.0"));
        return client;
    }

    private static string FormatCalculatorNumber(decimal value) =>
        value.ToString("0.###############", CultureInfo.InvariantCulture);

    private static string FormatCurrency(decimal value) =>
        value.ToString("N2", CultureInfo.InvariantCulture);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
