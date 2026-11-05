using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nimbus.Models;

namespace Nimbus.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Options);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // A broken settings file falls back to defaults.
        }

        var fresh = new AppSettings();
        if (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "id")
        {
            fresh.Language = "en";
        }

        return fresh;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // Settings are a convenience; failing to save must not crash the app.
        }
    }
}

public static class HistoryService
{
    private const int MaxEntries = 200;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<HistoryEntry> Load()
    {
        try
        {
            if (File.Exists(AppPaths.HistoryFile))
            {
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(AppPaths.HistoryFile), Options) ?? new();
            }
        }
        catch
        {
            // Ignore a corrupt history file.
        }

        return new();
    }

    public static void Save(IEnumerable<HistoryEntry> entries)
    {
        try
        {
            File.WriteAllText(AppPaths.HistoryFile, JsonSerializer.Serialize(entries.Take(MaxEntries).ToList(), Options));
        }
        catch
        {
            // Ignore.
        }
    }
}
