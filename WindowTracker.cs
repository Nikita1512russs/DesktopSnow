using System.Text;

namespace DesktopSnow;

internal readonly record struct DesktopWindow(nint Handle, Rectangle Bounds, bool CollectsSnow);

internal sealed class WindowTracker : IDisposable
{
    private readonly List<nint> hooks = [];
    private readonly Native.WinEventProc onEvent;
    private readonly HashSet<nint> overlays;
    private readonly uint processId = (uint)Environment.ProcessId;
    private Task<DesktopWindow[]>? pendingSnapshot;

    public bool Dirty { get; set; } = true;
    public HashSet<nint> StoppedWindows { get; } = [];
    public HashSet<nint> RemovedWindows { get; } = [];

    public WindowTracker(HashSet<nint> overlays)
    {
        this.overlays = overlays;
        onEvent = OnEvent;
        foreach (var (first, last) in new (uint, uint)[]
        {
            (Native.EventForeground, Native.EventForeground),
            (Native.EventMoveSizeEnd, Native.EventMoveSizeEnd),
            (Native.EventMinimizeStart, Native.EventMinimizeEnd),
            (Native.EventDestroy, Native.EventReorder),
            (Native.EventLocation, Native.EventLocation),
            (Native.EventCloaked, Native.EventUncloaked)
        })
        {
            nint hook = Native.SetWinEventHook(first, last, 0, onEvent, 0, 0, 0x0002);
            if (hook != 0) hooks.Add(hook);
            else Settings.Log($"SetWinEventHook({first:X}-{last:X}) failed");
        }
    }

    private void OnEvent(nint hook, uint kind, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (objectId != 0 || childId != 0 || overlays.Contains(hwnd)) return;
        Dirty = true;
        if (kind == Native.EventMoveSizeEnd) StoppedWindows.Add(hwnd);
        if (kind is Native.EventDestroy or Native.EventHide or Native.EventMinimizeStart or Native.EventCloaked)
            RemovedWindows.Add(hwnd);
    }

    public bool SnapshotPending => pendingSnapshot is not null;

    public void RequestSnapshot()
    {
        if (pendingSnapshot is not null) return;
        Dirty = false;
        pendingSnapshot = Task.Run(ReadSnapshot);
    }

    public DesktopWindow[]? TakeSnapshot()
    {
        if (pendingSnapshot is not { IsCompleted: true } work) return null;
        pendingSnapshot = null;
        if (work.IsCompletedSuccessfully) return work.Result;
        Settings.Log($"Ошибка чтения списка окон: {work.Exception}");
        return null;
    }

    private DesktopWindow[] ReadSnapshot()
    {
        nint previousDpi = Native.SetThreadDpiAwarenessContext(-4);
        try
        {
            var windows = new List<DesktopWindow>();
            var className = new StringBuilder(128);
            Native.EnumWindows((hwnd, _) => ReadWindow(hwnd, windows, className), 0);
            return windows.ToArray();
        }
        finally
        {
            if (previousDpi != 0) Native.SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    private bool ReadWindow(nint hwnd, List<DesktopWindow> windows, StringBuilder className)
    {
        if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
        int style = (int)Native.GetWindowLongPtr(hwnd, -20);
        if ((style & (Native.ExLayered | Native.ExTransparent)) == (Native.ExLayered | Native.ExTransparent))
            return true;

        Native.GetClassName(hwnd, className, className.Capacity);
        string name = className.ToString();
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or
            "Windows.UI.Core.CoreWindow" or "Shell_InputSwitchTopLevelWindow")
            return true;

        if (Native.DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return true;
        if (Native.DwmGetWindowAttribute(hwnd, 9, out Native.Rect rect, 16) != 0 &&
            !Native.GetWindowRect(hwnd, out rect))
            return true;
        Rectangle bounds = rect.ToDrawing();
        if (bounds.Width <= 0 || bounds.Height <= 0) return true;

        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        bool collects = pid != processId && bounds.Width >= 64 && bounds.Height >= 42 &&
            (style & (Native.ExToolWindow | Native.ExNoActivate)) == 0 && name != "#32768";
        windows.Add(new DesktopWindow(hwnd, bounds, collects));
        return true;
    }

    public void Dispose()
    {
        foreach (nint hook in hooks) Native.UnhookWinEvent(hook);
    }
}
