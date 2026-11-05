using System.Diagnostics;
using System.Windows.Input;
using Nimbus.Models;
using Nimbus.Services;

namespace Nimbus.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    private readonly MainViewModel _shell;
    private string _selectedPreset = "Standard";

    public HomeViewModel(MainViewModel shell)
    {
        _shell = shell;
        SelectPresetCommand = new RelayCommand(p => SelectedPreset = p as string ?? "Standard");
        OptimizeCommand = new AsyncRelayCommand(() => _shell.Tweaks.ApplyPresetAsync(SelectedPreset), () => !_shell.Job.IsBusy);
        RestorePointCommand = new AsyncRelayCommand(
            () => _shell.Tweaks.RunTweaksAsync(new[] { TweaksViewModel.RestorePointKey }, false, Loc.T("S.JobTitle.RestorePoint")),
            () => !_shell.Job.IsBusy);
    }

    public ICommand SelectPresetCommand { get; }
    public ICommand OptimizeCommand { get; }
    public ICommand RestorePointCommand { get; }

    public string SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                OnPropertyChanged(nameof(PresetDescription));
            }
        }
    }

    public string PresetDescription =>
        Loc.Format("S.Preset." + SelectedPreset + ".Desc", _shell.Tweaks.PresetCount(SelectedPreset));

    public string ActiveTweaksText => $"{_shell.Tweaks.AppliedCount} / {_shell.Tweaks.DetectableCount}";

    public string UpdateModeText => Loc.T("S.Upd.Mode." + _shell.Updates.CurrentMode);

    public bool IsUpdatesDisabled => _shell.Updates.CurrentMode == UpdateMode.Disabled;

    public string SystemText => _shell.Machine.FriendlyName;

    public string SystemDetail => Loc.Format("S.Home.Build", _shell.Machine.Build);

    public bool IsHomeEdition => _shell.Machine.IsHomeEdition;

    public string GreetingText => DateTime.Now.Hour switch
    {
        < 11 => Loc.T("S.Home.Morning"),
        < 15 => Loc.T("S.Home.Afternoon"),
        < 19 => Loc.T("S.Home.Evening"),
        _ => Loc.T("S.Home.Night"),
    };

    public void Refresh() => OnAllPropertiesChanged();
}

public sealed class UpdatesViewModel : ObservableObject
{
    private readonly MainViewModel _shell;
    private UpdateMode _currentMode = UpdateMode.Default;
    private UpdateMode _selectedMode = UpdateMode.Recommended;
    private bool _aggressiveFix;

    public UpdatesViewModel(MainViewModel shell)
    {
        _shell = shell;
        SelectModeCommand = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse<UpdateMode>(s, out var mode))
            {
                SelectedMode = mode;
            }
        });
        ApplyModeCommand = new AsyncRelayCommand(ApplyModeAsync, () => !_shell.Job.IsBusy);
        RunFixCommand = new AsyncRelayCommand(RunFixAsync, () => !_shell.Job.IsBusy);
        CheckAppUpdateCommand = new AsyncRelayCommand(async () =>
        {
            await _shell.AppUpdate.CheckAsync();
            if (_shell.AppUpdate.State == AppUpdateState.Available)
            {
                _shell.IsUpdateDialogOpen = true;
            }
        }, () => !_shell.AppUpdate.IsBusy);
        OpenReleasesCommand = new RelayCommand(() => TweaksViewModel.OpenUrl(AppInfo.GitHubRepoUrl + "/releases"));
    }

    public ICommand SelectModeCommand { get; }
    public ICommand ApplyModeCommand { get; }
    public ICommand RunFixCommand { get; }
    public ICommand CheckAppUpdateCommand { get; }
    public ICommand OpenReleasesCommand { get; }

    public MainViewModel Shell => _shell;

    public UpdateMode CurrentMode
    {
        get => _currentMode;
        private set
        {
            if (SetProperty(ref _currentMode, value))
            {
                OnPropertyChanged(nameof(CurrentModeText));
            }
        }
    }

    public string CurrentModeText => Loc.Format("S.Upd.CurrentMode", Loc.T("S.Upd.Mode." + CurrentMode));

    public UpdateMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(ApplyButtonText));
            }
        }
    }

    public string ApplyButtonText => Loc.Format("S.Upd.ApplyMode", Loc.T("S.Upd.Mode." + SelectedMode));

    public bool AggressiveFix
    {
        get => _aggressiveFix;
        set => SetProperty(ref _aggressiveFix, value);
    }

    public bool IsHomeEdition => _shell.Machine.IsHomeEdition;

    public void RefreshMode()
    {
        CurrentMode = UpdateModeService.Detect();
        _shell.Home.Refresh();
    }

    public void RefreshTexts() => OnAllPropertiesChanged();

    private async Task ApplyModeAsync()
    {
        var mode = SelectedMode;
        var danger = mode == UpdateMode.Disabled;
        var ok = await _shell.Dialog.ShowAsync(
            Loc.T(danger ? "S.Dlg.UpdDisable.Title" : "S.Dlg.UpdMode.Title"),
            Loc.T(danger ? "S.Dlg.UpdDisable.Msg" : "S.Upd.Mode." + mode + ".Desc"),
            confirmText: ApplyButtonText,
            danger: danger);
        if (!ok)
        {
            return;
        }

        var command = mode switch
        {
            UpdateMode.Recommended => "Invoke-WPFUpdatessecurity",
            UpdateMode.Disabled => "Invoke-WPFUpdatesdisable -Confirmed",
            _ => "Invoke-WPFUpdatesdefault",
        };

        var title = ApplyButtonText;
        var outcome = await _shell.Job.RunScriptAsync(title, command, restartHint: true);
        _shell.AddHistory("UpdateMode", title, new[] { Loc.T("S.Upd.Mode." + mode) }, outcome.Success, outcome.LogFile);
        RefreshMode();
    }

    private async Task RunFixAsync()
    {
        var ok = await _shell.Dialog.ShowAsync(
            Loc.T("S.Upd.FixTitle"),
            Loc.T(AggressiveFix ? "S.Dlg.Fix.MsgAggressive" : "S.Dlg.Fix.Msg"),
            confirmText: Loc.T("S.Upd.FixRun"));
        if (!ok)
        {
            return;
        }

        var title = Loc.T("S.Upd.FixTitle");
        var outcome = await _shell.Job.RunScriptAsync(
            title,
            AggressiveFix ? "Invoke-WPFFixesUpdate -Aggressive $true" : "Invoke-WPFFixesUpdate",
            restartHint: true);
        _shell.AddHistory("Fix", title, Array.Empty<string>(), outcome.Success, outcome.LogFile);
        RefreshMode();
    }
}

public sealed class HistoryViewModel : ObservableObject
{
    private readonly MainViewModel _shell;

    public HistoryViewModel(MainViewModel shell)
    {
        _shell = shell;
        _shell.History.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        ClearCommand = new AsyncRelayCommand(async () =>
        {
            if (await _shell.Dialog.ShowAsync(Loc.T("S.Hist.Clear"), Loc.T("S.Dlg.ClearHistory.Msg"), confirmText: Loc.T("S.Hist.Clear"), danger: true))
            {
                _shell.ClearHistory();
            }
        }, () => _shell.History.Count > 0);
        UndoCommand = new AsyncRelayCommand(
            p => p is HistoryItemViewModel h ? _shell.Tweaks.RunTweaksAsync(h.Entry.TweakKeys, true) : Task.CompletedTask,
            p => p is HistoryItemViewModel { CanUndo: true } && !_shell.Job.IsBusy);
        OpenLogCommand = new RelayCommand(p =>
        {
            if (p is HistoryItemViewModel { HasLog: true } h)
            {
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{h.Entry.LogFile}\"") { UseShellExecute = true });
            }
        });
        OpenLogFolderCommand = new RelayCommand(() =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Logs}\"") { UseShellExecute = true }));
    }

    public System.Collections.ObjectModel.ObservableCollection<HistoryItemViewModel> Items => _shell.History;
    public bool IsEmpty => _shell.History.Count == 0;

    public ICommand ClearCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _shell;

    public SettingsViewModel(MainViewModel shell)
    {
        _shell = shell;
        SetBackdropCommand = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse<BackdropKind>(s, out var kind))
            {
                Backdrop = kind;
            }
        });
        SetLanguageCommand = new RelayCommand(p => Language = p as string ?? "id");
        OpenGitHubCommand = new RelayCommand(() => TweaksViewModel.OpenUrl(AppInfo.GitHubRepoUrl));
        OpenWinUtilCommand = new RelayCommand(() => TweaksViewModel.OpenUrl(AppInfo.WinUtilRepoUrl));
        OpenLogFolderCommand = new RelayCommand(() =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Logs}\"") { UseShellExecute = true }));
    }

    public event EventHandler? BackdropChanged;

    public ICommand SetBackdropCommand { get; }
    public ICommand SetLanguageCommand { get; }
    public ICommand OpenGitHubCommand { get; }
    public ICommand OpenWinUtilCommand { get; }
    public ICommand OpenLogFolderCommand { get; }

    public string Language
    {
        get => _shell.Settings.Language;
        set
        {
            if (_shell.Settings.Language == value)
            {
                return;
            }

            _shell.Settings.Language = value;
            _shell.SaveSettings();
            Loc.SetLanguage(value);
            OnPropertyChanged();
        }
    }

    public BackdropKind Backdrop
    {
        get => _shell.Settings.Backdrop;
        set
        {
            if (_shell.Settings.Backdrop == value)
            {
                return;
            }

            _shell.Settings.Backdrop = value;
            _shell.SaveSettings();
            OnPropertyChanged();
            BackdropChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool BackdropSupported => _shell.Machine.SupportsBackdrop;

    public bool AutoRestorePoint
    {
        get => _shell.Settings.AutoRestorePoint;
        set
        {
            _shell.Settings.AutoRestorePoint = value;
            _shell.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool CheckUpdatesOnStartup
    {
        get => _shell.Settings.CheckUpdatesOnStartup;
        set
        {
            _shell.Settings.CheckUpdatesOnStartup = value;
            _shell.SaveSettings();
            OnPropertyChanged();
        }
    }

    public string VersionText => "Nimbus v" + _shell.AppUpdate.CurrentVersion;
    public string RepoText => AppInfo.GitHubRepoUrl.Replace("https://", string.Empty);
}
