namespace Nimbus.Models;

/// <summary>One entry of WinUtil's config/tweaks.json.</summary>
public sealed class TweakDefinition
{
    public required string Key { get; init; }
    public required string Content { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? Type { get; init; }
    public string? Link { get; init; }
    public List<RegistryEntry> Registry { get; init; } = new();
    public List<ServiceEntry> Services { get; init; } = new();
    public List<string> InvokeScript { get; init; } = new();
    public List<string> UndoScript { get; init; } = new();
    public List<string> Appx { get; init; } = new();
    public List<string> ComboItems { get; init; } = new();

    public bool IsToggle => string.Equals(Type, "Toggle", StringComparison.OrdinalIgnoreCase);
    public bool IsCombo => string.Equals(Type, "Combobox", StringComparison.OrdinalIgnoreCase);
    public bool IsButton => string.Equals(Type, "Button", StringComparison.OrdinalIgnoreCase);

    public bool IsEssential => Category.StartsWith("Essential", StringComparison.OrdinalIgnoreCase);
    public bool IsAdvanced => Category.Contains("Advanced", StringComparison.OrdinalIgnoreCase);
    public bool IsPreference => Category.StartsWith("Customize", StringComparison.OrdinalIgnoreCase);

    /// <summary>Plain tweak that is selected and then applied/undone in a batch.</summary>
    public bool IsSelectable => Type is null && (IsEssential || IsAdvanced);

    public bool HasUndo =>
        UndoScript.Count > 0
        || Services.Count > 0
        || Registry.Any(r => r.OriginalValue is not null);
}

public sealed class RegistryEntry
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public string Type { get; init; } = "DWord";
    public string? Value { get; init; }
    public string? OriginalValue { get; init; }
    public string? DefaultState { get; init; }
    public string? DefaultValue { get; init; }

    /// <summary>Per-state values for a registry-backed combobox (e.g. Multiplane Overlay).</summary>
    public Dictionary<string, string>? Values { get; init; }
}

public sealed class ServiceEntry
{
    public required string Name { get; init; }
    public string StartupType { get; init; } = string.Empty;
    public string OriginalType { get; init; } = string.Empty;
}

public sealed class DnsProvider
{
    public required string Name { get; init; }
    public string? Primary { get; init; }
    public string? Secondary { get; init; }
    public string? Primary6 { get; init; }
    public string? Secondary6 { get; init; }
    public string? DohTemplate { get; init; }
    public bool BenchmarkEligible { get; init; }
}

public sealed class AppxDefinition
{
    public required string Key { get; init; }
    public required string Content { get; init; }
    public required string PackageId { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? StoreId { get; init; }
}

public enum RiskLevel
{
    Safe,
    Caution,
    High,
}

public enum UpdateMode
{
    Default,
    Recommended,
    Disabled,
}

public enum BackdropKind
{
    Acrylic,
    Mica,
    Solid,
}

public sealed class HistoryEntry
{
    public DateTime Time { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> TweakKeys { get; set; } = new();
    public List<string> Items { get; set; } = new();
    public bool Success { get; set; }
    public string? LogFile { get; set; }
}

public sealed class AppSettings
{
    public string Language { get; set; } = "id";
    public BackdropKind Backdrop { get; set; } = BackdropKind.Acrylic;
    public bool AutoRestorePoint { get; set; } = true;
    public bool CheckUpdatesOnStartup { get; set; } = true;
}
