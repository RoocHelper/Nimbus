using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Nimbus.Models;

namespace Nimbus.Interop;

/// <summary>Windows 11 Mica/Acrylic backdrop, dark title area and rounded corners through DWM.</summary>
internal static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    private const int BackdropNone = 1;
    private const int BackdropMica = 2;
    private const int BackdropAcrylic = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    /// <summary>Applies the backdrop. Returns true when a translucent backdrop is active.</summary>
    public static bool Apply(Window window, BackdropKind kind, bool supported)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var source = HwndSource.FromHwnd(hwnd);
        try
        {
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            var round = 2;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));

            if (!supported || kind == BackdropKind.Solid)
            {
                var none = BackdropNone;
                DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref none, sizeof(int));
                var zero = new Margins();
                DwmExtendFrameIntoClientArea(hwnd, ref zero);
                if (source?.CompositionTarget is not null)
                {
                    source.CompositionTarget.BackgroundColor = Color.FromRgb(0x0D, 0x11, 0x17);
                }

                return false;
            }

            if (source?.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            var type = kind == BackdropKind.Mica ? BackdropMica : BackdropAcrylic;
            return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
