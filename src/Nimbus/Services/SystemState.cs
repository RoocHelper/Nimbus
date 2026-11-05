using System.Globalization;
using Microsoft.Win32;
using Nimbus.Models;

namespace Nimbus.Services;

/// <summary>Reads registry values using WinUtil's "HKLM:\..." path notation.</summary>
public static class RegistryReader
{
    public const string RemoveEntry = "<RemoveEntry>";

    public static (bool Exists, string? Value) Read(string psPath, string name)
    {
        try
        {
            var (hive, subKey) = Split(psPath);
            if (hive is null)
            {
                return (false, null);
            }

            using var baseKey = RegistryKey.OpenBaseKey(hive.Value, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey, false);
            var raw = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return raw is null ? (false, null) : (true, Format(raw));
        }
        catch
        {
            return (false, null);
        }
    }

    public static bool ValueEquals(string? actual, string? expected)
    {
        if (actual is null || expected is null)
        {
            return actual == expected;
        }

        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // DWORDs above 0x7FFFFFFF come back as negative Int32 values.
        if (long.TryParse(actual, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
            && long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            return a == b || (a < 0 && unchecked((uint)a) == b);
        }

        return false;
    }

    private static string Format(object raw) => raw switch
    {
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        string s => s,
        string[] arr => string.Join("\n", arr),
        byte[] bytes => Convert.ToHexString(bytes),
        _ => Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static (RegistryHive? Hive, string SubKey) Split(string psPath)
    {
        var colon = psPath.IndexOf(':');
        if (colon < 0)
        {
            return (null, string.Empty);
        }

        var root = psPath[..colon].ToUpperInvariant();
        var sub = psPath[(colon + 1)..].TrimStart('\\');
        RegistryHive? hive = root switch
        {
            "HKLM" => RegistryHive.LocalMachine,
            "HKCU" => RegistryHive.CurrentUser,
            "HKU" => RegistryHive.Users,
            "HKCR" => RegistryHive.ClassesRoot,
            _ => null,
        };
        return (hive, sub);
    }
}

/// <summary>Best-effort detection of tweaks that are already applied (mirrors WinUtil's toggle status logic).</summary>
public static class TweakStatusService
{
    /// <returns>true/false when the state can be read from the registry/services, null when unknown.</returns>
    public static bool? IsApplied(TweakDefinition tweak)
    {
        var registry = tweak.Registry.Where(r => r.Values is null).ToList();
        if (registry.Count == 0 && tweak.Services.Count == 0)
        {
            return null;
        }

        foreach (var reg in registry)
        {
            var (exists, current) = RegistryReader.Read(reg.Path, reg.Name);
            if (!exists)
            {
                current = reg.DefaultState?.ToLowerInvariant() switch
                {
                    "true" => reg.Value,
                    "false" => reg.OriginalValue,
                    _ => null,
                };

                if (current == RegistryReader.RemoveEntry)
                {
                    current = null;
                }
            }

            if (reg.Value == RegistryReader.RemoveEntry)
            {
                if (current is not null)
                {
                    return false;
                }

                continue;
            }

            if (!RegistryReader.ValueEquals(current, reg.Value))
            {
                return false;
            }
        }

        foreach (var svc in tweak.Services)
        {
            var current = ServiceStartType(svc.Name);
            if (current is null)
            {
                continue; // service not present on this PC
            }

            if (!string.Equals(current, svc.StartupType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns the combobox state whose registry values match the system (like Get-WinUtilRegistryComboState).</summary>
    public static string? ComboState(TweakDefinition tweak)
    {
        var settings = tweak.Registry.Where(r => r.Values is not null).ToList();
        if (settings.Count == 0)
        {
            return null;
        }

        foreach (var state in settings[0].Values!.Keys)
        {
            var matches = true;
            foreach (var s in settings)
            {
                var (exists, value) = RegistryReader.Read(s.Path, s.Name);
                var actual = exists && value is not null ? value : s.DefaultValue;
                var configured = s.Values!.TryGetValue(state, out var c) ? c : null;
                var expected = configured == RegistryReader.RemoveEntry ? s.DefaultValue : configured;
                if (!RegistryReader.ValueEquals(actual, expected))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return state;
            }
        }

        return null;
    }

    public static string? ServiceStartType(string serviceName)
    {
        var (exists, start) = RegistryReader.Read($@"HKLM:\SYSTEM\CurrentControlSet\Services\{serviceName}", "Start");
        if (!exists || start is null)
        {
            return null;
        }

        var (_, delayed) = RegistryReader.Read($@"HKLM:\SYSTEM\CurrentControlSet\Services\{serviceName}", "DelayedAutostart");
        return start switch
        {
            "0" or "1" => "Boot",
            "2" => delayed == "1" ? "AutomaticDelayedStart" : "Automatic",
            "3" => "Manual",
            "4" => "Disabled",
            _ => null,
        };
    }
}

public sealed class SystemInfo
{
    public string ProductName { get; init; } = "Windows";
    public string DisplayVersion { get; init; } = string.Empty;
    public string EditionId { get; init; } = string.Empty;
    public int Build { get; init; }

    public bool IsWindows11 => Build >= 22000;
    public bool IsHomeEdition => EditionId.StartsWith("Core", StringComparison.OrdinalIgnoreCase);

    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE (Mica/Acrylic) is available from Windows 11 22H2.</summary>
    public bool SupportsBackdrop => Build >= 22621;

    public string FriendlyName => string.IsNullOrEmpty(DisplayVersion) ? ProductName : $"{ProductName} {DisplayVersion}";

    public static SystemInfo Read()
    {
        const string key = @"HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var product = RegistryReader.Read(key, "ProductName").Value ?? "Windows";
        var buildText = RegistryReader.Read(key, "CurrentBuildNumber").Value ?? RegistryReader.Read(key, "CurrentBuild").Value;
        _ = int.TryParse(buildText, out var build);
        if (build >= 22000)
        {
            // Windows 11 still reports "Windows 10" as its product name.
            product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        return new SystemInfo
        {
            ProductName = product,
            DisplayVersion = RegistryReader.Read(key, "DisplayVersion").Value ?? string.Empty,
            EditionId = RegistryReader.Read(key, "EditionID").Value ?? string.Empty,
            Build = build,
        };
    }
}

public static class UpdateModeService
{
    private const string Policy = @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    public static UpdateMode Detect()
    {
        if (RegistryReader.Read(Policy + @"\AU", "NoAutoUpdate").Value == "1"
            || TweakStatusService.ServiceStartType("wuauserv") == "Disabled")
        {
            return UpdateMode.Disabled;
        }

        var feature = RegistryReader.Read(Policy, "DeferFeatureUpdatesPeriodInDays").Value;
        var quality = RegistryReader.Read(Policy, "DeferQualityUpdatesPeriodInDays").Value;
        if (feature == "365" && quality == "4")
        {
            return UpdateMode.Recommended;
        }

        return UpdateMode.Default;
    }
}
