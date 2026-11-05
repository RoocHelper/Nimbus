using System.IO;
using System.Reflection;
using System.Text;

namespace Nimbus.Services;

public static class AppInfo
{
    /// <summary>
    /// GitHub repository that hosts the Nimbus releases. Auto-update reads its Releases page.
    /// CHANGE THIS to your own repository before publishing, e.g. https://github.com/RoocHelper/Nimbus
    /// </summary>
    public const string GitHubRepoUrl = "https://github.com/RoocHelper/Nimbus";

    public const string WinUtilRepoUrl = "https://github.com/ChrisTitusTech/winutil";

    public static string Version
    {
        get
        {
            var info = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info))
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            }

            // Strip the "+commit" suffix the SDK appends.
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
    }

    public static bool IsRepoConfigured => !GitHubRepoUrl.Contains("YOUR_GITHUB_USERNAME", StringComparison.Ordinal);
}

public static class AppPaths
{
    /// <summary>%AppData%\Nimbus — kept outside the Velopack install folder so updates never wipe it.</summary>
    public static string DataRoot { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nimbus"));

    public static string Logs { get; } = Ensure(Path.Combine(DataRoot, "logs"));

    public static string Work { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NimbusWork"));

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string HistoryFile => Path.Combine(DataRoot, "history.json");

    public static string BundledData => Path.Combine(AppContext.BaseDirectory, "Data");
    public static string BundledScripts => Path.Combine(AppContext.BaseDirectory, "Scripts");

    public static string WriteCrash(Exception ex)
    {
        var path = Path.Combine(Logs, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        try
        {
            File.WriteAllText(path, ex.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Nothing else we can do.
        }

        return path;
    }

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
