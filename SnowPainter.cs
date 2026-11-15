using System.Drawing.Drawing2D;

namespace DesktopSnow;

internal sealed class SnowPainter : IDisposable
{
    private readonly SolidBrush snow = new(Color.FromArgb(241, 234, 245, 255));
    private readonly SolidBrush ice = new(Color.FromArgb(206, 188, 215, 235));
    private readonly SolidBrush white = new(Color.FromArgb(251, 246, 251, 255));
    private readonly SolidBrush coal = new(Color.FromArgb(36, 57, 67));
    private readonly SolidBrush scarf = new(Color.FromArgb(213, 56, 65));
    private readonly SolidBrush carrot = new(Color.FromArgb(240, 129, 55));
    private readonly SolidBrush plowBody = new(Color.FromArgb(225, 128, 53));
    private readonly SolidBrush plowLight = new(Color.FromArgb(255, 227, 157));
    private readonly SolidBrush hub = new(Color.FromArgb(121, 150, 157));
    private readonly Pen placement = new(Color.FromArgb(215, 227, 204, 156), 1.4f) { DashStyle = DashStyle.Dash };
    private readonly GraphicsPath contour = new();

    public void Bank(Graphics g, SnowBank bank, Rectangle view)
    {
        if (bank.Peak < .18f && bank.Stacks.Count == 0 || bank.Bounds.Right <= view.Left ||
            bank.Bounds.Left >= view.Right || bank.Y - 160 * bank.Scale >= view.Bottom || bank.Y + 1 < view.Top)
            return;
        GraphicsState? state = null;
        if (bank.HasOcclusion)
        {
            state = g.Save();
            g.SetClip(bank.Clip, CombineMode.Intersect);
        }
        int cursor = Math.Max(0, bank.Bin(view.Left) - 1);
        int end = Math.Min(bank.Height.Length, bank.Bin(view.Right) + 2);
        while (cursor < end)
        {
            while (cursor < end && (!bank.Visible[cursor] || bank.Height[cursor] < .14f)) cursor++;
            int start = cursor;
            while (cursor < end && bank.Visible[cursor] && bank.Height[cursor] >= .14f) cursor++;
            if (start < cursor) DrawRidge(g, bank, start, cursor - 1);
        }
        foreach (SnowStack stack in bank.Stacks)
        {
            float x = bank.X + stack.Offset;
            if (bank.HasVisibleEdge(x)) Stack(g, x, bank.Y - bank.At(x), stack.Radii);
        }
        if (state is not null) g.Restore(state);
    }

    private void DrawRidge(Graphics g, SnowBank bank, int first, int last)
    {
        contour.Reset();
        float left = bank.X + first * SnowBank.Step;
        float right = Math.Min(bank.Bounds.Right, bank.X + (last + 1) * SnowBank.Step);
        float floor = bank.Y + .78f;
        PointF previous = new(bank.Center(first), bank.Y - bank.Height[first]);
        contour.AddLine(left, floor, left, previous.Y + Math.Min(1, bank.Height[first] * .35f));
        contour.AddLine(new PointF(left, previous.Y + Math.Min(1, bank.Height[first] * .35f)), previous);
        // Adjacent bins are settled to a gentle slope, so one curve can span two.
        for (int i = Math.Min(first + 2, last); i > first && i <= last; i = i == last ? last + 1 : Math.Min(i + 2, last))
        {
            PointF next = new(bank.Center(i), bank.Y - bank.Height[i]);
            float middle = (next.X + previous.X) * .5f;
            contour.AddBezier(previous, new PointF(middle, previous.Y), new PointF(middle, next.Y), next);
            previous = next;
        }
        contour.AddLine(previous, new PointF(right, previous.Y + Math.Min(1, bank.Height[last] * .35f)));
        contour.AddLine(right, previous.Y + Math.Min(1, bank.Height[last] * .35f), right, floor);
        contour.CloseFigure();
        g.FillPath(snow, contour);
    }

    private void Stack(Graphics g, float x, float bottom, List<float> radii)
    {
        bool finished = radii.Count == 3;
        for (int i = 0; i < radii.Count; i++)
        {
            float radius = radii[i];
            float y = bottom - radius;
            Ball(g, x, y, radius);
            if (finished && i < 2)
            {
                Circle(g, coal, x + radius * .09f, y - radius * .18f, Math.Max(.6f, radius * .11f));
                Circle(g, coal, x + radius * .09f, y + radius * .25f, Math.Max(.6f, radius * .1f));
            }
            if (finished && i == 2)
            {
                Circle(g, coal, x - radius * .3f, y - radius * .23f, radius * .14f);
                Circle(g, coal, x + radius * .3f, y - radius * .23f, radius * .14f);
                g.FillEllipse(carrot, x - radius * .02f, y, radius * .47f, radius * .22f);
                g.FillEllipse(scarf, x - radius * .86f, bottom - radius * .44f, radius * 1.72f, radius * .46f);
                g.FillRectangle(scarf, x + radius * .45f, bottom - radius * .19f, radius * .35f, radius * .9f);
            }
            bottom -= radius * 1.88f;
        }
    }

    public void Ball(Graphics g, float x, float y, float radius)
    {
        Circle(g, ice, x, y, radius);
        Circle(g, white, x - radius * .09f, y - radius * .12f, radius * .87f);
    }

    public void Placement(Graphics g, float x, float y, float radius) =>
        g.DrawEllipse(placement, x - radius, y - radius, radius * 2, radius * 2);

    public void Plow(Graphics g, float x, float y, float scale, int direction)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(x, y);
        g.ScaleTransform(scale * direction, scale);
        g.FillRectangle(coal, -25, -13, 43, 8);
        g.FillRectangle(plowBody, -25, -22, 44, 12);
        g.FillRectangle(plowBody, -22, -35, 24, 18);
        g.FillRectangle(coal, -18, -31, 16, 10);
        g.FillRectangle(hub, -9, -30, 6, 8);
        g.FillEllipse(plowLight, -17, -39, 8, 5);
        g.FillEllipse(plowLight, 13, -20, 7, 5);
        Circle(g, coal, -16, -7, 7);
        Circle(g, coal, 8, -7, 7);
        Circle(g, hub, -16, -7, 2.8f);
        Circle(g, hub, 8, -7, 2.8f);
        contour.Reset();
        contour.AddLine(18, -12, 25, -12);
        contour.AddBezier(25, -12, 28, -12, 31, -14, 33, -18);
        contour.AddBezier(33, -18, 34, -8, 31, -4, 26, -.5f);
        contour.AddLine(26, -.5f, 18, -.5f);
        contour.CloseFigure();
        g.FillPath(hub, contour);
        g.FillRectangle(ice, 18, -2.5f, 11, 2);
        g.Restore(state);
    }

    private static void Circle(Graphics canvas, Brush brush, float x, float y, float radius) =>
        canvas.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);

    public void Dispose()
    {
        foreach (SolidBrush brush in new[] { snow, ice, white, coal, scarf, carrot, plowBody, plowLight, hub })
            brush.Dispose();
        contour.Dispose();
        placement.Dispose();
    }
}

internal sealed partial class SnowScene
{
    public void Draw(Overlay overlay, SnowPainter painter)
    {
        Graphics canvas = overlay.Canvas;
        Rectangle view = overlay.Bounds;
        overlay.BeginFrame();
        bool hasVectorContent = false;
        for (int i = 0; i < particleCount; i++)
        {
            ref readonly SnowParticle particle = ref particles[i];
            if (MarkSprite(particle.X, particle.Y, particle.Radius, particle.DrawAboveWindows,
                    particle.Kind == SnowParticleKind.Flake) &&
                particle.Kind == SnowParticleKind.Ball)
                hasVectorContent = true;
        }
        foreach (SnowBank bank in banks)
        {
            if (bank.Peak >= .14f && MarkBounds(bank.Bounds.Left, bank.Y - bank.Peak - 2, bank.Bounds.Right, bank.Y + 3))
                hasVectorContent = true;
            foreach (SnowStack stack in bank.Stacks)
            {
                float x = bank.X + stack.Offset;
                float width = stack.Radii.Max() + 2;
                if (bank.HasVisibleEdge(x) && MarkBounds(x - width, StackBase(bank, stack) - stack.Height - 2, x + width, bank.Y + 3))
                    hasVectorContent = true;
            }
        }
        SnowScreen? screen = null;
        foreach (SnowScreen candidate in screens)
        {
            if (candidate.Bounds != view || candidate.PlowX is not float x) continue;
            screen = candidate;
            float margin = 36 * candidate.Scale;
            float y = candidate.Ground.Y;
            hasVectorContent |= MarkBounds(x - margin, y - 41 * candidate.Scale, x + margin, y + 3);
        }
        (SnowBank Bank, SnowStack Stack)? placementTarget = null;
        if (heldBall is RolledBall held && held.Mass > 0)
        {
            hasVectorContent |= MarkSprite(held.X, held.Y, held.Radius + 1);
            placementTarget = StackTarget(new Point((int)held.X, (int)held.Y), held.Radius);
            if (placementTarget is { } target)
                hasVectorContent |= MarkSprite(target.Bank.X + target.Stack.Offset,
                    StackBase(target.Bank, target.Stack) - target.Stack.Height - held.Radius * .88f, held.Radius + 1);
        }
        overlay.ClearChangedTiles();
        for (int i = 0; i < particleCount; i++)
        {
            SnowParticle particle = particles[i];
            if (particle.Kind != SnowParticleKind.Ball && SpriteInView(particle.X, particle.Y, particle.Radius + 1))
                overlay.DrawFlake(particle);
        }
        if (!hasVectorContent) return;

        canvas.ResetTransform();
        canvas.ResetClip();
        canvas.SmoothingMode = SmoothingMode.AntiAlias;
        canvas.TranslateTransform(-view.X, -view.Y);
        canvas.SetClip(overlay.SnowClip, CombineMode.Replace);
        for (int i = 0; i < particleCount; i++)
            if (particles[i].Kind == SnowParticleKind.Ball &&
                SpriteInView(particles[i].X, particles[i].Y, particles[i].Radius + 1))
                painter.Ball(canvas, particles[i].X, particles[i].Y, particles[i].Radius);
        for (int i = banks.Count - 1; i >= 0; i--) painter.Bank(canvas, banks[i], view);
        if (screen?.PlowX is float position)
            painter.Plow(canvas, position, screen.Ground.Y, screen.Scale, plowDirection);
        if (heldBall is RolledBall ball && ball.Mass > 0)
        {
            if (placementTarget is { } match)
            {
                float radius = ball.Radius;
                painter.Placement(canvas, match.Bank.X + match.Stack.Offset,
                    StackBase(match.Bank, match.Stack) - match.Stack.Height - radius * .88f, radius);
            }
            painter.Ball(canvas, ball.X, ball.Y, ball.Radius);
        }
        overlay.Flush();

        bool SpriteInView(float x, float y, float margin) => x + margin >= view.Left && x - margin < view.Right &&
            y + margin >= view.Top && y - margin < view.Bottom;

        bool MarkSprite(float x, float y, float radius, bool aboveWindows = false, bool fallingSnow = false)
        {
            float margin = radius + 2;
            return MarkBounds(x - margin, y - margin, x + margin, y + margin, aboveWindows, fallingSnow);
        }

        bool MarkBounds(float left, float top, float right, float bottom, bool aboveWindows = false,
            bool fallingSnow = false)
        {
            if (left >= view.Right || right <= view.Left || top >= view.Bottom || bottom <= view.Top) return false;
            return overlay.IncludeDrawBounds(Rectangle.FromLTRB((int)MathF.Floor(left), (int)MathF.Floor(top),
                (int)MathF.Ceiling(right), (int)MathF.Ceiling(bottom)), aboveWindows, fallingSnow);
        }
    }
}
