namespace Quick;

internal sealed class QuickUpdateService
{
    private const string UpdateRepositoryUrl = "https://github.com/CYJun-1223/Quick";
    private readonly UpdateManager _manager = new(
        new GithubSource(UpdateRepositoryUrl, string.Empty, false, null),
        null,
        null);

    internal bool IsInstalled => _manager.IsInstalled;

    internal Task<UpdateInfo?> CheckForUpdatesAsync() => _manager.CheckForUpdatesAsync();

    internal async Task DownloadAndRestartAsync(UpdateInfo update, CancellationToken cancellationToken)
    {
        await _manager.DownloadUpdatesAsync(update, static _ => { }, cancellationToken);
        _manager.ApplyUpdatesAndRestart(update.TargetFullRelease, Array.Empty<string>());
    }
}
