using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nimbus.Interop;
using Nimbus.Models;
using Nimbus.ViewModels;

namespace Nimbus;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyBackdrop();
        StateChanged += (_, _) => UpdateMaximizedLayout();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.PageChanged -= OnPageChanged;
            oldVm.SettingsPage.BackdropChanged -= OnBackdropChanged;
        }

        if (e.NewValue is MainViewModel vm)
        {
            vm.PageChanged += OnPageChanged;
            vm.SettingsPage.BackdropChanged += OnBackdropChanged;
        }
    }

    private void OnBackdropChanged(object? sender, EventArgs e) => ApplyBackdrop();

    private void ApplyBackdrop()
    {
        var vm = ViewModel;
        var kind = vm?.Settings.Backdrop ?? BackdropKind.Acrylic;
        var supported = vm?.Machine.SupportsBackdrop ?? false;
        var translucent = WindowEffects.Apply(this, kind, supported);

        var resources = Application.Current.Resources;
        if (!translucent)
        {
            resources["WindowFillBrush"] = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));
            resources["SidebarFillBrush"] = new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x12));
        }
        else if (kind == BackdropKind.Mica)
        {
            resources["WindowFillBrush"] = new SolidColorBrush(Color.FromArgb(0x66, 0x08, 0x0B, 0x10));
            resources["SidebarFillBrush"] = new SolidColorBrush(Color.FromArgb(0x26, 0x00, 0x00, 0x00));
        }
        else
        {
            resources["WindowFillBrush"] = new SolidColorBrush(Color.FromArgb(0x8C, 0x08, 0x0B, 0x10));
            resources["SidebarFillBrush"] = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
        }
    }

    private void UpdateMaximizedLayout()
    {
        // A WindowChrome window overhangs the screen when maximized; pad it back in.
        if (WindowState == WindowState.Maximized)
        {
            var t = SystemParameters.WindowResizeBorderThickness;
            RootBorder.Margin = new Thickness(t.Left + 4, t.Top + 4, t.Right + 4, t.Bottom + 4);
            MaxButton.Content = "";
        }
        else
        {
            RootBorder.Margin = new Thickness(0);
            MaxButton.Content = "";
        }
    }

    private void OnPageChanged(object? sender, EventArgs e)
    {
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        if (PageHost.RenderTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration) { EasingFunction = ease });
        }
    }

    private void OnLogChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.ScrollToEnd();
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (ViewModel?.Job.IsBusy == true)
        {
            // Closing now would leave a half-applied change behind.
            e.Cancel = true;
            ViewModel.Toast.Show(Services.Loc.T("S.Toast.WaitForJob"), true);
        }

        base.OnClosing(e);
    }
}
