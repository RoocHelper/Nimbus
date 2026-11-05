using System.IO;
using System.Text;
using System.Text.Json;
using Nimbus.Models;

namespace Nimbus.Services;

/// <summary>
/// Loads WinUtil's JSON configuration (tweaks, DNS, presets, AppX) plus Nimbus' Indonesian texts.
/// WinUtil's JSON contains raw line breaks inside strings (PowerShell accepts that, System.Text.Json
/// does not), so every file is sanitized before parsing.
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public IReadOnlyList<TweakDefinition> Tweaks { get; private set; } = Array.Empty<TweakDefinition>();
    public IReadOnlyList<DnsProvider> DnsProviders { get; private set; } = Array.Empty<DnsProvider>();
    public IReadOnlyDictionary<string, List<string>> Presets { get; private set; } = new Dictionary<string, List<string>>();
    public IReadOnlyList<AppxDefinition> Appx { get; private set; } = Array.Empty<AppxDefinition>();

    private Dictionary<string, (string? Name, string? Desc)> _tweakTextId = new();
    private Dictionary<string, string> _appxDescId = new();
    private Dictionary<string, string> _appxCategoryId = new();
    private Dictionary<string, string> _comboId = new();

    public string DataDirectory => AppPaths.BundledData;

    public TweakDefinition? FindTweak(string key) => Tweaks.FirstOrDefault(t => t.Key == key);

    public void Load()
    {
        Tweaks = ParseTweaks(ReadDocument("tweaks.json"));
        DnsProviders = ParseDns(ReadDocument("dns.json"));
        Presets = ParsePresets(ReadDocument("preset.json"));
        Appx = ParseAppx(ReadDocument("appx.json"));
        LoadIndonesianTexts();
    }

    // ---------- Localized texts ----------

    public string TweakName(TweakDefinition t) =>
        Loc.Language == "id" && _tweakTextId.TryGetValue(t.Key, out var x) && !string.IsNullOrWhiteSpace(x.Name)
            ? x.Name!
            : t.Content;

    public string TweakDescription(TweakDefinition t) =>
        Loc.Language == "id" && _tweakTextId.TryGetValue(t.Key, out var x) && !string.IsNullOrWhiteSpace(x.Desc)
            ? x.Desc!
            : t.Description;

    public string AppxDescription(AppxDefinition a) =>
        Loc.Language == "id" && _appxDescId.TryGetValue(a.Key, out var d) ? d : a.Description;

    public string AppxCategory(AppxDefinition a) =>
        Loc.Language == "id" && _appxCategoryId.TryGetValue(a.Category, out var c) ? c : a.Category;

    public string ComboItem(string item) =>
        Loc.Language == "id" && _comboId.TryGetValue(item, out var c) ? c : item;

    // ---------- Parsing ----------

    private JsonDocument? ReadDocument(string fileName)
    {
        var path = Path.Combine(DataDirectory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonDocument.Parse(Sanitize(File.ReadAllText(path, Encoding.UTF8)), DocOptions);
    }

    private static List<TweakDefinition> ParseTweaks(JsonDocument? doc)
    {
        var list = new List<TweakDefinition>();
        if (doc is null)
        {
            return list;
        }

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var e = prop.Value;
            if (e.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            list.Add(new TweakDefinition
            {
                Key = prop.Name,
                Content = Str(e, "Content") ?? prop.Name,
                Description = Str(e, "Description") ?? string.Empty,
                Category = Str(e, "category") ?? string.Empty,
                Type = Str(e, "Type"),
                Link = Str(e, "link"),
                Registry = Items(e, "registry").Select(ParseRegistry).Where(r => r is not null).Select(r => r!).ToList(),
                Services = Items(e, "service").Select(ParseService).Where(s => s is not null).Select(s => s!).ToList(),
                InvokeScript = StringArray(e, "InvokeScript"),
                UndoScript = StringArray(e, "UndoScript"),
                Appx = StringArray(e, "appx"),
                ComboItems = (Str(e, "ComboItems") ?? string.Empty)
                    .Split(prop.Name == "WPFchangedns" ? ' ' : '|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
            });
        }

        return list;
    }

    private static RegistryEntry? ParseRegistry(JsonElement e)
    {
        var path = Str(e, "Path");
        var name = Str(e, "Name");
        if (path is null || name is null)
        {
            return null;
        }

        Dictionary<string, string>? values = null;
        if (e.TryGetProperty("Values", out var v) && v.ValueKind == JsonValueKind.Object)
        {
            values = new Dictionary<string, string>();
            foreach (var p in v.EnumerateObject())
            {
                values[p.Name] = AsString(p.Value) ?? string.Empty;
            }
        }

        return new RegistryEntry
        {
            Path = path,
            Name = name,
            Type = Str(e, "Type") ?? "DWord",
            Value = Str(e, "Value"),
            OriginalValue = Str(e, "OriginalValue"),
            DefaultState = Str(e, "DefaultState"),
            DefaultValue = Str(e, "DefaultValue"),
            Values = values,
        };
    }

    private static ServiceEntry? ParseService(JsonElement e)
    {
        var name = Str(e, "Name");
        return name is null
            ? null
            : new ServiceEntry
            {
                Name = name,
                StartupType = Str(e, "StartupType") ?? string.Empty,
                OriginalType = Str(e, "OriginalType") ?? string.Empty,
            };
    }

    private static List<DnsProvider> ParseDns(JsonDocument? doc)
    {
        var list = new List<DnsProvider>();
        if (doc is null)
        {
            return list;
        }

        foreach (var p in doc.RootElement.EnumerateObject())
        {
            list.Add(new DnsProvider
            {
                Name = p.Name,
                Primary = Str(p.Value, "Primary"),
                Secondary = Str(p.Value, "Secondary"),
                Primary6 = Str(p.Value, "Primary6"),
                Secondary6 = Str(p.Value, "Secondary6"),
                DohTemplate = Str(p.Value, "DohTemplate"),
                BenchmarkEligible = p.Value.TryGetProperty("BenchmarkEligible", out var b) && b.ValueKind == JsonValueKind.True,
            });
        }

        return list;
    }

    private static Dictionary<string, List<string>> ParsePresets(JsonDocument? doc)
    {
        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (doc is null)
        {
            return dict;
        }

        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Array)
            {
                dict[p.Name] = p.Value.EnumerateArray().Select(AsString).Where(s => s is not null).Select(s => s!).ToList();
            }
        }

        return dict;
    }

    private static List<AppxDefinition> ParseAppx(JsonDocument? doc)
    {
        var list = new List<AppxDefinition>();
        if (doc is null)
        {
            return list;
        }

        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var packageId = Str(p.Value, "PackageId");
            if (packageId is null)
            {
                continue;
            }

            list.Add(new AppxDefinition
            {
                Key = p.Name,
                Content = Str(p.Value, "Content") ?? packageId,
                PackageId = packageId,
                Category = Str(p.Value, "Category") ?? string.Empty,
                Description = Str(p.Value, "Description") ?? string.Empty,
                StoreId = Str(p.Value, "StoreId"),
            });
        }

        return list;
    }

    private void LoadIndonesianTexts()
    {
        var path = Path.Combine(DataDirectory, "i18n", "id.json");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8), DocOptions);
            var root = doc.RootElement;

            _tweakTextId = new();
            if (root.TryGetProperty("tweaks", out var tweaks))
            {
                foreach (var p in tweaks.EnumerateObject())
                {
                    _tweakTextId[p.Name] = (Str(p.Value, "name"), Str(p.Value, "desc"));
                }
            }

            _appxDescId = ReadStringMap(root, "appx");
            _appxCategoryId = ReadStringMap(root, "appxCategories");
            _comboId = ReadStringMap(root, "combo");
        }
        catch
        {
            // Missing translations fall back to English.
        }
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement root, string name)
    {
        var map = new Dictionary<string, string>();
        if (root.TryGetProperty(name, out var obj) && obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in obj.EnumerateObject())
            {
                var s = AsString(p.Value);
                if (s is not null)
                {
                    map[p.Name] = s;
                }
            }
        }

        return map;
    }

    // ---------- JSON helpers ----------

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? AsString(v) : null;

    private static string? AsString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static IEnumerable<JsonElement> Items(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToList()
            : Enumerable.Empty<JsonElement>();

    private static List<string> StringArray(JsonElement e, string name) =>
        Items(e, name).Select(AsString).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

    /// <summary>Escapes raw control characters that appear inside JSON string literals.</summary>
    public static string Sanitize(string json)
    {
        var sb = new StringBuilder(json.Length + 256);
        var inString = false;
        var escaped = false;
        foreach (var c in json)
        {
            if (!inString)
            {
                if (c == '"')
                {
                    inString = true;
                }

                sb.Append(c);
                continue;
            }

            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            switch (c)
            {
                case '\\':
                    sb.Append(c);
                    escaped = true;
                    break;
                case '"':
                    sb.Append(c);
                    inString = false;
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}
