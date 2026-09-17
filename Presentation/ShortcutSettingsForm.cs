namespace Quick;

internal sealed class ShortcutSettingsForm : Form
{
    private readonly LauncherForm _launcher;
    private readonly TextBox _shortcutInput = new();
    private readonly CheckBox _launchAtStartup = new();
    private readonly CheckBox _autoUpdate = new();
    private readonly Label _error = new();
    private Keys _modifiers;
    private Keys _key;

    public ShortcutSettingsForm(LauncherForm launcher, Keys modifiers, Keys key)
    {
        _launcher = launcher;
        _modifiers = modifiers;
        _key = key;

        Text = "Quick Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, 340);
        BackColor = Color.FromArgb(29, 29, 31);
        ForeColor = Color.FromArgb(245, 241, 235);

        var title = new Label
        {
            Location = new Point(28, 24),
            Size = new Size(420, 28),
            Text = "Settings",
            ForeColor = Color.FromArgb(245, 241, 235),
            Font = new Font("Segoe UI", 16f, FontStyle.Bold)
        };

        var label = new Label
        {
            Location = new Point(28, 70),
            Size = new Size(420, 20),
            Text = "Global shortcut",
            ForeColor = Color.FromArgb(215, 211, 218),
            Font = new Font("Segoe UI", 9.5f)
        };

        var instruction = new Label
        {
            Location = new Point(28, 94),
            Size = new Size(420, 20),
            Text = "Click the field, then press the shortcut you want to use.",
            ForeColor = Color.FromArgb(142, 138, 148),
            Font = new Font("Segoe UI", 8.5f)
        };

        _shortcutInput.Location = new Point(28, 126);
        _shortcutInput.Size = new Size(424, 38);
        _shortcutInput.ReadOnly = true;
        _shortcutInput.TabStop = true;
        _shortcutInput.Text = LauncherForm.FormatShortcut(_modifiers, _key);
        _shortcutInput.BackColor = Color.FromArgb(42, 40, 46);
        _shortcutInput.ForeColor = Color.FromArgb(245, 241, 235);
        _shortcutInput.BorderStyle = BorderStyle.FixedSingle;
        _shortcutInput.Font = new Font("Segoe UI", 11f);
        _shortcutInput.KeyDown += CaptureShortcut;

        _error.Location = new Point(28, 172);
        _error.Size = new Size(424, 20);
        _error.ForeColor = Color.FromArgb(210, 117, 93);
        _error.Font = new Font("Segoe UI", 8.5f);

        _launchAtStartup.Location = new Point(25, 198);
        _launchAtStartup.Size = new Size(424, 24);
        _launchAtStartup.AutoSize = true;
        _launchAtStartup.Text = "Launch Quick at Windows startup";
        _launchAtStartup.Checked = QuickSettingsStore.IsLaunchAtStartupEnabled(Application.ExecutablePath);
        _launchAtStartup.BackColor = BackColor;
        _launchAtStartup.ForeColor = Color.FromArgb(215, 211, 218);
        _launchAtStartup.Font = new Font("Segoe UI", 9.5f);
        _launchAtStartup.FlatStyle = FlatStyle.Flat;

        var autoUpdateAvailable = _launcher.IsAutoUpdateAvailable();
        _autoUpdate.Location = new Point(25, 224);
        _autoUpdate.Size = new Size(424, 24);
        _autoUpdate.AutoSize = true;
        _autoUpdate.Text = autoUpdateAvailable
            ? "Check for updates automatically"
            : "Automatic updates require the installed version";
        _autoUpdate.Checked = autoUpdateAvailable && QuickSettingsStore.IsAutoUpdateEnabled();
        _autoUpdate.Enabled = autoUpdateAvailable;
        _autoUpdate.BackColor = BackColor;
        _autoUpdate.ForeColor = Color.FromArgb(215, 211, 218);
        _autoUpdate.Font = new Font("Segoe UI", 9.5f);
        _autoUpdate.FlatStyle = FlatStyle.Flat;

        var cancel = new Button
        {
            Location = new Point(258, 278),
            Size = new Size(92, 34),
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(48, 46, 52),
            ForeColor = Color.FromArgb(224, 220, 228)
        };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(78, 71, 82);

        var save = new Button
        {
            Location = new Point(360, 278),
            Size = new Size(92, 34),
            Text = "Save",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(113, 57, 63),
            ForeColor = Color.FromArgb(255, 244, 239)
        };
        save.FlatAppearance.BorderColor = Color.FromArgb(153, 79, 81);
        save.Click += (_, _) => SaveShortcut();

        var source = new LinkLabel
        {
            Location = new Point(28, 285),
            Size = new Size(190, 20),
            Text = "Exchange data source",
            LinkColor = Color.FromArgb(142, 138, 148),
            ActiveLinkColor = Color.FromArgb(240, 166, 106),
            VisitedLinkColor = Color.FromArgb(142, 138, 148),
            Font = new Font("Segoe UI", 8.5f)
        };
        source.LinkClicked += (_, _) =>
        {
            Process.Start(new ProcessStartInfo("https://www.exchangerate-api.com") { UseShellExecute = true });
        };

        Controls.AddRange([title, label, instruction, _shortcutInput, _error, _launchAtStartup, _autoUpdate, cancel, save, source]);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void CaptureShortcut(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Menu or Keys.ControlKey or Keys.ShiftKey)
        {
            return;
        }

        var modifiers = e.Modifiers & (Keys.Alt | Keys.Control | Keys.Shift);
        if (modifiers == Keys.None)
        {
            _error.Text = "Add Alt, Ctrl, or Shift to the key.";
            return;
        }

        _modifiers = modifiers;
        _key = e.KeyCode;
        _shortcutInput.Text = LauncherForm.FormatShortcut(_modifiers, _key);
        _error.Text = string.Empty;
        e.SuppressKeyPress = true;
    }

    private void SaveShortcut()
    {
        if (!_launcher.TrySetShortcut(_modifiers, _key, out var error))
        {
            _error.Text = error;
            return;
        }

        if (!QuickSettingsStore.TrySetLaunchAtStartup(Application.ExecutablePath, _launchAtStartup.Checked, out error))
        {
            _error.Text = error;
            return;
        }

        if (!QuickSettingsStore.TrySetAutoUpdate(_autoUpdate.Checked, out error))
        {
            _error.Text = error;
            return;
        }

        _launcher.SetAutoUpdateEnabled(_autoUpdate.Checked);

        DialogResult = DialogResult.OK;
        Close();
    }
}
