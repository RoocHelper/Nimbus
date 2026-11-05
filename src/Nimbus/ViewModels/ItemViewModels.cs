using Nimbus.Models;
using Nimbus.Services;

namespace Nimbus.ViewModels;

public sealed class TweakItemViewModel : ObservableObject
{
    private static readonly HashSet<string> HighRisk = new(StringComparer.OrdinalIgnoreCase)
    {
        "WPFTweaksRemoveEdge",
        "WPFTweaksRemoveOneDrive",
        "WPFTweaksDisableBitLocker",
        "WPFTweaksDisableIPv6",
        "WPFTweaksWindowsAI",
    };

    private static readonly HashSet<string> CautionInEssential = new(StringComparer.OrdinalIgnoreCase)
    {
        "WPFTweaksDisableExplorerAutoDiscovery",
        "WPFTweaksRevertStartMenu",
    };

    private readonly ConfigService _config;
    private bool _isSelected;
    private bool? _isApplied;
    private bool _isOn;
    private bool _isWorking;

    public TweakItemViewModel(TweakDefinition definition, ConfigService config)
    {
        Definition = definition;
        _config = config;
        Risk = HighRisk.Contains(definition.Key) ? RiskLevel.High
            : definition.IsAdvanced || CautionInEssential.Contains(definition.Key) ? RiskLevel.Caution
            : RiskLevel.Safe;
    }

    public TweakDefinition Definition { get; }
    public string Key => Definition.Key;
    public string Name => _config.TweakName(Definition);
    public string Description => _config.TweakDescription(Definition);
    public RiskLevel Risk { get; }
    public string RiskText => Loc.T("S.Risk." + Risk);
    public string? Link => Definition.Link;
    public bool HasUndo => Definition.HasUndo;

    /// <summary>Raised when the user flips a preference toggle. Args: item, requested state.</summary>
    public event Action<TweakItemViewModel, bool>? ToggleRequested;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>true = already active on this PC, false = not active, null = cannot be detected.</summary>
    public bool? IsApplied
    {
        get => _isApplied;
        set
        {
            if (SetProperty(ref _isApplied, value))
            {
                OnPropertyChanged(nameof(IsAppliedTrue));
            }
        }
    }

    public bool IsAppliedTrue => IsApplied == true;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value)
            {
                return;
            }

            _isOn = value;
            OnPropertyChanged();
            ToggleRequested?.Invoke(this, value);
        }
    }

    public bool IsWorking
    {
        get => _isWorking;
        set => SetProperty(ref _isWorking, value);
    }

    public void SetOnSilently(bool value)
    {
        if (_isOn != value)
        {
            _isOn = value;
            OnPropertyChanged(nameof(IsOn));
        }
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query)
        || Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Description.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Definition.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Definition.Key.Contains(query, StringComparison.OrdinalIgnoreCase);

    public void RefreshTexts() => OnAllPropertiesChanged();
}

public sealed class OptionViewModel : ObservableObject
{
    private bool _isSelected;
    private string _label;
    private string _description;

    public OptionViewModel(string key, string label, string description = "", string detail = "")
    {
        Key = key;
        _label = label;
        _description = description;
        Detail = detail;
    }

    public string Key { get; }
    public string Detail { get; }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class ComboTweakViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private string? _currentState;
    private bool _isWorking;

    public ComboTweakViewModel(TweakDefinition definition, ConfigService config)
    {
        Definition = definition;
        _config = config;
        var states = definition.ComboItems.Count > 0
            ? definition.ComboItems
            : definition.Registry.FirstOrDefault(r => r.Values is not null)?.Values?.Keys.ToList() ?? new List<string>();
        Options = states.Select(s => new OptionViewModel(s, config.ComboItem(s))).ToList();
    }

    public TweakDefinition Definition { get; }
    public IReadOnlyList<OptionViewModel> Options { get; }
    public string Name => _config.TweakName(Definition);
    public string Description => _config.TweakDescription(Definition);

    public string? CurrentState
    {
        get => _currentState;
        set
        {
            if (SetProperty(ref _currentState, value))
            {
                foreach (var o in Options)
                {
                    o.IsSelected = o.Key == value;
                }
            }
        }
    }

    public bool IsWorking
    {
        get => _isWorking;
        set => SetProperty(ref _isWorking, value);
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query)
        || Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Description.Contains(query, StringComparison.OrdinalIgnoreCase);

    public void RefreshTexts()
    {
        foreach (var o in Options)
        {
            o.Label = _config.ComboItem(o.Key);
        }

        OnAllPropertiesChanged();
    }
}

public sealed class AppxItemViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private bool _isSelected;
    private bool? _isInstalled;

    public AppxItemViewModel(AppxDefinition definition, ConfigService config)
    {
        Definition = definition;
        _config = config;
    }

    public AppxDefinition Definition { get; }
    public string Name => Definition.Content;
    public string Category => _config.AppxCategory(Definition);
    public string Description => _config.AppxDescription(Definition);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool? IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsInstalledTrue));
            }
        }
    }

    public bool IsInstalledTrue => IsInstalled == true;

    public string StatusText => IsInstalled switch
    {
        true => Loc.T("S.Appx.Installed"),
        false => Loc.T("S.Appx.NotInstalled"),
        _ => Loc.T("S.Appx.Checking"),
    };

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query)
        || Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Description.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Definition.PackageId.Contains(query, StringComparison.OrdinalIgnoreCase);

    public void RefreshTexts() => OnAllPropertiesChanged();
}

public sealed class HistoryItemViewModel : ObservableObject
{
    public HistoryItemViewModel(HistoryEntry entry)
    {
        Entry = entry;
    }

    public HistoryEntry Entry { get; }
    public string Title => Entry.Title;
    public string TimeText => Entry.Time.ToString(Loc.Language == "id" ? "dd MMM yyyy, HH:mm" : "MMM dd yyyy, HH:mm",
        System.Globalization.CultureInfo.GetCultureInfo(Loc.Language == "id" ? "id-ID" : "en-US"));
    public string ItemsText => Entry.Items.Count == 0 ? string.Empty : string.Join(" · ", Entry.Items.Take(6))
        + (Entry.Items.Count > 6 ? $" +{Entry.Items.Count - 6}" : string.Empty);
    public bool Success => Entry.Success;
    public bool CanUndo => Entry.Action == "Apply" && Entry.TweakKeys.Count > 0;
    public bool HasLog => Entry.LogFile is not null && System.IO.File.Exists(Entry.LogFile);

    public void RefreshTexts() => OnAllPropertiesChanged();
}
