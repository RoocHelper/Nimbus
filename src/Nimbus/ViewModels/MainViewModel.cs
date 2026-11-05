using System.Collections.ObjectModel;
using System.Windows.Input;
using Nimbus.Models;
using Nimbus.Services;

namespace Nimbus.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private string _currentPageKey = "home";
    private object? _currentPage;
    private bool _isUpdateDialogOpen;
    private bool _isLoaded;

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        Config = new ConfigService();
        Config.Load();
        Machine = SystemInfo.Read();

        Job = new JobViewModel();
        Dialog = new DialogViewModel();
        Toast = new ToastViewModel();
        AppUpdate = new AppUpdateService();
        History = new ObservableCollection<HistoryItemViewModel>(HistoryService.Load().Select(h => new HistoryItemViewModel(h)));

        Tweaks = new TweaksViewModel(this);
        Home = new HomeViewModel(this);
        Updates = new UpdatesViewModel(this);
        HistoryPage = new HistoryViewModel(this);
        SettingsPage = new SettingsViewModel(this);

        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? "home"));
        SetLanguageCommand = new RelayCommand(p => SettingsPage.Language = p as string ?? "id");
        OpenUpdateDialogCommand = new RelayCommand(() => IsUpdateDialogOpen = true);
        CloseUpdateDialogCommand = new RelayCommand(() => IsUpdateDialogOpen = false, () => !AppUpdate.IsBusy);
        InstallUpdateCommand = new AsyncRelayCommand(() => AppUpdate.DownloadAndRestartAsync(), () => AppUpdate.State == AppUpdateState.Available);

        Loc.LanguageChanged += (_, _) => RefreshLanguage();
        AppUpdate.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppUpdateService.State) or nameof(AppUpdateService.NewVersion))
            {
                OnPropertyChanged(nameof(UpdateBadgeText));
                OnPropertyChanged(nameof(AppUpdateStatusText));
            }
        };

        _currentPage = Home;
    }

    public event EventHandler? PageChanged;

    public AppSettings Settings { get; }
    public ConfigService Config { get; }
    public SystemInfo Machine { get; }
    public JobViewModel Job { get; }
    public DialogViewModel Dialog { get; }
    public ToastViewModel Toast { get; }
    public AppUpdateService AppUpdate { get; }
    public ObservableCollection<HistoryItemViewModel> History { get; }

    public HomeViewModel Home { get; }
    public TweaksViewModel Tweaks { get; }
    public UpdatesViewModel Updates { get; }
    public HistoryViewModel HistoryPage { get; }
    public SettingsViewModel SettingsPage { get; }

    public ICommand NavigateCommand { get; }
    public ICommand SetLanguageCommand { get; }
    public ICommand OpenUpdateDialogCommand { get; }
    public ICommand CloseUpdateDialogCommand { get; }
    public ICommand InstallUpdateCommand { get; }

    public string AppVersion => "v" + AppUpdate.CurrentVersion;
    public string Language => Loc.Language;

    public string UpdateBadgeText => Loc.Format("S.Update.Badge", AppUpdate.NewVersion ?? string.Empty);

    public string AppUpdateStatusText => AppUpdate.State switch
    {
        AppUpdateState.NotConfigured => Loc.T("S.AppUpd.NotConfigured"),
        AppUpdateState.NotInstalled => Loc.T("S.AppUpd.NotInstalled"),
        AppUpdateState.Checking => Loc.T("S.AppUpd.Checking"),
        AppUpdateState.UpToDate => Loc.T("S.AppUpd.UpToDate"),
        AppUpdateState.Available => Loc.Format("S.AppUpd.Available", AppUpdate.NewVersion ?? string.Empty),
        AppUpdateState.Downloading => Loc.T("S.AppUpd.Downloading"),
        AppUpdateState.Ready => Loc.T("S.AppUpd.Ready"),
        AppUpdateState.Error => Loc.Format("S.AppUpd.Error", AppUpdate.Error ?? string.Empty),
        _ => Loc.T("S.AppUpd.Idle"),
    };

    public string CurrentPageKey
    {
        get => _currentPageKey;
        set => Navigate(value);
    }

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public bool IsUpdateDialogOpen
    {
        get => _isUpdateDialogOpen;
        set => SetProperty(ref _isUpdateDialogOpen, value);
    }

    public async Task InitializeAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        await Tweaks.RefreshStatusAsync();
        Updates.RefreshMode();
        Home.Refresh();

        if (Settings.CheckUpdatesOnStartup)
        {
            await AppUpdate.CheckAsync();
            if (AppUpdate.State == AppUpdateState.Available)
            {
                IsUpdateDialogOpen = true;
            }
        }
    }

    public void Navigate(string key)
    {
        object page = key switch
        {
            "tweaks" => Tweaks,
            "updates" => Updates,
            "history" => HistoryPage,
            "settings" => SettingsPage,
            _ => Home,
        };

        if (_currentPageKey == key && ReferenceEquals(_currentPage, page))
        {
            return;
        }

        _currentPageKey = key;
        OnPropertyChanged(nameof(CurrentPageKey));
        CurrentPage = page;
        PageChanged?.Invoke(this, EventArgs.Empty);

        if (page is TweaksViewModel tweaks)
        {
            tweaks.OnShown();
        }
        else if (page is HomeViewModel home)
        {
            home.Refresh();
        }
        else if (page is UpdatesViewModel updates)
        {
            updates.RefreshMode();
        }
    }

    public void AddHistory(string action, string title, IEnumerable<string> items, bool success, string? logFile, IEnumerable<string>? tweakKeys = null)
    {
        var entry = new HistoryEntry
        {
            Time = DateTime.Now,
            Action = action,
            Title = title,
            Items = items.ToList(),
            TweakKeys = tweakKeys?.ToList() ?? new List<string>(),
            Success = success,
            LogFile = logFile,
        };
        History.Insert(0, new HistoryItemViewModel(entry));
        HistoryService.Save(History.Select(h => h.Entry));
    }

    public void ClearHistory()
    {
        History.Clear();
        HistoryService.Save(Array.Empty<HistoryEntry>());
    }

    public void SaveSettings() => SettingsService.Save(Settings);

    private void RefreshLanguage()
    {
        OnAllPropertiesChanged();
        Tweaks.RefreshTexts();
        Home.Refresh();
        Updates.RefreshTexts();
        foreach (var h in History)
        {
            h.RefreshTexts();
        }
    }
}
