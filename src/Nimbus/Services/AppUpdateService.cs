using Nimbus.ViewModels;
using Velopack;
using Velopack.Sources;

namespace Nimbus.Services;

public enum AppUpdateState
{
    Idle,
    NotInstalled,
    NotConfigured,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Ready,
    Error,
}

/// <summary>
/// Checks GitHub Releases for a newer Nimbus build and installs it (Velopack).
/// Only works for an installed copy (Setup.exe); a dev build from Visual Studio reports NotInstalled.
/// </summary>
public sealed class AppUpdateService : ObservableObject
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;
    private AppUpdateState _state = AppUpdateState.Idle;
    private int _progress;
    private string? _newVersion;
    private string? _releaseNotes;
    private string? _error;

    public AppUpdateService()
    {
        if (!AppInfo.IsRepoConfigured)
        {
            _state = AppUpdateState.NotConfigured;
            return;
        }

        try
        {
            _manager = new UpdateManager(new GithubSource(AppInfo.GitHubRepoUrl, null, false));
            if (!_manager.IsInstalled)
            {
                _state = AppUpdateState.NotInstalled;
            }
        }
        catch (Exception ex)
        {
            _state = AppUpdateState.Error;
            _error = ex.Message;
        }
    }

    public AppUpdateState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    public bool IsUpdateAvailable => State is AppUpdateState.Available or AppUpdateState.Downloading or AppUpdateState.Ready;
    public bool IsBusy => State is AppUpdateState.Checking or AppUpdateState.Downloading;

    public string CurrentVersion => _manager?.IsInstalled == true && _manager.CurrentVersion is not null
        ? _manager.CurrentVersion.ToString()
        : AppInfo.Version;

    public int Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string? NewVersion
    {
        get => _newVersion;
        private set => SetProperty(ref _newVersion, value);
    }

    public string? ReleaseNotes
    {
        get => _releaseNotes;
        private set => SetProperty(ref _releaseNotes, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public async Task CheckAsync()
    {
        if (_manager is null || !_manager.IsInstalled || IsBusy)
        {
            return;
        }

        State = AppUpdateState.Checking;
        try
        {
            _pending = await _manager.CheckForUpdatesAsync();
            if (_pending is null)
            {
                State = AppUpdateState.UpToDate;
                return;
            }

            NewVersion = _pending.TargetFullRelease.Version.ToString();
            ReleaseNotes = CleanNotes(_pending.TargetFullRelease.NotesMarkdown);
            State = AppUpdateState.Available;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            State = AppUpdateState.Error;
        }
    }

    public async Task DownloadAndRestartAsync()
    {
        if (_manager is null || _pending is null || IsBusy)
        {
            return;
        }

        State = AppUpdateState.Downloading;
        Progress = 0;
        try
        {
            await _manager.DownloadUpdatesAsync(_pending, p => System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => { Progress = p; }));
            Progress = 100;
            State = AppUpdateState.Ready;
            await Task.Delay(600);
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            State = AppUpdateState.Error;
        }
    }

    private static string? CleanNotes(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var lines = markdown
            .Replace("\r", string.Empty)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.TrimStart('#').Trim())
            .Select(l => l.StartsWith("- ", StringComparison.Ordinal) || l.StartsWith("* ", StringComparison.Ordinal) ? "•  " + l[2..] : l)
            .Select(l => l.Replace("**", string.Empty));
        return string.Join(Environment.NewLine, lines);
    }
}
