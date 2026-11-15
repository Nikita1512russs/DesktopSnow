namespace DesktopSnow;

internal sealed partial class SnowScene
{
    private sealed class RolledBall(float x, float y, float mass, float scale)
    {
        public float X = x, Y = y, Mass = mass;
        public float Scale { get; } = scale;
        public float Radius => MathF.Sqrt(Mass * SnowBank.Step / MathF.PI);
        public float RollRemainder;
        public SnowBank? OriginalBank;
        public SnowStack? OriginalStack;
    }

    private RolledBall? heldBall;

    public bool BeginBall(Point position)
    {
        if (!CanInteractAt(position.X, position.Y)) return false;
        foreach (SnowBank surface in banks)
        {
            for (int index = surface.Stacks.Count - 1; index >= 0; index--)
            {
                SnowStack stack = surface.Stacks[index];
                if (stack.Radii.Count == 3) continue;
                float x = surface.X + stack.Offset;
                float radius = stack.Radii[^1];
                float y = StackBase(surface, stack) - stack.Height + radius * .88f;
                float dx = position.X - x, dy = position.Y - y;
                float grabRadius = radius + 5 * surface.Scale;
                if (dx * dx + dy * dy > grabRadius * grabRadius ||
                    !surface.HasVisibleEdge(x) || !surface.Clip.IsVisible(position)) continue;
                heldBall = new RolledBall(position.X, position.Y, MassForRadius(radius), surface.Scale)
                {
                    OriginalBank = surface, OriginalStack = stack
                };
                stack.Radii.RemoveAt(stack.Radii.Count - 1);
                if (stack.Radii.Count == 0) surface.Stacks.RemoveAt(index);
                return true;
            }
        }
        SnowBank? bank = BankAt(position, 14, requireSnow: true);
        if (bank is null) return false;
        heldBall = new RolledBall(position.X, position.Y, 0, bank.Scale);
        return true;
    }

    private static float MassForRadius(float radius) => MathF.PI * radius * radius / SnowBank.Step;

    private void MoveBall(Point position)
    {
        if (heldBall is not RolledBall ball) return;
        float dx = position.X - ball.X, dy = position.Y - ball.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance == 0) return;
        float spacing = Math.Max(4 * ball.Scale, distance / 180);
        for (float along = Math.Max(0, spacing - ball.RollRemainder); along <= distance; along += spacing)
        {
            float fraction = along / distance;
            RollAt(ball, new Point((int)MathF.Round(ball.X + dx * fraction), (int)MathF.Round(ball.Y + dy * fraction)));
        }
        ball.RollRemainder = (ball.RollRemainder + distance) % spacing;
        ball.X = position.X;
        ball.Y = position.Y;
    }

    private void RollAt(RolledBall ball, Point position)
    {
        float capacity = MassForRadius(18 * ball.Scale) - ball.Mass;
        if (capacity <= .001f) return;
        float radius = ball.Radius;
        SnowBank? bank = BankAt(position, 9 + radius / ball.Scale * .4f, requireSnow: true);
        if (bank is null) return;
        float width = Math.Max(5 * bank.Scale, radius * .75f);
        int first = Math.Max(0, bank.Bin(position.X - width));
        int last = Math.Min(bank.Height.Length - 1, bank.Bin(position.X + width));
        for (int i = first; i <= last && capacity > .001f; i++)
        {
            float height = bank.Height[i];
            float x = bank.Center(i);
            if (height <= 0 || !bank.Visible[i] ||
                !bank.IsVisibleAt(x, bank.Y - height) || !CanInteractAt(x, bank.Y - height)) continue;
            float weight = 1 - Math.Min(1, Math.Abs(x - position.X) / (width + SnowBank.Step));
            float taken = Math.Min(capacity, Math.Min(height, (2 + 3 * weight) * bank.Scale));
            bank.Height[i] -= taken;
            ball.Mass += taken;
            capacity -= taken;
        }
    }

    private SnowBank? BankAt(Point position, float tolerance, bool requireSnow)
    {
        if (!CanInteractAt(position.X, position.Y)) return null;
        foreach (SnowBank bank in banks)
        {
            if (!bank.HasVisibleEdge(position.X) || !bank.IsVisibleAt(position.X, position.Y)) continue;
            float height = bank.At(position.X);
            if ((!requireSnow || height > .35f * bank.Scale) &&
                position.Y <= bank.Y + 3 * bank.Scale &&
                position.Y >= bank.Y - height - tolerance * bank.Scale)
                return bank;
        }
        return null;
    }

    private (SnowBank Bank, SnowStack Stack)? StackTarget(Point position, float radius)
    {
        if (!CanInteractAt(position.X, position.Y)) return null;
        (SnowBank, SnowStack)? target = null;
        float nearest = float.PositiveInfinity;
        foreach (SnowBank bank in banks)
        {
            foreach (SnowStack stack in bank.Stacks)
            {
                float x = bank.X + stack.Offset;
                float top = StackBase(bank, stack) - stack.Height;
                if (stack.Radii.Count >= 3 || !bank.HasVisibleEdge(x) ||
                    !bank.IsVisibleAt(position.X, position.Y)) continue;
                float dx = Math.Abs(position.X - x);
                float dy = Math.Abs(position.Y - (top - radius * .65f));
                if (dx > Math.Max(18 * bank.Scale, stack.Radii[^1] + radius * .6f) ||
                    dy > radius + 11 * bank.Scale || position.Y > top + stack.Radii[^1] * .6f) continue;
                float distance = dx * dx + dy * dy;
                if (distance >= nearest) continue;
                nearest = distance;
                target = (bank, stack);
            }
        }
        return target;
    }

    public void ReleaseBall(Point position)
    {
        if (heldBall is null) return;
        MoveBall(position);
        RolledBall ball = heldBall;
        heldBall = null;
        float radius = ball.Radius;
        if (radius < 2.6f * ball.Scale)
        {
            DropCollectedSnow(ball);
            return;
        }
        if (StackTarget(position, radius) is { } match)
        {
            match.Stack.Radii.Add(radius);
            return;
        }

        SnowBank? target = BankAt(position, 24, requireSnow: false);
        if (target is not null && target.Stacks.Count < 16)
        {
            var stack = new SnowStack(position.X - target.X);
            stack.Radii.Add(radius);
            target.Stacks.Add(stack);
        }
        else DropCollectedSnow(ball);
    }

    public void CancelBall()
    {
        if (heldBall is RolledBall ball)
        {
            if (ball.OriginalBank is SnowBank bank && ball.OriginalStack is SnowStack stack &&
                banks.Contains(bank) && (stack.Radii.Count == 0 || bank.Stacks.Contains(stack)))
            {
                if (stack.Radii.Count == 0) bank.Stacks.Add(stack);
                stack.Radii.Add(ball.Radius);
            }
            else DropCollectedSnow(ball);
        }
        heldBall = null;
    }

    private void DropCollectedSnow(RolledBall ball)
    {
        if (ball.Mass <= 0) return;
        ThrowBall(ball.X, ball.Y, ball.Radius, 0, 0, ball.Mass);
    }

    private static float StackBase(SnowBank bank, SnowStack stack) => bank.Y - bank.At(bank.X + stack.Offset);

    private void BreakStacks(SnowBank bank, float vx, float vy)
    {
        foreach (SnowStack stack in bank.Stacks)
            if (bank.HasVisibleEdge(bank.X + stack.Offset)) BreakStack(bank, stack, vx, vy);
        bank.Stacks.Clear();
    }

    private void BreakStack(SnowBank bank, SnowStack stack, float vx, float vy)
    {
        float x = bank.X + stack.Offset;
        float bottom = StackBase(bank, stack);
        foreach (float radius in stack.Radii)
        {
            ThrowBall(x, bottom - radius, radius, vx, vy, MassForRadius(radius));
            for (int j = 0; j < 5; j++) Powder(x, bottom - radius, vx, vy, 0, bank.Scale);
            bottom -= radius * 1.88f;
        }
    }

    private void SweepCursor(Point from, Point to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        int sprays = 28;
        foreach (SnowBank bank in banks)
        {
            float radius = 13 * bank.Scale;
            if (Math.Min(from.Y, to.Y) > bank.Y + radius ||
                Math.Max(from.Y, to.Y) < bank.Y - bank.Peak - 118 * bank.Scale) continue;

            for (int i = bank.Stacks.Count - 1; i >= 0; i--)
            {
                SnowStack stack = bank.Stacks[i];
                if (stack.Radii.Count < 3) continue;
                float x = bank.X + stack.Offset;
                float bottom = StackBase(bank, stack);
                if (!bank.HasVisibleEdge(x) || !SegmentHits(from, to, x, bottom - stack.Height, bottom,
                    stack.Radii[0] + radius, out PointF hit) ||
                    !bank.IsVisibleAt(hit.X, hit.Y) || !CanInteractAt(hit.X, hit.Y)) continue;
                BreakStack(bank, stack, dx * 7, dy * 5);
                bank.Stacks.RemoveAt(i);
            }
            if (bank.Peak < .25f) continue;

            int first = Math.Max(0, bank.Bin(Math.Min(from.X, to.X) - radius));
            int last = Math.Min(bank.Height.Length - 1, bank.Bin(Math.Max(from.X, to.X) + radius));
            float moved = 0;
            float sprayMass = 0;
            int sprayStride = Math.Max(1, (last - first + 18) / 18);
            int clearedFirst = -1, clearedLast = -1;
            for (int i = first; i <= last; i++)
            {
                float x = bank.Center(i);
                float height = bank.Height[i];
                if (!bank.Visible[i] || height < .15f ||
                    !SegmentHits(from, to, x, bank.Y - height, bank.Y, radius, out PointF hit) ||
                    !bank.IsVisibleAt(hit.X, hit.Y) || !CanInteractAt(hit.X, hit.Y)) continue;
                float cleared = height * .83f;
                bank.Height[i] -= cleared;
                moved += cleared;
                if (clearedFirst < 0) clearedFirst = i;
                clearedLast = i;
                sprayMass += cleared * .3f;
                if (sprays > 0 && (sprayStride == 1 || (i - first) % sprayStride == 0))
                {
                    Powder(x, bank.Y - height, dx * 8, -57 + dy * 2.3f, sprayMass, bank.Scale,
                        drawAboveWindows: true);
                    sprayMass = 0;
                    sprays--;
                }
            }
            if (moved <= 0) continue;
            bank.Deposit(bank.Center(clearedFirst) - 15 * bank.Scale, Math.Min(10 * bank.Scale, moved * .31f));
            bank.Deposit(bank.Center(clearedLast) + 15 * bank.Scale, Math.Min(10 * bank.Scale, moved * .31f));
        }
    }

    private static bool SegmentHits(Point from, Point to, float x, float top, float bottom, float radius, out PointF hit)
    {
        float begin = 0, end = 1;
        float dx = to.X - from.X, dy = to.Y - from.Y;
        if (!ClipSegment(from.X, dx, x - radius, x + radius, ref begin, ref end) ||
            !ClipSegment(from.Y, dy, top - radius, bottom + radius, ref begin, ref end))
        {
            hit = default;
            return false;
        }
        float along = dx == 0 ? (begin + end) * .5f : Math.Clamp((x - from.X) / dx, begin, end);
        hit = new PointF(x, Math.Clamp(from.Y + dy * along, top, bottom - .01f));
        return true;
    }

    private static bool ClipSegment(float origin, float delta, float min, float max, ref float begin, ref float end)
    {
        if (delta == 0) return origin >= min && origin <= max;
        float a = (min - origin) / delta, b = (max - origin) / delta;
        begin = Math.Max(begin, Math.Min(a, b));
        end = Math.Min(end, Math.Max(a, b));
        return begin <= end;
    }

    private void AdvancePlows(float dt)
    {
        if (!settings.Plow || !settings.Ground) return;
        plowTime += dt;
        bool driving = false;
        foreach (SnowScreen screen in screens)
            if (screen.PlowX.HasValue) driving = true;
        if (!driving && plowTime >= settings.PlowMinutes * 60)
        {
            plowTime = 0;
            plowDirection = -plowDirection;
            foreach (SnowScreen screen in screens)
                screen.PlowX = plowDirection > 0 ? screen.Bounds.Left - 68 * screen.Scale :
                    screen.Bounds.Right + 68 * screen.Scale;
        }
        foreach (SnowScreen screen in screens)
        {
            if (screen.PlowX is not float x) continue;
            SnowBank bank = screen.Ground;
            float next = x + plowDirection * 143 * screen.Scale * dt;
            float blade = 28 * screen.Scale * plowDirection;
            int first = Math.Clamp(bank.Bin(Math.Min(x + blade, next + blade)), 0, bank.Height.Length);
            int last = Math.Clamp(bank.Bin(Math.Max(x + blade, next + blade)), -1, bank.Height.Length - 1);
            for (int i = first; i <= last; i++)
            {
                float height = bank.Height[i];
                if (height < .025f) continue;
                bank.Height[i] = 0;
                if (!bank.Visible[i]) continue;
                int grains = Math.Clamp((int)(height / (2.6f * screen.Scale)), 1, 10);
                for (int j = 0; j < grains; j++)
                    Powder(bank.Center(i), bank.Y - RandomBetween(1, Math.Max(2, height)),
                        plowDirection * RandomBetween(145, 225), RandomBetween(-138, -52),
                        0, screen.Scale);
            }
            // Clear under the chassis too, where loose snow can slump behind the blade.
            int chassis = Math.Clamp(bank.Bin(next - blade), 0, bank.Height.Length - 1);
            if (plowDirection > 0 && chassis < first)
                Array.Clear(bank.Height, chassis, first - chassis);
            else if (plowDirection < 0 && chassis > last)
                Array.Clear(bank.Height, last + 1, chassis - last);
            for (int i = bank.Stacks.Count - 1; i >= 0; i--)
            {
                SnowStack stack = bank.Stacks[i];
                float stackX = bank.X + stack.Offset;
                if (stackX < Math.Min(x + blade, next + blade) - 6 * screen.Scale ||
                    stackX > Math.Max(x + blade, next + blade) + 6 * screen.Scale) continue;
                if (bank.HasVisibleEdge(stackX)) BreakStack(bank, stack, plowDirection * 80, -35);
                bank.Stacks.RemoveAt(i);
            }
            bool finished = plowDirection > 0 ? next > screen.Bounds.Right + 68 * screen.Scale :
                next < screen.Bounds.Left - 68 * screen.Scale;
            screen.PlowX = finished ? null : next;
        }
    }
}
