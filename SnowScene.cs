namespace DesktopSnow;

internal sealed partial class SnowScene(Settings settings, Func<float, float, bool>? isSnowVisible = null,
    Func<float, float, bool>? isFallingSnowVisible = null) : IDisposable
{
    private const int FlakeLimit = 5600;
    private const int PowderLimit = 540;
    private const int LooseBallLimit = 96;
    private const int ParticleLimit = FlakeLimit + PowderLimit + LooseBallLimit;
    private readonly Random random = new();
    private readonly SnowParticle[] particles = new SnowParticle[ParticleLimit];
    private readonly Dictionary<nint, SnowBank> windowBanks = [];
    private readonly HashSet<nint> liveWindows = [];
    private readonly List<nint> removed = [];
    private readonly List<SnowBank> banks = [];
    private readonly List<SnowBank> collisionBanks = [];
    private readonly List<SnowScreen> screens = [];
    private int particleCount, flakeCount, powderCount, looseBallCount;
    private float tallestBank;
    private float time, restTime, plowTime;
    private int plowDirection = 1;
    private Point? lastCursor;
    public IReadOnlyList<SnowScreen> Screens => screens;
    public bool HasMovingWindows
    {
        get
        {
            foreach (SnowBank bank in windowBanks.Values)
                if (bank.SpeedX != 0 || bank.SpeedY != 0) return true;
            return false;
        }
    }

    public void SetScreens(IEnumerable<(Rectangle Bounds, Rectangle WorkArea, float Scale)> monitors)
    {
        foreach (SnowScreen screen in screens) screen.Ground.Dispose();
        screens.Clear();
        banks.Clear();
        collisionBanks.Clear();
        foreach (var monitor in monitors) screens.Add(new SnowScreen(monitor.Bounds, monitor.WorkArea, monitor.Scale));
        ResetPlow();
    }

    public void ResetPlow()
    {
        plowTime = 0;
        foreach (SnowScreen screen in screens) screen.PlowX = null;
    }

    public void ResetCursor() => lastCursor = null;

    private bool CanInteractAt(float x, float y) => isSnowVisible?.Invoke(x, y) ?? true;

    private bool IsCovered(float x, float y) => !CanInteractAt(x, y) && OnScreen(x, y, 0);

    private bool IsInSnowShadow(float x, float y) => isFallingSnowVisible is null
        ? IsCovered(x, y)
        : !isFallingSnowVisible(x, y) && OnScreen(x, y, 0);

    public void ClearSnowmen()
    {
        foreach (SnowBank bank in windowBanks.Values) bank.Stacks.Clear();
        foreach (SnowScreen screen in screens) screen.Ground.Stacks.Clear();
        CancelBall();
        for (int i = particleCount - 1; i >= 0; i--)
            if (particles[i].Kind == SnowParticleKind.Ball) RemoveParticle(i);
    }

    public void UpdateWindows(IReadOnlyList<DesktopWindow> windows, HashSet<nint> stopped, HashSet<nint> gone)
    {
        banks.Clear();
        liveWindows.Clear();
        if (settings.Windows)
        {
            for (int z = 0; z < windows.Count; z++)
            {
                DesktopWindow window = windows[z];
                if (!window.CollectsSnow || !IsScreenEdge(window.Bounds)) continue;
                liveWindows.Add(window.Handle);
                float scale = ScaleAt(window.Bounds.X + window.Bounds.Width / 2, window.Bounds.Y);
                if (windowBanks.TryGetValue(window.Handle, out SnowBank? bank) && gone.Contains(window.Handle))
                {
                    LoseBank(bank, 0, 0);
                    bank.Dispose();
                    windowBanks.Remove(window.Handle);
                    bank = null;
                }
                if (bank is null)
                {
                    bank = new SnowBank(window.Bounds, scale, spillAtSides: true);
                    windowBanks.Add(window.Handle, bank);
                }
                else MoveBank(bank, window.Bounds, scale, stopped.Contains(window.Handle));
                bank.SetOcclusion(windows, z);
                banks.Add(bank);
            }
        }

        removed.Clear();
        foreach ((nint handle, SnowBank bank) in windowBanks)
        {
            if (liveWindows.Contains(handle)) continue;
            if (settings.Windows) LoseBank(bank, 0, 0);
            bank.Dispose();
            removed.Add(handle);
        }
        foreach (nint handle in removed) windowBanks.Remove(handle);

        foreach (SnowScreen screen in screens)
        {
            if (!settings.Ground)
            {
                screen.Ground.Clear();
                continue;
            }
            screen.Ground.SetOcclusion(windows, windows.Count, desktopWindowsOnly: true);
            banks.Add(screen.Ground);
        }
        collisionBanks.Clear();
        collisionBanks.AddRange(banks);
        // Keep the front-to-back order for edges with identical Y coordinates.
        for (int i = 1; i < collisionBanks.Count; i++)
        {
            SnowBank current = collisionBanks[i];
            int j = i;
            while (j > 0 && collisionBanks[j - 1].Y > current.Y)
            {
                collisionBanks[j] = collisionBanks[j - 1];
                j--;
            }
            collisionBanks[j] = current;
        }
        tallestBank = 0;
        foreach (SnowBank bank in banks) tallestBank = Math.Max(tallestBank, bank.Maximum);
    }

    private bool IsScreenEdge(Rectangle bounds)
    {
        foreach (SnowScreen screen in screens)
            if (bounds.Left < screen.Bounds.Right && bounds.Right > screen.Bounds.Left &&
                bounds.Top >= screen.Bounds.Top && bounds.Top < screen.Bounds.Bottom) return true;
        return false;
    }

    private void MoveBank(SnowBank bank, Rectangle bounds, float scale, bool stopped)
    {
        Rectangle old = bank.Bounds;
        if (old == bounds)
        {
            if (stopped || time - bank.LastMotion > .10f) StopBank(bank, scale);
            return;
        }
        int dx = bounds.X - old.X;
        int dy = bounds.Y - old.Y;
        float duration = Math.Clamp(time - bank.LastMotion, .028f, .15f);
        float vx = dx / duration;
        float vy = dy / duration;
        bool resizing = old.Width != bounds.Width;
        int offset = resizing && Math.Abs(old.Right - bounds.Right) < Math.Abs(dx) ? dx : 0;
        float speed = MathF.Sqrt(vx * vx + vy * vy);
        if (resizing || speed > 650 * scale)
        {
            if (time - bank.LastShed > .13f)
            {
                Shed(bank, resizing ? .26f : .20f, vx * .45f, vy * .36f);
                bank.LastShed = time;
            }
            BreakStacks(bank, vx * .38f, vy * .32f);
        }
        bank.Resize(bounds, scale, offset);
        for (int i = bank.Stacks.Count - 1; i >= 0; i--)
            if (bank.Stacks[i].Offset < 0 || bank.Stacks[i].Offset >= bounds.Width) bank.Stacks.RemoveAt(i);
        if (dx != 0 || dy != 0)
        {
            bank.LastMotion = time;
            bank.SpeedX = vx;
            bank.SpeedY = vy;
        }
        if (stopped) StopBank(bank, scale);
    }

    private void StopBank(SnowBank bank, float scale)
    {
        if (Math.Abs(bank.SpeedX) + Math.Abs(bank.SpeedY) > 260 * scale)
        {
            Shed(bank, .26f, bank.SpeedX * .45f, bank.SpeedY * .22f);
            if (Math.Abs(bank.SpeedX) + Math.Abs(bank.SpeedY) > 425 * scale)
                BreakStacks(bank, bank.SpeedX * .4f, bank.SpeedY * .24f);
        }
        bank.SpeedX = bank.SpeedY = 0;
    }

    private void LoseBank(SnowBank bank, float vx, float vy)
    {
        Shed(bank, 1, vx, vy, closing: true);
        BreakStacks(bank, vx, vy);
    }

    private void Shed(SnowBank bank, float fraction, float vx, float vy, bool closing = false)
    {
        if (bank.Peak < .3f) return;
        int groups = closing ? 48 + settings.Intensity / 2 : 55;
        int stride = Math.Max(1, (bank.Height.Length + groups - 1) / groups);
        float mass = 0;
        for (int i = 0; i < bank.Height.Length; i++)
        {
            if (!closing && !bank.Visible[i]) continue;
            float height = bank.Height[i];
            float lost = height * fraction;
            bank.Height[i] -= lost;
            mass += lost;
            if (mass < .42f || (i % stride != 0 && i + 1 != bank.Height.Length)) continue;
            float x = bank.Center(i);
            if (closing)
            {
                float massPerGrain = (16 - settings.Intensity * .08f) * bank.Scale;
                int grains = Math.Clamp((int)MathF.Ceiling(mass / massPerGrain), 1, 5);
                for (int j = 0; j < grains; j++)
                    Powder(x + RandomBetween(-SnowBank.Step, SnowBank.Step), bank.Y + RandomBetween(1, 7),
                        vx, vy + 35, mass * .72f / grains, bank.Scale);
            }
            else if (settings.Snowmen && mass > 11 * bank.Scale && random.NextDouble() < .25)
                ThrowBall(x, bank.Y + 2, Math.Clamp(MathF.Sqrt(mass) * .9f, 2, 9 * bank.Scale), vx, vy, mass * .72f);
            else
                Powder(x, bank.Y + 2, vx, vy, mass * .72f, bank.Scale);
            mass = 0;
        }
    }

    public void Advance(float dt, Point? cursor, bool molding)
    {
        time += dt;
        if (settings.Cursor && !molding && cursor is Point next && lastCursor is Point previous && previous != next)
            SweepCursor(previous, next);
        lastCursor = molding ? null : cursor;
        if (molding && cursor is Point position) MoveBall(position);

        foreach (SnowScreen screen in screens) SpawnFlakes(screen, dt);
        SimulateParticles(dt);
        AdvancePlows(dt);
        restTime += dt;
        if (restTime < .05f) return;
        foreach (SnowBank bank in banks)
        {
            bank.Settle(restTime, settings.Melt);
            SpillAtSide(bank, left: true);
            SpillAtSide(bank, left: false);
        }
        restTime = 0;
    }

    private void SpillAtSide(SnowBank bank, bool left)
    {
        float mass = bank.TakeSpill(left);
        if (mass <= 0) return;
        float direction = left ? -1 : 1;
        float x = (left ? bank.Bounds.Left : bank.Bounds.Right) + direction * bank.Scale;
        float y = bank.Y + 1;
        if (!OnScreen(x, y, 0) || !bank.IsVisibleAt(x, y)) return;
        int grains = Math.Clamp((int)(mass / (5 * bank.Scale)), 1, 6);
        for (int i = 0; i < grains; i++)
            Powder(x, y, direction * 24 * bank.Scale, 43 * bank.Scale, mass / grains, bank.Scale);
    }

    private void SpawnFlakes(SnowScreen screen, float dt)
    {
        float level = settings.Intensity / 100f;
        if (level <= 0) return;
        screen.SpawnCredit += dt * (5 * level + 124 * MathF.Pow(level, 1.46f)) *
            Math.Clamp(screen.Bounds.Width / (1400f * screen.Scale), .35f, 2);
        while (screen.SpawnCredit >= 1 && flakeCount < FlakeLimit && particleCount < ParticleLimit)
        {
            screen.SpawnCredit -= 1;
            float size = RandomBetween(.8f, 2.25f + level * 2.2f) * screen.Scale;
            particles[particleCount++] = new SnowParticle
            {
                X = RandomBetween(screen.Bounds.Left, screen.Bounds.Right),
                Y = screen.Bounds.Top - size,
                Radius = size, Vy = RandomBetween(37, 97) * screen.Scale + size * 11,
                Vx = RandomBetween(-15, 15) * screen.Scale, Mass = size * 1.9f,
                Phase = RandomBetween(0, 6.28f), Tone = (byte)random.Next(8)
            };
            flakeCount++;
        }
        screen.SpawnCredit = Math.Min(2, screen.SpawnCredit);
    }

    private void SimulateParticles(float dt)
    {
        for (int i = particleCount - 1; i >= 0; i--)
        {
            ref SnowParticle particle = ref particles[i];
            particle.Age += dt;
            float flutter = 0;
            if (particle.Kind == SnowParticleKind.Flake)
                flutter = MathF.Sin(time * 1.6f + particle.Phase) * 19;
            else particle.Vy = Math.Min(420, particle.Vy + dt * 236);
            float x = particle.X + (particle.Vx + flutter) * dt;
            float y = particle.Y + particle.Vy * dt;
            if (particle.Kind == SnowParticleKind.Flake && !particle.PassedBehindWindow)
                particle.PassedBehindWindow = IsInSnowShadow(particle.X, particle.Y);
            if (particle.Vy > 0 && Land(ref particle, x, y) ||
                particle.Age > 90 || !OnScreen(x, y, 85))
            {
                RemoveParticle(i);
                continue;
            }
            particle.X = x;
            particle.Y = y;
        }
    }

    private bool Land(ref SnowParticle particle, float x, float y)
    {
        float start = particle.Y + particle.Radius;
        float end = y + particle.Radius;
        bool fallingSnow = particle.Kind == SnowParticleKind.Flake;
        float best = 2;
        float landingX = x;
        float landingY = y;
        SnowBank? target = null;
        float left = Math.Min(x, particle.X), right = Math.Max(x, particle.X);
        float highestLanding = end + tallestBank;
        int first = 0, last = collisionBanks.Count;
        while (first < last)
        {
            int middle = first + (last - first) / 2;
            if (collisionBanks[middle].Y < start) first = middle + 1;
            else last = middle;
        }
        for (int i = first; i < collisionBanks.Count; i++)
        {
            SnowBank bank = collisionBanks[i];
            if (bank.Y > highestLanding) break;
            if (bank.Y - bank.Peak > end || right < bank.X || left >= bank.Bounds.Right)
                continue;

            float fraction = Math.Clamp((bank.Y - start) / (end - start), 0, 1);
            float contactX = particle.X + (x - particle.X) * fraction;
            if (contactX < bank.X || contactX >= bank.Bounds.Right) continue;
            if (fallingSnow && bank.IsWindow)
            {
                // The desktop and each window keep their own accumulation behind foreground windows.
                if (end >= bank.Y && bank.SupportsSnow(contactX) && OnScreen(contactX, bank.Y, 0))
                    bank.Deposit(contactX, particle.Mass, includeCovered: true);
                continue;
            }
            float top = bank.Y - bank.At(contactX);
            fraction = (top - start) / (end - start);
            if (fraction < -.24f || fraction > 1 || fraction >= best) continue;
            contactX = particle.X + (x - particle.X) * Math.Max(0, fraction);
            if (!bank.SupportsSnow(contactX) || !bank.HasVisibleEdge(contactX) || !bank.IsVisibleAt(contactX, top) ||
                !OnScreen(contactX, bank.Y, 0)) continue;
            if (!fallingSnow && IsCovered(contactX, top - .25f)) continue;
            best = fraction;
            landingX = contactX;
            landingY = top;
            target = bank;
        }
        if (target is null) return false;
        if (particle.Mass > 0 && (!fallingSnow ||
            !particle.PassedBehindWindow && !IsInSnowShadow(landingX, landingY - .25f)))
            target.Deposit(landingX, particle.Mass);
        return true;
    }

    private bool OnScreen(float x, float y, int margin)
    {
        foreach (SnowScreen screen in screens)
        {
            Rectangle area = screen.Bounds;
            if (x >= area.Left - margin && x < area.Right + margin &&
                y >= area.Top - margin && y < area.Bottom + margin) return true;
        }
        return false;
    }

    private float ScaleAt(float x, float y)
    {
        foreach (SnowScreen screen in screens)
            if (screen.Bounds.Contains((int)x, (int)y)) return screen.Scale;
        return screens.Count == 0 ? 1 : screens[0].Scale;
    }

    private float RandomBetween(float min, float max) => min + random.NextSingle() * (max - min);

    private void Powder(float x, float y, float vx, float vy, float mass, float scale, bool drawAboveWindows = false)
    {
        if (powderCount >= PowderLimit || particleCount == ParticleLimit) return;
        powderCount++;
        particles[particleCount++] = new SnowParticle
        {
            X = x, Y = y, Vx = Math.Clamp(vx, -320, 320) + RandomBetween(-48, 48) * scale,
            Vy = Math.Clamp(vy, -180, 150) + RandomBetween(-38, 43) * scale,
            Radius = RandomBetween(.72f, 2.65f) * scale, Mass = mass,
            Tone = (byte)random.Next(8), Kind = SnowParticleKind.Powder, DrawAboveWindows = drawAboveWindows
        };
    }

    private void ThrowBall(float x, float y, float radius, float vx, float vy, float mass)
    {
        if (looseBallCount >= LooseBallLimit || particleCount == ParticleLimit) return;
        looseBallCount++;
        particles[particleCount++] = new SnowParticle
        {
            X = x, Y = y, Radius = radius, Vx = Math.Clamp(vx, -290, 290) + RandomBetween(-51, 51),
            Vy = Math.Clamp(vy, -110, 150) + RandomBetween(-46, 11),
            Mass = mass, Kind = SnowParticleKind.Ball
        };
    }

    private void RemoveParticle(int index)
    {
        switch (particles[index].Kind)
        {
            case SnowParticleKind.Flake: flakeCount--; break;
            case SnowParticleKind.Powder: powderCount--; break;
            case SnowParticleKind.Ball: looseBallCount--; break;
        }
        particles[index] = particles[--particleCount];
    }

    public void Dispose()
    {
        foreach (SnowBank bank in windowBanks.Values) bank.Dispose();
        foreach (SnowScreen screen in screens) screen.Ground.Dispose();
    }
}
