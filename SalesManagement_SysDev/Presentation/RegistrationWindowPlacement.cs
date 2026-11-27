using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

// Window geometry only. Native rectangles stay in pixels until dimensions are converted to WPF units.
internal sealed class RegistrationWindowPlacement
{
    private readonly Window window;
    private readonly double preferredWidth, preferredHeight, minimumWidth, minimumHeight;
    private HwndSource? source;
    private bool queued, closed;

    public RegistrationWindowPlacement(Window window, double preferredWidth = 1150, double preferredHeight = 760,
        double minimumWidth = 950, double minimumHeight = 600)
    {
        this.window = window;
        this.preferredWidth = preferredWidth; this.preferredHeight = preferredHeight;
        this.minimumWidth = minimumWidth; this.minimumHeight = minimumHeight;
        window.SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(WindowMessage);
            Apply(true);
        };
        window.Loaded += (_, _) => Apply(true);
        window.DpiChanged += (_, _) => QueueApply();
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Maximized) window.WindowState = WindowState.Normal;
            QueueApply();
        };
        window.Closed += (_, _) => { closed = true; source?.RemoveHook(WindowMessage); };
    }


    internal sealed record Placement(Rect Pixels, double MinWidth, double MinHeight, double MaxWidth, double MaxHeight);

    internal static Placement Calculate(Rect workPixels, Rect anchorPixels, double scaleX, double scaleY,
        double preferredWidth = 1150, double preferredHeight = 760, bool center = true,
        double minimumWidth = 950, double minimumHeight = 600)
    {
        var maxWidth = Math.Floor(workPixels.Width * .95) / scaleX;
        var maxHeight = Math.Floor(workPixels.Height * .90) / scaleY;
        var minWidth = Math.Min(minimumWidth, maxWidth);
        var minHeight = Math.Min(minimumHeight, maxHeight);
        var widthPixels = Math.Min(Math.Ceiling(Math.Clamp(preferredWidth, minWidth, maxWidth) * scaleX), Math.Floor(workPixels.Width * .95));
        var heightPixels = Math.Min(Math.Ceiling(Math.Clamp(preferredHeight, minHeight, maxHeight) * scaleY), Math.Floor(workPixels.Height * .90));
        var left = center ? anchorPixels.Left + (anchorPixels.Width - widthPixels) / 2 : anchorPixels.Left;
        var top = center ? anchorPixels.Top + (anchorPixels.Height - heightPixels) / 2 : anchorPixels.Top;
        var bounds = new Rect(Math.Clamp(left, workPixels.Left, workPixels.Right - widthPixels),
            Math.Clamp(top, workPixels.Top, workPixels.Bottom - heightPixels), widthPixels, heightPixels);
        return new Placement(bounds, minWidth, minHeight, maxWidth, maxHeight);
    }

    internal static Rect ReadWorkAreaAt(Point screenPoint)
    {
        var monitor = MonitorFromPoint(new NativePoint((int)screenPoint.X, (int)screenPoint.Y), 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info) ? info.Work.ToRect() : Rect.Empty;
    }

    private void Apply(bool center)
    {
        if (closed || window.WindowState == WindowState.Minimized) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var anchorHandle = center && window.Owner != null ? new WindowInteropHelper(window.Owner).Handle : handle;
        if (anchorHandle == IntPtr.Zero) anchorHandle = handle;
        var monitor = MonitorFromWindow(anchorHandle, 2); // MONITOR_DEFAULTTONEAREST
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(anchorHandle, out var anchor)) return;
        var dpi = GetDpiForWindow(anchorHandle);
        var wpfDpi = VisualTreeHelper.GetDpi(window);
        var scaleX = dpi > 0 ? dpi / 96d : wpfDpi.DpiScaleX;
        var scaleY = dpi > 0 ? dpi / 96d : wpfDpi.DpiScaleY;
        var placement = Calculate(info.Work.ToRect(), anchor.ToRect(), scaleX, scaleY,
            center ? preferredWidth : window.Width, center ? preferredHeight : window.Height, center, minimumWidth, minimumHeight);

        // Relax the old minimum before lowering maxima on a smaller monitor.
        window.MinWidth = 0; window.MinHeight = 0;
        window.MaxWidth = placement.MaxWidth; window.MaxHeight = placement.MaxHeight;
        window.MinWidth = placement.MinWidth; window.MinHeight = placement.MinHeight;
        window.Width = placement.Pixels.Width / scaleX;
        window.Height = placement.Pixels.Height / scaleY;
        var bounds = placement.Pixels;
        SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
            (int)bounds.Width, (int)bounds.Height, 0x0014); // SWP_NOZORDER | SWP_NOACTIVATE
    }

    private void QueueApply()
    {
        if (queued || closed) return;
        queued = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            queued = false;
            Apply(false);
        }));
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x007E or 0x001A or 0x0232) QueueApply(); // Display/work-area change, end of move/resize
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x, Y = y;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
        int x, int y, int width, int height, uint flags);
}
