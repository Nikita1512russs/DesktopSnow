namespace DesktopSnow;

internal sealed class ScreenCoverage
{
    private readonly List<Rectangle> remaining = [];

    public bool WindowsCoverWorkArea(IReadOnlyList<DesktopWindow> windows, Rectangle workArea, float scale)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) return false;
        // Snap Layout seams can be only a few pixels wide.
        FindExposedAreas(windows, workArea, remaining, Math.Max(2, (int)Math.Ceiling(5 * scale)));
        return remaining.Count == 0;
    }

    public static void FindExposedAreas(IReadOnlyList<DesktopWindow> windows, Rectangle workArea,
        List<Rectangle> exposed, int tolerance = 0, bool includeWindowShadows = false)
    {
        exposed.Clear();
        if (workArea.Width <= 0 || workArea.Height <= 0) return;
        exposed.Add(workArea);

        foreach (DesktopWindow window in windows)
        {
            if (!window.CollectsSnow || !window.Bounds.IntersectsWith(workArea)) continue;
            Rectangle cover = window.Bounds;
            if (includeWindowShadows)
                cover = Rectangle.FromLTRB(cover.Left, cover.Top, cover.Right, workArea.Bottom);
            cover.Inflate(tolerance, tolerance);
            for (int i = exposed.Count - 1; i >= 0; i--)
            {
                Rectangle area = exposed[i];
                Rectangle overlap = Rectangle.Intersect(area, cover);
                if (overlap.Width <= 0 || overlap.Height <= 0) continue;

                exposed[i] = exposed[^1];
                exposed.RemoveAt(exposed.Count - 1);
                Add(area.Left, area.Top, area.Right, overlap.Top);
                Add(area.Left, overlap.Bottom, area.Right, area.Bottom);
                Add(area.Left, overlap.Top, overlap.Left, overlap.Bottom);
                Add(overlap.Right, overlap.Top, area.Right, overlap.Bottom);
            }
            if (exposed.Count == 0) return;
        }

        void Add(int left, int top, int right, int bottom)
        {
            if (right > left && bottom > top)
                exposed.Add(Rectangle.FromLTRB(left, top, right, bottom));
        }
    }
}
