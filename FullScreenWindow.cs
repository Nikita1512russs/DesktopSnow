using System.Text;

namespace DesktopSnow;

internal sealed class FullScreenWindow
{
    private readonly StringBuilder className = new(128);

    public (Rectangle Bounds, bool Maximized) ReadForeground() => Read(Native.GetForegroundWindow());

    internal (Rectangle Bounds, bool Maximized) Read(nint window)
    {
        if (window == 0 || window == Native.GetDesktopWindow() || window == Native.GetShellWindow() ||
            !Native.IsWindowVisible(window) || Native.IsIconic(window)) return default;

        Native.GetWindowThreadProcessId(window, out uint process);
        if (process == (uint)Environment.ProcessId) return default;
        int style = (int)Native.GetWindowLongPtr(window, -20);
        if ((style & (Native.ExToolWindow | Native.ExNoActivate)) != 0) return default;

        Native.GetClassName(window, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return default;
        if (Native.DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return default;

        if (Native.DwmGetWindowAttribute(window, 9, out Native.Rect frame, 16) != 0 &&
            !Native.GetWindowRect(window, out frame)) return default;
        return (frame.ToDrawing(), Native.IsZoomed(window));
    }

    public static bool CoversDisplay((Rectangle Bounds, bool Maximized) window, Rectangle monitor,
        Rectangle workArea, float scale)
    {
        if (window.Bounds.Width <= 0 || window.Bounds.Height <= 0) return false;

        // Small gaps due to rounded corners or DPI rounding still count as coverage.
        int tolerance = Math.Max(2, (int)Math.Ceiling(2 * scale));
        return Covers(monitor) || window.Maximized && Covers(workArea);

        bool Covers(Rectangle area) => area.Width > tolerance * 2 && area.Height > tolerance * 2 &&
            window.Bounds.Left <= area.Left + tolerance && window.Bounds.Top <= area.Top + tolerance &&
            window.Bounds.Right >= area.Right - tolerance && window.Bounds.Bottom >= area.Bottom - tolerance;
    }
}
