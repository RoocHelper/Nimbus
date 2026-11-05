using System.IO;
using System.Windows;
using System.Windows.Threading;
using Nimbus.Services;
using Nimbus.ViewModels;

namespace Nimbus;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppPaths.WriteCrash(args.Exception);
            args.SetObserved();
        };

        var settings = SettingsService.Load();
        Loc.SetLanguage(settings.Language);

        MainViewModel viewModel;
        try
        {
            viewModel = new MainViewModel(settings);
        }
        catch (Exception ex)
        {
            var path = AppPaths.WriteCrash(ex);
            MessageBox.Show(
                Loc.Format("S.Error.Unexpected", ex.Message, Path.GetDirectoryName(path) ?? string.Empty),
                "Nimbus",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        _ = viewModel.InitializeAsync();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var path = AppPaths.WriteCrash(e.Exception);
        MessageBox.Show(
            Loc.Format("S.Error.Unexpected", e.Exception.Message, Path.GetDirectoryName(path) ?? string.Empty),
            "Nimbus",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
