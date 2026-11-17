using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UtilitiesManager;

/// <summary>
/// Position d'une fenêtre relevée et remise par Windows, en pixels : avec plusieurs écrans à des mises à l'échelle
/// différentes, les unités WPF (<c>Left</c> / <c>Top</c>) rouvriraient une fenêtre agrandie sur l'écran principal.
/// </summary>
internal static class WindowPosition
{
    private const int ShowNormal = 1;
    private const int ShowMaximized = 3;
    private const int MonitorDefaultToNull = 0;

    /// <summary>
    /// Remet la position enregistrée à la création de la fenêtre, avant son affichage, si elle touche encore un
    /// écran branché ; sinon la fenêtre garde sa position par défaut.
    /// </summary>
    public static void Restore(Window window, WindowPlacement? placement)
    {
        if (placement is null)
            return;
        var bounds = new NativeRect
        {
            Left = (int)placement.Left,
            Top = (int)placement.Top,
            Right = (int)(placement.Left + placement.Width),
            Bottom = (int)(placement.Top + placement.Height),
        };
        if (MonitorFromRect(ref bounds, MonitorDefaultToNull) == IntPtr.Zero)
            return;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SourceInitialized += (_, _) =>
        {
            var native = NativePlacement.Create();
            native.ShowCmd = placement.Maximized ? ShowMaximized : ShowNormal;
            native.NormalPosition = bounds;
            SetWindowPlacement(new WindowInteropHelper(window).Handle, ref native);
        };
    }

    /// <summary>Position normale (hors agrandissement) et état agrandi de la fenêtre ; null si Windows ne la donne pas.</summary>
    public static WindowPlacement? Read(Window window)
    {
        var native = NativePlacement.Create();
        if (!GetWindowPlacement(new WindowInteropHelper(window).Handle, ref native))
            return null;
        var bounds = native.NormalPosition;
        return new WindowPlacement(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                                   native.ShowCmd == ShowMaximized);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;

        public static NativePlacement Create() => new() { Length = Marshal.SizeOf<NativePlacement>() };
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr handle, ref NativePlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr handle, ref NativePlacement placement);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, int flags);
}
