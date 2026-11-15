using System.Runtime.InteropServices;
using System.Text;

namespace DesktopSnow;

internal static class Native
{
    public const int WsPopup = unchecked((int)0x80000000);
    public const int ExTopmost = 0x8, ExTransparent = 0x20, ExToolWindow = 0x80,
        ExLayered = 0x80000, ExNoActivate = 0x08000000;
    public const int EventForeground = 3, EventMoveSizeEnd = 0x000B,
        EventMinimizeStart = 0x0016, EventMinimizeEnd = 0x0017,
        EventDestroy = 0x8001, EventShow = 0x8002, EventHide = 0x8003,
        EventReorder = 0x8004, EventLocation = 0x800B, EventCloaked = 0x8017, EventUncloaked = 0x8018;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point(int x, int y)
    {
        public int X = x, Y = y;
        public readonly System.Drawing.Point ToDrawing() => new(X, Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToDrawing() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant, Colors;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Blend(byte alpha)
    {
        public byte Operation = 0, Flags = 0, Alpha = alpha, Format = 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct LayerUpdate
    {
        public uint Size;
        public nint Screen;
        public Point* Destination;
        public Point* Dimensions;
        public nint Source;
        public Point* Origin;
        public uint ColorKey;
        public Blend* Blend;
        public uint Flags;
        public Rect* Dirty;
    }

    public delegate bool EnumProc(nint hwnd, nint data);
    public delegate void WinEventProc(nint hook, uint kind, nint hwnd, int objectId, int childId, uint thread, uint time);
    public delegate nint MouseProc(int code, nint message, nint data);
    public delegate void TimerProc(uint timer, uint message, nuint user, nuint first, nuint second);

    [DllImport("user32")] public static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32")] public static extern bool IsZoomed(nint hwnd);
    [DllImport("user32")] public static extern nint GetForegroundWindow();
    [DllImport("user32")] public static extern nint GetDesktopWindow();
    [DllImport("user32")] public static extern nint GetShellWindow();
    [DllImport("user32")] public static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint hwnd, StringBuilder name, int length);
    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32")] public static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore")] public static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32")] public static extern nint SetWinEventHook(uint from, uint to, nint module,
        WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32")] public static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32")] public static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32", EntryPoint = "PostMessageW")] public static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern nint FindWindow(string? className, string title);
    [DllImport("user32")] public static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32")] public static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32", SetLastError = true)] public static extern bool UpdateLayeredWindow(nint window, nint screen,
        ref Point destination, ref Point size, nint dc, ref Point origin, uint color, ref Blend blend, uint flags);
    [DllImport("user32", SetLastError = true)] public static extern bool UpdateLayeredWindowIndirect(nint window, in LayerUpdate update);
    [DllImport("user32", SetLastError = true)] public static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32", SetLastError = true)] public static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("user32", SetLastError = true)] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint SetWindowsHookEx(int id, MouseProc callback, nint module, uint thread);
    [DllImport("user32")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);
    [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect rect, int size);
    [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("gdi32")] public static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32")] public static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage,
        out nint bits, nint section, uint offset);
    [DllImport("gdi32")] public static extern nint SelectObject(nint dc, nint handle);
    [DllImport("gdi32")] public static extern bool DeleteObject(nint handle);
    [DllImport("gdi32")] public static extern bool DeleteDC(nint dc);
    [DllImport("winmm")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm")] public static extern uint timeEndPeriod(uint period);
    [DllImport("winmm")] public static extern uint timeSetEvent(uint delay, uint resolution, TimerProc callback, nuint user, uint mode);
    [DllImport("winmm")] public static extern uint timeKillEvent(uint timer);
}
