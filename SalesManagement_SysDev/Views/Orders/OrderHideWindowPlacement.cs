using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SalesManagement_SysDev;

// Keeps SizeToContent.Height while constraining the dialog to its owner's monitor in physical pixels.
internal sealed class OrderHideWindowPlacement
{
    private readonly Window window;
    private HwndSource? source;
    private bool queued, closed, loaded, applying;

    public OrderHideWindowPlacement(Window window)
    {
        this.window = window;
        window.SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(WindowMessage);
            Apply(true);
        };
        window.Loaded += (_, _) => { loaded = true; Apply(true); };
        window.SizeChanged += (_, _) => { if (loaded && !applying) QueueApply(); };
        window.DpiChanged += (_, _) => QueueApply();
        window.Closed += (_, _) => { closed = true; source?.RemoveHook(WindowMessage); };
    }

    private void Apply(bool center)
    {
        if (closed || applying) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var anchorHandle = center && window.Owner != null ? new WindowInteropHelper(window.Owner).Handle : handle;
        if (anchorHandle == IntPtr.Zero) anchorHandle = handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(anchorHandle, 2), ref info) || !GetWindowRect(anchorHandle, out var anchor)) return;
        var dpi = GetDpiForWindow(anchorHandle);
        var scale = dpi > 0 ? dpi / 96d : VisualTreeHelper.GetDpi(window).DpiScaleX;
        var height = window.ActualHeight > 0 ? window.ActualHeight : 378;
        var placement = RegistrationWindowPlacement.Calculate(info.Work.ToRect(), anchor.ToRect(), scale, scale,
            460, height, center, 420, 0);
        applying = true;
        try
        {
            window.MinWidth = 0;
            window.MaxWidth = placement.MaxWidth; window.MaxHeight = placement.MaxHeight;
            window.MinWidth = placement.MinWidth;
            window.Width = placement.Pixels.Width / scale;
            SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(placement.Pixels.Left), (int)Math.Round(placement.Pixels.Top),
                0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
        }
        finally { applying = false; }
    }

    private void QueueApply()
    {
        if (queued || closed) return;
        queued = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { queued = false; Apply(false); }));
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x007E or 0x001A or 0x0232) QueueApply();
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
