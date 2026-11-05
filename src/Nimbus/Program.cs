using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Windows;
using Velopack;

namespace Nimbus;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must run first: it handles install/update/uninstall hooks and exits when needed.
        VelopackApp.Build().Run();

        if (!IsAdministrator())
        {
            if (TryRelaunchElevated(args))
            {
                return;
            }

            var indonesian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "id";
            MessageBox.Show(
                indonesian
                    ? "Nimbus perlu izin administrator untuk mengubah pengaturan Windows.\nBuka lagi lalu pilih \"Yes\" saat diminta."
                    : "Nimbus needs administrator rights to change Windows settings.\nOpen it again and choose \"Yes\" when asked.",
                "Nimbus",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryRelaunchElevated(string[] args)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args.Select(a => "\"" + a.Replace("\"", "\\\"") + "\"")),
                WorkingDirectory = AppContext.BaseDirectory,
            });
            return true;
        }
        catch (Win32Exception)
        {
            // 1223 = the user pressed "No" on the UAC prompt.
            return false;
        }
    }
}
