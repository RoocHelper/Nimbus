using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Nimbus.Models;
using Nimbus.Services;

namespace Nimbus.ViewModels;

public sealed class TweaksViewModel : ObservableObject
{
    public const string RestorePointKey = "WPFTweaksRestorePoint";
    private const string OosuUrl = "https://dl5.oo-software.com/files/ooshutup10/OOSU10.exe";

    private readonly MainViewModel _shell;
    private readonly List<TweakItemViewModel> _essential;
    private readonly List<TweakItemViewModel> _advanced;
    private readonly List<TweakItemViewModel> _preferences;
    private readonly List<AppxItemViewModel> _appx;
    private string _selectedCategory = "essential";
    private string _searchText = string.Empty;
    private OptionViewModel? _selectedDns;
    private bool _appxLoaded;
    private bool _isAppxLoading;

    public TweaksViewModel(MainViewModel shell)
    {
        _shell = shell;
        var config = shell.Config;

        _essential = config.Tweaks.Where(t => t.IsSelectable && t.IsEssential).Select(t => new TweakItemViewModel(t, config)).ToList();
        _advanced = config.Tweaks.Where(t => t.IsSelectable && t.IsAdvanced).Select(t => new TweakItemViewModel(t, config)).ToList();
        _preferences = config.Tweaks.Where(t => t.IsToggle).Select(t => new TweakItemViewModel(t, config)).ToList();
        foreach (var p in _preferences)
        {
            p.ToggleRequested += OnToggleRequested;
        }

        var mpo = config.Tweaks.FirstOrDefault(t => t.IsCombo && t.Registry.Any(r => r.Values is not null));
        Mpo = mpo is null ? null : new ComboTweakViewModel(mpo, config);

        DnsOptions = BuildDnsOptions(config);
        _selectedDns = DnsOptions.FirstOrDefault(o => o.Key == "Cloudflare") ?? DnsOptions.FirstOrDefault();
        if (_selectedDns is not null)
        {
            _selectedDns.IsSelected = true;
        }

        _appx = config.Appx.Select(a => new AppxItemViewModel(a, config)).ToList();

        foreach (var item in _essential.Concat(_advanced))
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TweakItemViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(SelectedCountText));
                }
            };
        }

        foreach (var item in _appx)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppxItemViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedAppxCount));
                    OnPropertyChanged(nameof(SelectedAppxText));
                }
            };
        }

        SelectCategoryCommand = new RelayCommand(p => SelectedCategory = p as string ?? "essential");
        SelectPresetCommand = new RelayCommand(p => SelectPreset(p as string ?? "Standard"));
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        SelectAppliedCommand = new RelayCommand(SelectApplied);
        ApplyCommand = new AsyncRelayCommand(() => RunSelectedAsync(false), () => SelectedCount > 0 && !_shell.Job.IsBusy);
        UndoCommand = new AsyncRelayCommand(() => RunSelectedAsync(true), () => SelectedCount > 0 && !_shell.Job.IsBusy);
        SelectDnsCommand = new RelayCommand(p => SelectDns(p as OptionViewModel));
        ApplyDnsCommand = new AsyncRelayCommand(ApplyDnsAsync, () => _selectedDns is not null && !_shell.Job.IsBusy);
        SetComboCommand = new AsyncRelayCommand(p => SetComboAsync(p as OptionViewModel), _ => !_shell.Job.IsBusy);
        UltimateOnCommand = new AsyncRelayCommand(() => RunPowerPlanAsync(true), () => !_shell.Job.IsBusy);
        UltimateOffCommand = new AsyncRelayCommand(() => RunPowerPlanAsync(false), () => !_shell.Job.IsBusy);
        RunOosuCommand = new AsyncRelayCommand(RunOosuAsync, () => !_shell.Job.IsBusy);
        RefreshAppxCommand = new AsyncRelayCommand(LoadAppxAsync, () => !_isAppxLoading);
        RemoveAppxCommand = new AsyncRelayCommand(() => RunAppxAsync(false), () => SelectedAppxCount > 0 && !_shell.Job.IsBusy);
        InstallAppxCommand = new AsyncRelayCommand(() => RunAppxAsync(true), () => SelectedAppxCount > 0 && !_shell.Job.IsBusy);
        OpenLinkCommand = new RelayCommand(p => OpenUrl(p as string));

        RebuildVisible();
    }

    // ---------- Bindable state ----------

    public ObservableCollection<TweakItemViewModel> VisibleTweaks { get; } = new();
    public ObservableCollection<TweakItemViewModel> VisiblePreferences { get; } = new();
    public ObservableCollection<AppxItemViewModel> VisibleAppx { get; } = new();
    public IReadOnlyList<OptionViewModel> DnsOptions { get; }
    public ComboTweakViewModel? Mpo { get; }
    public bool IsMpoVisible => Mpo is not null && Mpo.Matches(SearchText);

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                RebuildVisible();
                OnPropertyChanged(nameof(IsSelectableCategory));
                OnPropertyChanged(nameof(IsAdvancedCategory));
                if (value == "appx" && !_appxLoaded)
                {
                    _ = LoadAppxAsync();
                }
            }
        }
    }

    public bool IsSelectableCategory => SelectedCategory is "essential" or "advanced";
    public bool IsAdvancedCategory => SelectedCategory == "advanced";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RebuildVisible();
            }
        }
    }

    public int SelectedCount => _essential.Count(i => i.IsSelected) + _advanced.Count(i => i.IsSelected);
    public string SelectedCountText => Loc.Format("S.Tweaks.Selected", SelectedCount);

    public int SelectedAppxCount => _appx.Count(a => a.IsSelected);
    public string SelectedAppxText => Loc.Format("S.Tweaks.Selected", SelectedAppxCount);

    public bool HasNoResults => SelectedCategory switch
    {
        "essential" or "advanced" => VisibleTweaks.Count == 0,
        "prefs" => VisiblePreferences.Count == 0 && !IsMpoVisible,
        "appx" => VisibleAppx.Count == 0,
        _ => false,
    };

    public bool IsAppxLoading
    {
        get => _isAppxLoading;
        private set => SetProperty(ref _isAppxLoading, value);
    }

    /// <summary>Selectable tweaks whose state can be detected and that are currently active.</summary>
    public int AppliedCount => _essential.Concat(_advanced).Count(i => i.IsApplied == true);
    public int DetectableCount => _essential.Concat(_advanced).Count(i => i.IsApplied is not null);

    public int PresetCount(string preset) =>
        _shell.Config.Presets.TryGetValue(preset, out var keys) ? keys.Count : 0;

    // ---------- Commands ----------

    public ICommand SelectCategoryCommand { get; }
    public ICommand SelectPresetCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand SelectAppliedCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand SelectDnsCommand { get; }
    public ICommand ApplyDnsCommand { get; }
    public ICommand SetComboCommand { get; }
    public ICommand UltimateOnCommand { get; }
    public ICommand UltimateOffCommand { get; }
    public ICommand RunOosuCommand { get; }
    public ICommand RefreshAppxCommand { get; }
    public ICommand RemoveAppxCommand { get; }
    public ICommand InstallAppxCommand { get; }
    public ICommand OpenLinkCommand { get; }

    // ---------- Public operations ----------

    public void OnShown()
    {
        if (SelectedCategory == "appx" && !_appxLoaded)
        {
            _ = LoadAppxAsync();
        }
    }

    public void SelectPreset(string preset)
    {
        if (!_shell.Config.Presets.TryGetValue(preset, out var keys))
        {
            return;
        }

        foreach (var item in _essential.Concat(_advanced))
        {
            item.IsSelected = keys.Contains(item.Key, StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<bool> ApplyPresetAsync(string preset)
    {
        if (!_shell.Config.Presets.TryGetValue(preset, out var keys))
        {
            return false;
        }

        return await RunTweaksAsync(keys, undo: false, Loc.Format("S.JobTitle.Preset", Loc.T("S.Preset." + preset)));
    }

    public async Task<bool> RunTweaksAsync(IReadOnlyList<string> keys, bool undo, string? title = null)
    {
        var config = _shell.Config;
        var defs = keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(config.FindTweak)
            .Where(d => d is not null && (!undo || d.HasUndo))
            .Select(d => d!)
            .ToList();

        if (!undo && _shell.Settings.AutoRestorePoint && defs.All(d => d.Key != RestorePointKey))
        {
            var rp = config.FindTweak(RestorePointKey);
            if (rp is not null)
            {
                defs.Insert(0, rp);
            }
        }

        // The restore point must always be taken before anything changes.
        defs = defs.OrderBy(d => d.Key == RestorePointKey ? 0 : 1).ToList();
        if (defs.Count == 0)
        {
            _shell.Toast.Show(Loc.T("S.Toast.NothingToUndo"));
            return false;
        }

        var names = defs.Select(config.TweakName).ToList();
        var danger = defs.Any(d => FindItem(d.Key)?.Risk == RiskLevel.High);
        var confirmed = await _shell.Dialog.ShowAsync(
            Loc.T(undo ? "S.Dlg.Undo.Title" : "S.Dlg.Apply.Title"),
            Loc.Format(undo ? "S.Dlg.Undo.Msg" : "S.Dlg.Apply.Msg", defs.Count),
            names,
            Loc.T(undo ? "S.Tweaks.Undo" : "S.Tweaks.Apply"),
            danger);
        if (!confirmed)
        {
            return false;
        }

        var script = new StringBuilder();
        for (var i = 0; i < defs.Count; i++)
        {
            var status = Loc.Format(undo ? "S.Status.Undoing" : "S.Status.Applying", names[i]);
            script.AppendLine($"Write-NimbusProgress -Percent {i * 100 / defs.Count} -Status {PowerShellRunner.Q(status)}");
            script.AppendLine($"Invoke-NimbusTweak -Key {PowerShellRunner.Q(defs[i].Key)}{(undo ? " -Undo" : string.Empty)}");
        }

        title ??= Loc.T(undo ? "S.JobTitle.Undo" : "S.JobTitle.Apply");
        var outcome = await _shell.Job.RunScriptAsync(title, script.ToString(), restartHint: true);
        _shell.AddHistory(
            undo ? "Undo" : "Apply",
            title,
            names,
            outcome.Success,
            outcome.LogFile,
            defs.Where(d => d.Key != RestorePointKey && d.HasUndo).Select(d => d.Key));

        if (outcome.Success)
        {
            ClearSelection();
        }

        await RefreshStatusAsync();
        return outcome.Success;
    }

    public async Task RefreshStatusAsync()
    {
        var all = _essential.Concat(_advanced).Concat(_preferences).ToList();
        var results = await Task.Run(() =>
            all.Select(i => (Item: i, State: TweakStatusService.IsApplied(i.Definition))).ToList());
        var combo = Mpo is null ? null : await Task.Run(() => TweakStatusService.ComboState(Mpo.Definition));

        foreach (var (item, state) in results)
        {
            item.IsApplied = state;
            if (item.Definition.IsToggle)
            {
                item.SetOnSilently(state == true);
            }
        }

        if (Mpo is not null)
        {
            Mpo.CurrentState = combo;
        }

        OnPropertyChanged(nameof(AppliedCount));
        OnPropertyChanged(nameof(DetectableCount));
        _shell.Home.Refresh();
    }

    public void RefreshTexts()
    {
        foreach (var item in _essential.Concat(_advanced).Concat(_preferences))
        {
            item.RefreshTexts();
        }

        foreach (var a in _appx)
        {
            a.RefreshTexts();
        }

        foreach (var d in DnsOptions)
        {
            d.Label = DnsLabel(d.Key);
            d.Description = DnsDescription(d.Key);
        }

        Mpo?.RefreshTexts();
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(SelectedAppxText));
    }

    // ---------- Private helpers ----------

    private TweakItemViewModel? FindItem(string key) =>
        _essential.Concat(_advanced).Concat(_preferences).FirstOrDefault(i => i.Key == key);

    private void RebuildVisible()
    {
        var q = SearchText.Trim();
        Replace(VisibleTweaks, (SelectedCategory == "advanced" ? _advanced : _essential).Where(i => i.Matches(q)));
        Replace(VisiblePreferences, _preferences.Where(i => i.Matches(q)));
        Replace(VisibleAppx, _appx.Where(a => a.Matches(q)));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(IsMpoVisible));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void ClearSelection()
    {
        foreach (var item in _essential.Concat(_advanced))
        {
            item.IsSelected = false;
        }
    }

    private void SelectApplied()
    {
        foreach (var item in _essential.Concat(_advanced))
        {
            item.IsSelected = item.IsApplied == true;
        }
    }

    private Task RunSelectedAsync(bool undo)
    {
        var keys = _essential.Concat(_advanced).Where(i => i.IsSelected).Select(i => i.Key).ToList();
        return RunTweaksAsync(keys, undo);
    }

    private async void OnToggleRequested(TweakItemViewModel item, bool on)
    {
        if (_shell.Job.IsBusy)
        {
            item.SetOnSilently(!on);
            _shell.Toast.Show(Loc.T("S.Toast.Busy"), true);
            return;
        }

        item.IsWorking = true;
        var script = $"Invoke-NimbusTweak -Key {PowerShellRunner.Q(item.Key)}{(on ? string.Empty : " -Undo")}";
        var outcome = await _shell.Job.RunScriptAsync(item.Name, script, silent: true);
        item.IsWorking = false;

        if (!outcome.Success)
        {
            item.SetOnSilently(!on);
            _shell.Toast.Show(Loc.Format("S.Toast.Failed", item.Name), true);
        }
        else
        {
            item.IsApplied = on;
            _shell.Toast.Show(Loc.Format(on ? "S.Toast.On" : "S.Toast.Off", item.Name));
        }

        _shell.AddHistory("Toggle", item.Name, new[] { Loc.T(on ? "S.State.On" : "S.State.Off") }, outcome.Success, outcome.LogFile);
    }

    private async Task SetComboAsync(OptionViewModel? option)
    {
        if (Mpo is null || option is null || option.Key == Mpo.CurrentState)
        {
            return;
        }

        var previous = Mpo.CurrentState;
        Mpo.CurrentState = option.Key;
        Mpo.IsWorking = true;
        var script = $"Set-NimbusComboState -Key {PowerShellRunner.Q(Mpo.Definition.Key)} -State {PowerShellRunner.Q(option.Key)}";
        var outcome = await _shell.Job.RunScriptAsync(Mpo.Name, script, silent: true);
        Mpo.IsWorking = false;

        if (outcome.Success)
        {
            _shell.Toast.Show(Loc.Format("S.Toast.Set", Mpo.Name, option.Label));
        }
        else
        {
            Mpo.CurrentState = previous;
            _shell.Toast.Show(Loc.Format("S.Toast.Failed", Mpo.Name), true);
        }

        _shell.AddHistory("Toggle", Mpo.Name, new[] { option.Label }, outcome.Success, outcome.LogFile);
    }

    private static IReadOnlyList<OptionViewModel> BuildDnsOptions(ConfigService config)
    {
        var list = new List<OptionViewModel>
        {
            new("DHCP", DnsLabel("DHCP"), DnsDescription("DHCP")),
            new("Fastest", DnsLabel("Fastest"), DnsDescription("Fastest")),
        };

        foreach (var p in config.DnsProviders)
        {
            var detail = string.Join("  ·  ", new[] { p.Primary, p.Secondary }.Where(s => !string.IsNullOrWhiteSpace(s)));
            list.Add(new OptionViewModel(p.Name, DnsLabel(p.Name), DnsDescription(p.Name), detail));
        }

        return list;
    }

    private static string DnsLabel(string key) => key switch
    {
        "DHCP" => Loc.T("S.Dns.DHCP"),
        "Fastest" => Loc.T("S.Dns.Fastest"),
        _ => key.Replace('_', ' '),
    };

    private static string DnsDescription(string key)
    {
        var text = Loc.T("S.Dns.Desc." + key);
        return text.StartsWith("S.Dns.", StringComparison.Ordinal) ? string.Empty : text;
    }

    private void SelectDns(OptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var o in DnsOptions)
        {
            o.IsSelected = ReferenceEquals(o, option);
        }

        _selectedDns = option;
    }

    private async Task ApplyDnsAsync()
    {
        if (_selectedDns is null)
        {
            return;
        }

        var ok = await _shell.Dialog.ShowAsync(
            Loc.T("S.Dlg.Dns.Title"),
            Loc.Format("S.Dlg.Dns.Msg", _selectedDns.Label),
            confirmText: Loc.T("S.Dns.Apply"));
        if (!ok)
        {
            return;
        }

        var title = Loc.Format("S.JobTitle.Dns", _selectedDns.Label);
        var outcome = await _shell.Job.RunScriptAsync(title, $"Set-NimbusDns -Provider {PowerShellRunner.Q(_selectedDns.Key)}");
        _shell.AddHistory("Dns", title, new[] { _selectedDns.Label }, outcome.Success, outcome.LogFile);
    }

    private async Task RunPowerPlanAsync(bool enable)
    {
        if (enable)
        {
            var ok = await _shell.Dialog.ShowAsync(Loc.T("S.Perf.UltTitle"), Loc.T("S.Dlg.Ult.Msg"), confirmText: Loc.T("S.Perf.Enable"));
            if (!ok)
            {
                return;
            }
        }

        var title = Loc.T(enable ? "S.JobTitle.UltOn" : "S.JobTitle.UltOff");
        var outcome = await _shell.Job.RunScriptAsync(title, enable ? "Invoke-WPFUltimatePerformance -Enable" : "Invoke-WPFUltimatePerformance");
        _shell.AddHistory("Power", title, Array.Empty<string>(), outcome.Success, outcome.LogFile);
    }

    private async Task RunOosuAsync()
    {
        var title = Loc.T("S.JobTitle.Oosu");
        var target = Path.Combine(AppPaths.Work, "OOSU10.exe");
        var outcome = await _shell.Job.RunAsync(title, async log =>
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Nimbus/" + AppInfo.Version);
            log("GET " + OosuUrl);
            using var response = await http.GetAsync(OosuUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var file = File.Create(target))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                var lastPercent = -1;
                while ((n = await source.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    var percent = total > 0 ? (int)(read * 100 / total) : -1;
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        _shell.Job.Report(percent, Loc.Format("S.Status.Downloading", read / 1048576.0, total / 1048576.0));
                    }
                }
            }

            log("Saved to " + target);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return false;
        });
        _shell.AddHistory("Oosu", title, Array.Empty<string>(), outcome.Success, outcome.LogFile);
    }

    private async Task LoadAppxAsync()
    {
        if (IsAppxLoading)
        {
            return;
        }

        IsAppxLoading = true;
        foreach (var a in _appx)
        {
            a.IsInstalled = null;
        }

        try
        {
            var result = await PowerShellRunner.RunAsync("Get-NimbusInstalledAppx", null);
            var prefix = PowerShellRunner.ProtocolPrefix + "APPX|";
            var installed = new HashSet<string>(
                result.Lines.Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).Select(l => l[prefix.Length..].Trim()),
                StringComparer.OrdinalIgnoreCase);
            foreach (var a in _appx)
            {
                a.IsInstalled = installed.Any(n => n.Contains(a.Definition.PackageId, StringComparison.OrdinalIgnoreCase));
            }

            _appxLoaded = true;
        }
        catch
        {
            // Leave the status as unknown.
        }
        finally
        {
            IsAppxLoading = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task RunAppxAsync(bool install)
    {
        var selected = _appx.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var ok = await _shell.Dialog.ShowAsync(
            Loc.T(install ? "S.Dlg.AppxInstall.Title" : "S.Dlg.AppxRemove.Title"),
            Loc.Format(install ? "S.Dlg.AppxInstall.Msg" : "S.Dlg.AppxRemove.Msg", selected.Count),
            selected.Select(a => a.Name),
            Loc.T(install ? "S.Appx.Install" : "S.Appx.Remove"),
            danger: !install);
        if (!ok)
        {
            return;
        }

        var script = new StringBuilder();
        if (install)
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var a = selected[i].Definition;
                script.AppendLine($"Write-NimbusProgress -Percent {i * 100 / selected.Count} -Status {PowerShellRunner.Q(Loc.Format("S.Status.Installing", a.Content))}");
                script.AppendLine($"try {{ Install-NimbusAppx -PackageId {PowerShellRunner.Q(a.PackageId)} -StoreId {PowerShellRunner.Q(a.StoreId)} }} catch {{ Write-WinUtilLog -Level 'ERROR' -Component 'AppX' -Message $_.Exception.Message }}");
            }
        }
        else
        {
            var ids = string.Join(", ", selected.Select(a => PowerShellRunner.Q(a.Definition.PackageId)));
            script.AppendLine($"Remove-NimbusAppx -PackageIds @({ids})");
        }

        var title = Loc.T(install ? "S.JobTitle.AppxInstall" : "S.JobTitle.AppxRemove");
        var outcome = await _shell.Job.RunScriptAsync(title, script.ToString());
        _shell.AddHistory(install ? "AppxInstall" : "AppxRemove", title, selected.Select(a => a.Name), outcome.Success, outcome.LogFile);

        foreach (var a in selected)
        {
            a.IsSelected = false;
        }

        await LoadAppxAsync();
    }

    public static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            Clipboard.SetText(url);
        }
    }
}
