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

    private async Task CheckForUpdatesAsync()
    {
        if (_updateCheckInFlight || !QuickSettingsStore.IsAutoUpdateEnabled())
        {
            return;
        }

        _updateCheckInFlight = true;
        try
        {
            if (!_updateService.IsInstalled)
            {
                return;
            }

            UpdateInfo? update;
            try
            {
                update = await _updateService.CheckForUpdatesAsync();
            }
            catch
            {
                return;
            }

            if (update is null)
            {
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
                MessageBox.Show(
                    this,
                    "Quick could not install the update. Please try again later.",
                    "Quick update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _updateCheckInFlight = false;
        }
    }
}
