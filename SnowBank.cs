namespace DesktopSnow;

internal sealed class SnowBank : IDisposable
{
    public const int Step = 4;
    public Rectangle Bounds { get; private set; }
    public float Scale { get; private set; }
    public float[] Height { get; private set; } = [];
    public bool[] Visible { get; private set; } = [];
    public Region Clip { get; } = new();
    private List<Rectangle> occluders = [];
    private List<Rectangle> nextOccluders = [];
    private bool occlusionDirty;
    private float leftSpill, rightSpill;
    private readonly bool spillAtSides;
    private int edgeBins;
    public List<SnowStack> Stacks { get; } = [];
    public float Peak { get; private set; }
    public float LastMotion, SpeedX, SpeedY, LastShed;
    public int X => Bounds.X;
    public int Y => Bounds.Y;
    public float Maximum => 31 * Scale;
    public bool HasOcclusion => occluders.Count != 0;
    public bool IsWindow => spillAtSides;

    public SnowBank(Rectangle bounds, float scale, bool spillAtSides = false)
    {
        this.spillAtSides = spillAtSides;
        Resize(bounds, scale, 0);
    }

    public int Bin(float x) => (int)MathF.Floor((x - X) / Step);
    public float Center(int bin) => Math.Min(X + (bin + .5f) * Step, Bounds.Right - .5f);
    public bool HasVisibleEdge(float x)
    {
        int bin = Bin(x);
        return x < Bounds.Right && bin >= 0 && bin < Height.Length && Visible[bin];
    }

    public bool SupportsSnow(float x)
    {
        float margin = spillAtSides ? Math.Max(Step, 5 * Scale) : 0;
        return x >= X + margin && x < Bounds.Right - margin;
    }

    public float At(float x)
    {
        float index = Math.Clamp((x - X) / Step - .5f, 0, Height.Length - 1);
        int left = (int)index;
        return Height[left] + (Height[Math.Min(left + 1, Height.Length - 1)] - Height[left]) * (index - left);
    }

    public bool IsVisibleAt(float x, float y)
    {
        foreach (Rectangle cover in occluders)
            if (x >= cover.Left && x < cover.Right && y >= cover.Top && y < cover.Bottom) return false;
        return true;
    }

    public void Resize(Rectangle bounds, float scale, int offset)
    {
        occlusionDirty |= bounds != Bounds;
        int length = Math.Max(1, (bounds.Width + Step - 1) / Step);
        if (length != Height.Length || offset != 0)
        {
            float[] resized = new float[length];
            for (int i = 0; i < resized.Length && Height.Length > 0; i++)
            {
                float oldX = X + (i + .5f) * Step + offset;
                if (oldX >= X && oldX < Bounds.Right) resized[i] = At(oldX);
            }
            Height = resized;
            Visible = new bool[length];
            Array.Fill(Visible, true);
        }
        foreach (SnowStack stack in Stacks) stack.Offset -= offset;
        Bounds = bounds;
        Scale = scale;
        edgeBins = spillAtSides ? (int)MathF.Ceiling((Math.Max(Step, 5 * scale) + Maximum / .82f) / Step) : 0;
        Peak = Math.Min(Peak, Maximum);
    }

    private void AddToBin(int bin, float amount, bool includeCovered)
    {
        if ((uint)bin >= (uint)Height.Length || !includeCovered && !Visible[bin]) return;
        Height[bin] = Math.Min(Maximum, Height[bin] + amount);
        Peak = Math.Max(Peak, Height[bin]);
    }

    public void Deposit(float x, float amount, bool includeCovered = false)
    {
        int bin = Bin(x);
        AddToBin(bin, amount * .52f, includeCovered);
        AddToBin(bin - 1, amount * .20f, includeCovered);
        AddToBin(bin + 1, amount * .20f, includeCovered);
        AddToBin(bin - 2, amount * .04f, includeCovered);
        AddToBin(bin + 2, amount * .04f, includeCovered);
    }

    public void SetOcclusion(IReadOnlyList<DesktopWindow> windows, int z, bool desktopWindowsOnly = false)
    {
        nextOccluders.Clear();
        for (int i = 0; i < z; i++)
        {
            if (desktopWindowsOnly && !windows[i].CollectsSnow) continue;
            Rectangle cover = windows[i].Bounds;
            if (cover.Right <= X || cover.Left >= Bounds.Right || cover.Top > Y + 1 ||
                cover.Bottom < Y - (int)(150 * Scale)) continue;
            nextOccluders.Add(cover);
        }
        bool changed = nextOccluders.Count != occluders.Count;
        for (int i = 0; !changed && i < occluders.Count; i++)
            changed = occluders[i] != nextOccluders[i];
        if (!changed && (!occlusionDirty || occluders.Count == 0)) return;
        occlusionDirty = false;
        (occluders, nextOccluders) = (nextOccluders, occluders);
        Array.Fill(Visible, true);
        Clip.MakeInfinite();
        foreach (Rectangle cover in occluders)
        {
            Clip.Exclude(cover);
            if (cover.Bottom <= Y || cover.Top > Y) continue;
            int first = Math.Max(0, Bin(cover.Left));
            int last = Math.Min(Height.Length - 1, Bin(cover.Right - 1));
            for (int j = first; j <= last; j++)
                if (Center(j) >= cover.Left && Center(j) < cover.Right) Visible[j] = false;
        }
    }

    public void Settle(float dt, bool melt)
    {
        if (Peak <= 0) return;
        Peak = 0;
        float slope = Step * .49f;
        for (int i = 0; i < Height.Length; i++)
        {
            if (melt) Height[i] = Math.Max(0, Height[i] - dt * (.09f * Scale + Height[i] * .004f));
            if (i == 0 || Visible[i] != Visible[i - 1]) continue;
            float difference = Height[i] - Height[i - 1];
            if (Math.Abs(difference) <= slope) continue;
            float transfer = (difference - MathF.CopySign(slope, difference)) * Math.Min(.38f, dt * 9);
            Height[i] -= transfer;
            Height[i - 1] += transfer;
        }

        for (int i = 0; i < Height.Length; i++)
        {
            bool left = i < Height.Length / 2;
            if ((left ? i : Height.Length - i - 1) < edgeBins && Visible[i])
            {
                float x = Center(i);
                float distance = Math.Min(x - X, Bounds.Right - x);
                float allowed = Math.Max(0, distance - Math.Max(Step, 5 * Scale)) * .82f;
                float overflow = Height[i] - allowed;
                if (overflow > 0 && IsVisibleAt(x, Y - allowed - .25f))
                {
                    Height[i] = allowed;
                    if (left) leftSpill += overflow;
                    else rightSpill += overflow;
                }
            }
            Peak = Math.Max(Peak, Height[i]);
        }
    }

    public float TakeSpill(bool left)
    {
        ref float pending = ref (left ? ref leftSpill : ref rightSpill);
        if (pending < 4 * Scale) return 0;
        float amount = pending;
        pending = 0;
        return amount;
    }

    public void Clear()
    {
        Array.Clear(Height);
        Stacks.Clear();
        Peak = 0;
        leftSpill = rightSpill = 0;
    }

    public void Dispose() => Clip.Dispose();
}

internal sealed class SnowStack(float offset)
{
    public float Offset = offset;
    public List<float> Radii { get; } = [];
    public float Height => Radii.Sum() * 1.88f;
}

internal enum SnowParticleKind : byte { Flake, Powder, Ball }

internal struct SnowParticle
{
    public bool DrawAboveWindows;
    public bool PassedBehindWindow;
    public float X, Y, Vx, Vy, Radius, Phase, Mass, Age;
    public byte Tone;
    public SnowParticleKind Kind;
}

internal sealed class SnowScreen(Rectangle bounds, Rectangle workArea, float scale)
{
    public Rectangle Bounds { get; } = bounds;
    public float Scale { get; } = scale;
    public SnowBank Ground { get; } = new(
        new Rectangle(bounds.Left, workArea.Bottom < bounds.Bottom ? workArea.Bottom - 1 : bounds.Bottom - 1,
            bounds.Width, 1), scale);
    public float? PlowX { get; set; }
    public float SpawnCredit { get; set; }
}
