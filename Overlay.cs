using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DesktopSnow;

internal sealed class Overlay : NativeWindow, IDisposable
{
    private const int TileSize = 64;
    private readonly nint dc, dib, oldBitmap;
    private readonly nint pixels;
    private readonly Bitmap bitmap;
    private readonly List<Rectangle> exposedAreas = [];
    private readonly List<Rectangle> fallingSnowAreas = [];
    private Rectangle snowArea;
    private bool reportedPresentError;
    private Rectangle lastDrawn;
    private Rectangle drawn;
    private Rectangle dirty;
    private byte previousAlpha;
    private bool initialized, partialUpdates = true;
    private readonly int columns, rows;
    private bool[] previousTiles, tiles;
    public Rectangle Bounds { get; }
    public bool CoveredByWindows { get; set; }
    public bool Covered { get; set; }
    public Graphics Canvas { get; }
    public Region SnowClip { get; } = new();

    public Overlay(Rectangle bounds)
    {
        Bounds = bounds;
        snowArea = bounds;
        exposedAreas.Add(bounds);
        fallingSnowAreas.Add(bounds);
        columns = (bounds.Width + TileSize - 1) / TileSize;
        rows = (bounds.Height + TileSize - 1) / TileSize;
        previousTiles = new bool[columns * rows];
        tiles = new bool[previousTiles.Length];
        dc = Native.CreateCompatibleDC(0);
        if (dc == 0)
        {
            SnowClip.Dispose();
            throw new Win32Exception("CreateCompatibleDC");
        }
        Bitmap? createdBitmap = null;
        Graphics? createdCanvas = null;
        try
        {
            var info = new Native.BitmapInfo
            {
                Size = 40, Width = bounds.Width, Height = -bounds.Height, Planes = 1, BitCount = 32
            };
            dib = Native.CreateDIBSection(dc, ref info, 0, out nint bits, 0, 0);
            if (dib == 0) throw new Win32Exception("CreateDIBSection");
            pixels = bits;
            oldBitmap = Native.SelectObject(dc, dib);
            if (oldBitmap == 0 || oldBitmap == -1) throw new Win32Exception("SelectObject");
            createdBitmap = new Bitmap(bounds.Width, bounds.Height, bounds.Width * 4, PixelFormat.Format32bppPArgb, bits);
            createdCanvas = Graphics.FromImage(createdBitmap);
            CreateHandle(new CreateParams
            {
                Caption = "Desktop Snow — слой",
                Style = Native.WsPopup,
                ExStyle = Native.ExLayered | Native.ExTransparent | Native.ExNoActivate |
                    Native.ExToolWindow | Native.ExTopmost,
                X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height
            });
            bitmap = createdBitmap;
            Canvas = createdCanvas;
        }
        catch
        {
            DestroyHandle();
            createdCanvas?.Dispose();
            createdBitmap?.Dispose();
            SnowClip.Dispose();
            ReleaseBuffer();
            throw;
        }
    }

    public void SetVisible(bool show) => Native.ShowWindow(Handle, show ? 4 : 0);

    public void UpdateExposedAreas(IReadOnlyList<DesktopWindow> windows, Rectangle workArea)
    {
        snowArea = Rectangle.Intersect(Bounds, workArea);
        ScreenCoverage.FindExposedAreas(windows, snowArea, exposedAreas);
        ScreenCoverage.FindExposedAreas(windows, snowArea, fallingSnowAreas, includeWindowShadows: true);
        SnowClip.MakeEmpty();
        foreach (Rectangle area in exposedAreas) SnowClip.Union(area);
    }

    public bool ContainsExposedPoint(float x, float y) => ContainsPoint(exposedAreas, x, y);
    public bool ContainsFallingSnowPoint(float x, float y) => ContainsPoint(fallingSnowAreas, x, y);

    private bool ContainsPoint(List<Rectangle> areas, float x, float y)
    {
        if (Covered) return false;
        foreach (Rectangle area in areas)
            if (x >= area.Left && x < area.Right && y >= area.Top && y < area.Bottom) return true;
        return false;
    }

    public unsafe void Clear() => new Span<uint>((void*)pixels, Bounds.Width * Bounds.Height).Clear();

    public void BeginFrame()
    {
        drawn = Rectangle.Empty;
        Array.Clear(tiles);
    }

    public unsafe void ClearChangedTiles()
    {
        dirty = lastDrawn.IsEmpty ? drawn : drawn.IsEmpty ? lastDrawn : Rectangle.Union(lastDrawn, drawn);
        if (!initialized || dirty == Bounds && DenseCoverage())
        {
            Clear();
            return;
        }
        for (int row = 0; row < rows; row++)
        {
            int column = 0;
            while (column < columns)
            {
                int index = row * columns + column;
                if (!previousTiles[index] && !tiles[index])
                {
                    column++;
                    continue;
                }
                int start = column++;
                while (column < columns && (previousTiles[row * columns + column] || tiles[row * columns + column]))
                    column++;
                int x = start * TileSize;
                int length = Math.Min(Bounds.Width, column * TileSize) - x;
                int bottom = Math.Min(Bounds.Height, (row + 1) * TileSize);
                for (int y = row * TileSize; y < bottom; y++)
                    new Span<uint>((uint*)pixels + y * Bounds.Width + x, length).Clear();
            }
        }
    }

    private bool DenseCoverage()
    {
        int marked = 0;
        for (int i = 0; i < tiles.Length; i++)
            if (tiles[i] || previousTiles[i]) marked++;
        return marked * 4 >= tiles.Length * 3;
    }

    public bool IncludeDrawBounds(Rectangle bounds, bool aboveWindows = false, bool fallingSnow = false)
    {
        bool visible = false;
        foreach (Rectangle area in DrawAreas(aboveWindows, fallingSnow))
        {
            Rectangle clipped = Rectangle.Intersect(area, bounds);
            if (clipped.Width <= 0 || clipped.Height <= 0) continue;
            MarkTiles(clipped);
            visible = true;
        }
        return visible;
    }

    private ReadOnlySpan<Rectangle> DrawAreas(bool aboveWindows, bool fallingSnow) => aboveWindows
        ? MemoryMarshal.CreateReadOnlySpan(ref snowArea, 1)
        : CollectionsMarshal.AsSpan(fallingSnow ? fallingSnowAreas : exposedAreas);

    private void MarkTiles(Rectangle clipped)
    {
        drawn = drawn.IsEmpty ? clipped : Rectangle.Union(drawn, clipped);
        int left = (clipped.Left - Bounds.Left) / TileSize;
        int right = (clipped.Right - 1 - Bounds.Left) / TileSize;
        int top = (clipped.Top - Bounds.Top) / TileSize;
        int bottom = (clipped.Bottom - 1 - Bounds.Top) / TileSize;
        for (int y = top; y <= bottom; y++)
            Array.Fill(tiles, true, y * columns + left, right - left + 1);
    }

    public unsafe void DrawFlake(SnowParticle flake)
    {
        float x = flake.X - Bounds.X;
        float y = flake.Y - Bounds.Y;
        float outer = flake.Radius + .5f;
        float inner = Math.Max(0, flake.Radius - .5f);
        float outerSquared = outer * outer, innerSquared = inner * inner;
        Rectangle sprite = Rectangle.FromLTRB((int)MathF.Floor(flake.X - outer),
            (int)MathF.Floor(flake.Y - outer), (int)MathF.Floor(flake.X + outer) + 1,
            (int)MathF.Floor(flake.Y + outer) + 1);
        uint opacity = (uint)(138 + 16 * flake.Tone);
        uint red = (uint)(217 + 5 * flake.Tone), green = (uint)(235 + 2 * flake.Tone);
        foreach (Rectangle area in DrawAreas(flake.DrawAboveWindows, flake.Kind == SnowParticleKind.Flake))
        {
            Rectangle clipped = Rectangle.Intersect(area, sprite);
            if (clipped.Width <= 0 || clipped.Height <= 0) continue;
            int left = clipped.Left - Bounds.X, right = clipped.Right - Bounds.X;
            int bottom = clipped.Bottom - Bounds.Y;
            for (int row = clipped.Top - Bounds.Y; row < bottom; row++)
            {
                uint* line = (uint*)pixels + row * Bounds.Width;
                float dy = row + .5f - y;
                for (int column = left; column < right; column++)
                {
                    float dx = column + .5f - x;
                    float distance = dx * dx + dy * dy;
                    if (distance >= outerSquared) continue;
                    uint alpha = distance <= innerSquared ? opacity :
                        (uint)(opacity * (outerSquared - distance) / (outerSquared - innerSquared));
                    if (alpha == 0) continue;
                    uint pr = red * alpha / 255, pg = green * alpha / 255;
                    uint pb = alpha;
                    uint previous = line[column];
                    if (previous != 0)
                    {
                        uint remaining = 255 - alpha;
                        pr += ((previous >> 16) & 255) * remaining / 255;
                        pg += ((previous >> 8) & 255) * remaining / 255;
                        pb += (previous & 255) * remaining / 255;
                        alpha += (previous >> 24) * remaining / 255;
                    }
                    line[column] = (alpha << 24) | (pr << 16) | (pg << 8) | pb;
                }
            }
        }
    }

    public bool SetCaptureExclusion(bool exclude)
    {
        uint expected = exclude ? 0x11u : 0u;
        if (Native.SetWindowDisplayAffinity(Handle, expected) &&
            Native.GetWindowDisplayAffinity(Handle, out uint actual) && actual == expected)
            return true;
        Settings.Log($"Display affinity {expected:X}, monitor {Bounds}: Win32 {Marshal.GetLastWin32Error()}");
        return false;
    }

    public void Present(byte alpha)
    {
        Array.Fill(tiles, true);
        drawn = dirty = Bounds;
        Flush();
        Upload(alpha);
    }

    public void Flush() => Canvas.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);

    public unsafe void Upload(byte alpha)
    {
        var location = new Native.Point(Bounds.X, Bounds.Y);
        var size = new Native.Point(Bounds.Width, Bounds.Height);
        var origin = new Native.Point(0, 0);
        var blend = new Native.Blend(alpha);
        if (initialized && dirty.IsEmpty && previousAlpha == alpha) return;
        bool success = false;
        if (initialized && previousAlpha == alpha && partialUpdates && dirty != Bounds)
        {
            var local = new Native.Rect { Left = dirty.Left - Bounds.Left, Top = dirty.Top - Bounds.Top,
                Right = dirty.Right - Bounds.Left, Bottom = dirty.Bottom - Bounds.Top };
            var update = new Native.LayerUpdate
            {
                Size = (uint)sizeof(Native.LayerUpdate), Destination = &location, Dimensions = &size,
                Source = dc, Origin = &origin, Blend = &blend, Flags = 2, Dirty = &local
            };
            success = Native.UpdateLayeredWindowIndirect(Handle, in update);
            if (!success) partialUpdates = false;
        }
        if (!success)
            success = Native.UpdateLayeredWindow(Handle, 0, ref location, ref size, dc, ref origin, 0, ref blend, 2);
        if (!success && !reportedPresentError)
            Settings.Log($"UpdateLayeredWindow: Win32 {Marshal.GetLastWin32Error()}");
        reportedPresentError = !success;
        if (success)
        {
            initialized = true;
            previousAlpha = alpha;
            lastDrawn = drawn;
            (tiles, previousTiles) = (previousTiles, tiles);
        }
        else initialized = false;
    }

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case 0x0084: message.Result = -1; return; // HTTRANSPARENT
            case 0x0021: message.Result = 3; return; // MA_NOACTIVATE
            default: base.WndProc(ref message); return;
        }
    }

    private void ReleaseBuffer()
    {
        if (oldBitmap != 0 && oldBitmap != -1) Native.SelectObject(dc, oldBitmap);
        if (dib != 0) Native.DeleteObject(dib);
        Native.DeleteDC(dc);
    }

    public void Dispose()
    {
        DestroyHandle();
        Canvas.Dispose();
        SnowClip.Dispose();
        bitmap.Dispose();
        ReleaseBuffer();
    }
}
