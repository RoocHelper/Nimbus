using System.Windows;

namespace Nimbus.Services;

/// <summary>
/// Tiny localization helper. UI strings live in Localization/Strings.{lang}.xaml and are used from
/// XAML through {DynamicResource S.Key}; swapping the dictionary re-translates the whole window.
/// </summary>
public static class Loc
{
    public static string Language { get; private set; } = "id";

    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(string language)
    {
        language = language == "en" ? "en" : "id";
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source is not null && d.Source.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase));
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Nimbus;component/Localization/Strings.{language}.xaml", UriKind.Absolute),
        };

        if (existing is null)
        {
            dictionaries.Add(replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }

        var changed = Language != language;
        Language = language;
        if (changed)
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static string T(string key)
    {
        return Application.Current?.TryFindResource(key) as string ?? key;
    }

    public static string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(T(key), args);
        }
        catch (FormatException)
        {
            return T(key);
        }
    }
}
