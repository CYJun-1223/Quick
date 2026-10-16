namespace Quick;

internal sealed partial class LauncherForm
{
    private const int UpdateCheckIntervalMs = 24 * 60 * 60 * 1000;
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private readonly QuickUpdateService _updateService = new();
    private bool _updateCheckInFlight;

    internal bool IsAutoUpdateAvailable() => _updateService.IsInstalled;

    internal void SetAutoUpdateEnabled(bool enabled)
    {
        if (!enabled || !_updateService.IsInstalled)
        {
            _updateTimer.Stop();
            return;
        }

        _updateTimer.Start();
        _ = CheckForUpdatesAsync();
    }

    // manual = the tray menu item: ignores the automatic setting and always reports the outcome.
    private async Task CheckForUpdatesAsync(bool manual = false)
    {
        if (_updateCheckInFlight)
        {
            if (manual)
            {
                ShowUpdateMessage("Quick is already checking for updates.", MessageBoxIcon.Information);
            }

            return;
        }

        if (!manual && !QuickSettingsStore.IsAutoUpdateEnabled())
        {
            return;
        }

        _updateCheckInFlight = true;
        try
        {
            if (!_updateService.IsInstalled)
            {
                if (manual)
                {
                    ShowUpdateMessage("Updates need the installed version of Quick.", MessageBoxIcon.Information);
                }

                return;
            }

            UpdateInfo? update;
            try
            {
                update = await _updateService.CheckForUpdatesAsync();
            }
            catch
            {
                if (manual)
                {
                    ShowUpdateMessage("Quick could not check for updates. Please try again later.", MessageBoxIcon.Warning);
                }

                return;
            }

            if (update is null)
            {
                if (manual)
                {
                    ShowUpdateMessage($"Quick {_updateService.CurrentVersion} is the latest version.", MessageBoxIcon.Information);
                }

                return;
            }

            var choice = MessageBox.Show(
                this,
                $"Quick {update.TargetFullRelease.Version} is available. Download and restart now?",
                "Quick update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (choice != DialogResult.Yes)
            {
                return;
            }

            try
            {
                await _updateService.DownloadAndRestartAsync(update, _shutdown.Token);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
            }
            catch
            {
                ShowUpdateMessage("Quick could not install the update. Please try again later.", MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _updateCheckInFlight = false;
        }
    }

    private void ShowUpdateMessage(string text, MessageBoxIcon icon) =>
        MessageBox.Show(this, text, "Quick update", MessageBoxButtons.OK, icon);
}
