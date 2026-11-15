using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopSnow;

internal sealed class SnowHost : NativeWindow, IDisposable
{
    public const string WindowName = "Desktop Snow — управление";
    public const int OpenSettingsMessage = 0x8001;
    private const int FrameMessage = 0x8002;
    private const uint PeriodicSynchronousTimer = 0x101;
    private readonly Settings settings;
    private readonly WindowTracker tracker;
    private readonly SnowScene scene;
    private readonly SnowPainter painter = new();
    private readonly FullScreenWindow fullScreen = new();
    private readonly ScreenCoverage screenCoverage = new();
    private readonly List<Overlay> overlays = [];
    private readonly HashSet<nint> handles = [];
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private readonly Native.MouseProc mouse;
    private readonly Native.TimerProc frameTimer;
    private nint mouseHook;
    private uint nativeTimer;
    private int frameQueued;
    private bool molding, capturedStroke, displaysDirty;
    private uint requestedResolution;
    private double lastFrame, lastSnapshot;
    private double nextFullScreenCheck;
    private double nextFrame, frameCost, nextRateCheck;
    private int baseFramePeriod, framePeriod;
    private bool captureSupported = true;
    private (Rectangle Bounds, Rectangle WorkArea, float Scale)[] displayLayout = [];
    private DesktopWindow[] previousWindows = [];
    private bool refreshSurfaces = true;
    private bool checkWindowMotion;
    private bool allDisplaysCovered;
    private bool coverageDirty = true;
    private bool hasWindowSnapshot;

    public bool IsMolding => molding;
    public bool CaptureSupported => captureSupported;
    public event Action? StatusChanged;
    public event Action? OpenSettings;

    public SnowHost(Settings settings)
    {
        this.settings = settings;
        scene = new SnowScene(settings, IsSnowVisible, IsFallingSnowVisible);
        mouse = OnMouse;
        frameTimer = QueueFrame;
        CreateHandle(new CreateParams { Caption = WindowName });
        tracker = new WindowTracker(handles);
        timer.Tick += Tick;
        RebuildDisplays();
        SetEnabled();
    }

    private void RebuildDisplays()
    {
        displaysDirty = false;
        var layout = new List<(Rectangle Bounds, Rectangle WorkArea, float Scale)>();
        foreach (Screen screen in Screen.AllScreens)
        {
            Rectangle bounds = screen.Bounds;
            var point = new Native.Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            nint monitor = Native.MonitorFromPoint(point, 2);
            float scale = Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi / 96f : 1;
            layout.Add((bounds, screen.WorkingArea, scale));
        }
        if (displayLayout.Length == overlays.Count && layout.SequenceEqual(displayLayout)) return;
        displayLayout = layout.ToArray();
        foreach (Overlay overlay in overlays) overlay.Dispose();
        overlays.Clear();
        handles.Clear();
        var monitors = new List<(Rectangle, Rectangle, float)>();
        foreach (var screen in layout)
        {
            try
            {
                Rectangle bounds = screen.Bounds;
                var overlay = new Overlay(bounds);
                overlays.Add(overlay);
                handles.Add(overlay.Handle);
                monitors.Add(screen);
            }
            catch (Exception error)
            {
                Settings.Log($"Ошибка создания слоя: {error}");
            }
        }
        scene.SetScreens(monitors);
        coverageDirty = true;
        nextFullScreenCheck = 0;
        RefreshSurfaces();
        SetCaptureExclusion();
        if (nativeTimer != 0 || timer.Enabled) UpdateFrameRate();
    }

    public void SetCaptureExclusion()
    {
        captureSupported = overlays.Count > 0;
        foreach (Overlay overlay in overlays)
            captureSupported &= overlay.SetCaptureExclusion(settings.HideFromCapture);
        StatusChanged?.Invoke();
    }

    public void SetEnabled()
    {
        if (settings.Enabled)
        {
            if (displaysDirty) RebuildDisplays();
            lastFrame = watch.Elapsed.TotalSeconds;
            nextFullScreenCheck = 0;
            hasWindowSnapshot = false;
            CheckScreenCoverage(lastFrame);
            RefreshSurfaces();
            scene.ResetPlow();
            UpdateFrameRate();
        }
        else
        {
            EndMolding();
            StopTimer();
            foreach (Overlay overlay in overlays) overlay.SetVisible(false);
        }
        scene.ResetCursor();
    }

    public void UpdateFrameRate()
    {
        StopTimer();
        if (!settings.Enabled) return;
        long pixels = 0;
        foreach (Overlay overlay in overlays) pixels += (long)overlay.Bounds.Width * overlay.Bounds.Height;
        baseFramePeriod = settings.ReduceLoad ? (pixels > 4_500_000 ? 40 : 33) : 16;
        frameCost = 0;
        nextRateCheck = lastFrame + 1;
        framePeriod = baseFramePeriod;
        StartTimer();
    }

    private void StartTimer()
    {
        StopTimer();
        lastFrame = watch.Elapsed.TotalSeconds;
        if (allDisplaysCovered)
        {
            timer.Interval = 180;
            timer.Start();
            return;
        }
        uint resolution = settings.ReduceLoad ? 4u : 1u;
        if (Native.timeBeginPeriod(resolution) == 0) requestedResolution = resolution;
        nextFrame = lastFrame;
        // A synchronous kill keeps the callback from using a destroyed handle.
        nativeTimer = Native.timeSetEvent((uint)framePeriod, resolution, frameTimer, (nuint)Handle, PeriodicSynchronousTimer);
        if (nativeTimer == 0)
        {
            timer.Interval = framePeriod;
            timer.Start();
        }
    }

    private void QueueFrame(uint id, uint message, nuint window, nuint first, nuint second)
    {
        if (Interlocked.Exchange(ref frameQueued, 1) != 0) return;
        if (!Native.PostMessage((nint)window, FrameMessage, (nint)id, 0)) Volatile.Write(ref frameQueued, 0);
    }

    private void StopTimer()
    {
        timer.Stop();
        if (nativeTimer != 0) Native.timeKillEvent(nativeTimer);
        nativeTimer = 0;
        if (requestedResolution != 0) Native.timeEndPeriod(requestedResolution);
        requestedResolution = 0;
    }

    public void RefreshSurfaces()
    {
        refreshSurfaces = true;
        tracker.Dirty = true;
    }
    public void ResetPlow() => scene.ResetPlow();
    public void ClearSnowmen()
    {
        EndMolding();
        scene.ClearSnowmen();
    }

    public bool BeginMolding()
    {
        if (molding) return true;
        if (capturedStroke || allDisplaysCovered || !settings.Enabled || !settings.Snowmen ||
            !Native.RegisterHotKey(Handle, 1, 0x4000, 0x1B))
            return false;
        mouseHook = Native.SetWindowsHookEx(14, mouse, Native.GetModuleHandle(null), 0);
        if (mouseHook == 0)
        {
            Settings.Log($"WH_MOUSE_LL: Win32 {Marshal.GetLastWin32Error()}");
            Native.UnregisterHotKey(Handle, 1);
            return false;
        }
        molding = true;
        scene.ResetCursor();
        StatusChanged?.Invoke();
        return true;
    }

    public void EndMolding()
    {
        if (!molding) return;
        scene.CancelBall();
        molding = false;
        Native.UnregisterHotKey(Handle, 1);
        if (!capturedStroke) ReleaseMouseHook();
        StatusChanged?.Invoke();
    }

    private void ReleaseMouseHook()
    {
        if (mouseHook == 0) return;
        Native.UnhookWindowsHookEx(mouseHook);
        mouseHook = 0;
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            if (message == 0x0201 && molding)
            {
                capturedStroke = scene.BeginBall(Marshal.PtrToStructure<Native.Point>(data).ToDrawing());
                if (capturedStroke) return 1;
            }
            if (message == 0x0202 && capturedStroke)
            {
                capturedStroke = false;
                if (molding) scene.ReleaseBall(Marshal.PtrToStructure<Native.Point>(data).ToDrawing());
                else ReleaseMouseHook();
                return 1;
            }
        }
        return Native.CallNextHookEx(mouseHook, code, message, data);
    }

    private void Tick(object? sender, EventArgs args)
    {
        double now = watch.Elapsed.TotalSeconds;
        if (displaysDirty) RebuildDisplays();
        UpdateWindows(now);
        if (coverageDirty || now >= nextFullScreenCheck)
        {
            bool wasPaused = allDisplaysCovered;
            CheckScreenCoverage(now);
            if (allDisplaysCovered != wasPaused)
            {
                StartTimer();
                return;
            }
        }
        if (!hasWindowSnapshot) return;
        if (allDisplaysCovered)
        {
            float elapsedCovered = (float)Math.Clamp(now - lastFrame, 0, .24);
            lastFrame = now;
            while (elapsedCovered > 0)
            {
                float step = Math.Min(elapsedCovered, .06f);
                scene.Advance(step, null, false);
                elapsedCovered -= step;
            }
            return;
        }
        if (now + .002 < nextFrame) return;
        double elapsed = now - lastFrame;
        float dt = (float)Math.Clamp(elapsed, .001, .08);
        lastFrame = now;
        nextFrame = now + (framePeriod - 3) / 1000d;

        scene.Advance(dt, ReadSnowCursor(), molding);
        if (settings.Opacity == 0 && !molding)
        {
            foreach (Overlay overlay in overlays)
                if (Native.IsWindowVisible(overlay.Handle)) overlay.SetVisible(false);
            return;
        }
        byte alpha = (byte)Math.Round(255 * settings.Opacity / 100d);
        foreach (Overlay overlay in overlays)
        {
            if (overlay.Covered) continue;
            scene.Draw(overlay, painter);
            overlay.Upload(alpha);
            if (!Native.IsWindowVisible(overlay.Handle)) overlay.SetVisible(true);
        }
        if (settings.ReduceLoad) AdjustFrameRate(now, elapsed);
    }

    private void UpdateWindows(double now)
    {
        if (tracker.TakeSnapshot() is DesktopWindow[] windows)
        {
            hasWindowSnapshot = true;
            bool changed = !windows.AsSpan().SequenceEqual(previousWindows);
            coverageDirty |= changed;
            previousWindows = windows;
            if (changed || refreshSurfaces || checkWindowMotion ||
                tracker.StoppedWindows.Count > 0 || tracker.RemovedWindows.Count > 0)
            {
                scene.UpdateWindows(windows, tracker.StoppedWindows, tracker.RemovedWindows);
                checkWindowMotion = scene.HasMovingWindows;
                refreshSurfaces = false;
            }
            tracker.StoppedWindows.Clear();
            tracker.RemovedWindows.Clear();
        }
        double snapshotAge = now - lastSnapshot;
        if (!tracker.SnapshotPending &&
            ((tracker.Dirty || checkWindowMotion) && snapshotAge >= .06 || snapshotAge > 3))
        {
            tracker.RequestSnapshot();
            lastSnapshot = now;
        }
    }

    private Point? ReadSnowCursor()
    {
        if (!Native.GetCursorPos(out Native.Point cursor)) return null;
        return IsSnowVisible(cursor.X, cursor.Y) ? cursor.ToDrawing() : null;
    }

    private bool IsSnowVisible(float x, float y)
    {
        foreach (Overlay overlay in overlays)
            if (overlay.ContainsExposedPoint(x, y)) return true;
        return false;
    }

    private bool IsFallingSnowVisible(float x, float y)
    {
        foreach (Overlay overlay in overlays)
            if (overlay.ContainsFallingSnowPoint(x, y)) return true;
        return false;
    }

    private void CheckScreenCoverage(double now)
    {
        nextFullScreenCheck = now + .14;
        var foreground = fullScreen.ReadForeground();
        allDisplaysCovered = overlays.Count > 0;
        bool coverageChanged = false;
        foreach (Overlay overlay in overlays)
        {
            var display = Array.Find(displayLayout, display => display.Bounds == overlay.Bounds);
            if (coverageDirty)
            {
                overlay.UpdateExposedAreas(previousWindows, display.WorkArea);
                overlay.CoveredByWindows = screenCoverage.WindowsCoverWorkArea(previousWindows, display.WorkArea, display.Scale);
            }
            bool covered = overlay.CoveredByWindows ||
                FullScreenWindow.CoversDisplay(foreground, overlay.Bounds, display.WorkArea, display.Scale);
            allDisplaysCovered &= covered;
            if (covered == overlay.Covered) continue;
            overlay.Covered = covered;
            coverageChanged = true;
            if (covered) overlay.SetVisible(false);
        }
        coverageDirty = false;
        if (coverageChanged)
        {
            scene.ResetCursor();
            EndMolding();
        }
    }

    private void AdjustFrameRate(double started, double elapsed)
    {
        double duration = watch.Elapsed.TotalSeconds - started;
        // Decay transient stalls gradually and leave time for other desktop applications.
        frameCost = Math.Max(duration, frameCost * .97);
        if (started < nextRateCheck) return;
        int target = baseFramePeriod;
        if (frameCost * 1000 > baseFramePeriod * .45 || elapsed > framePeriod * .002)
            target = Math.Clamp((int)Math.Ceiling(frameCost * 2400), baseFramePeriod + 8, 50);
        nextRateCheck = started + (target > framePeriod ? 1 : 3);
        if (target == framePeriod || Math.Abs(target - framePeriod) < 4) return;
        framePeriod = target;
        StartTimer();
    }

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case 0x007E: // WM_DISPLAYCHANGE
            case 0x001A when message.WParam == 0x002F: // SPI_SETWORKAREA
            case 0x02E0: // WM_DPICHANGED
                displaysDirty = true;
                break;
            case 0x0312 when message.WParam == 1:
                EndMolding();
                break;
            case OpenSettingsMessage:
                OpenSettings?.Invoke();
                break;
            case FrameMessage:
                try
                {
                    if (settings.Enabled && nativeTimer != 0 && message.WParam == (nint)nativeTimer)
                        Tick(this, EventArgs.Empty);
                }
                finally { Volatile.Write(ref frameQueued, 0); }
                break;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        StopTimer();
        timer.Dispose();
        EndMolding();
        ReleaseMouseHook();
        tracker.Dispose();
        foreach (Overlay overlay in overlays) overlay.Dispose();
        scene.Dispose();
        painter.Dispose();
        DestroyHandle();
    }
}
