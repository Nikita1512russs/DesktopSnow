using System.Reflection;
using System.Runtime.InteropServices;
using DesktopSnow;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        try
        {
            CheckCoverage();
            CheckRenderingAndInteraction();
            CheckSnowfallBehindWindows();
            CheckTaskbarMinimize();
            Console.WriteLine("Desktop coverage, rendering, interaction, and taskbar checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void CheckCoverage()
    {
        var area = new Rectangle(-1920, 0, 1920, 1032);
        var left = new DesktopWindow(1, Rectangle.FromLTRB(-1920, 0, -1120, 1032), true);
        var right = new DesktopWindow(2, Rectangle.FromLTRB(-1114, 0, 0, 1032), true);
        var exposed = new List<Rectangle>();
        var coverage = new ScreenCoverage();
        Check(coverage.WindowsCoverWorkArea([left, right], area, 1), "A thin Snap seam should hide snow.");

        right = right with { Bounds = Rectangle.FromLTRB(-900, 0, 0, 1032) };
        Check(!coverage.WindowsCoverWorkArea([left, right], area, 1), "A wide gap should keep snow visible.");
        ScreenCoverage.FindExposedAreas([left, right], area, exposed);
        Check(exposed.SequenceEqual([Rectangle.FromLTRB(-1120, 0, -900, 1032)]),
            "Only the gap between the two windows should be exposed.");

        var overlapping = new DesktopWindow(3, Rectangle.FromLTRB(-1300, 150, -1030, 850), true);
        ScreenCoverage.FindExposedAreas([left, right, overlapping], area, exposed);
        Check(!exposed.Exists(rect => rect.Contains(-1080, 300)) && exposed.Exists(rect => rect.Contains(-1080, 130)),
            "Overlapping windows should clip the occupied part of a gap.");
        ScreenCoverage.FindExposedAreas([left, right with { CollectsSnow = false }], area, exposed);
        Check(exposed.Exists(rect => rect.Contains(-200, 300)), "Helper windows should leave the desktop exposed.");
        ScreenCoverage.FindExposedAreas([left, right], new Rectangle(0, 0, 1920, 1032), exposed);
        Check(exposed.SequenceEqual([new Rectangle(0, 0, 1920, 1032)]),
            "Windows on one monitor should leave the next monitor exposed.");
    }

    private static void CheckRenderingAndInteraction()
    {
        var view = new Rectangle(-300, 40, 420, 260);
        var area = new Rectangle(view.X, view.Y, view.Width, 238);
        var left = new DesktopWindow(1, Rectangle.FromLTRB(-300, 85, -200, area.Bottom), true);
        var right = new DesktopWindow(2, Rectangle.FromLTRB(-80, 85, 120, area.Bottom), true);
        using var layer = new Overlay(view);
        using var painter = new SnowPainter();
        using var scene = new SnowScene(new Settings { Intensity = 0, Melt = false, Plow = false },
            layer.ContainsExposedPoint);
        var bitmap = (Bitmap)typeof(Overlay).GetField("bitmap", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(layer)!;
        byte Alpha(int x, int y) => bitmap.GetPixel(x - view.Left, y - view.Top).A;

        void DrawFlakeAt(int x, int y = 133, bool aboveWindows = false)
        {
            layer.BeginFrame();
            layer.IncludeDrawBounds(new Rectangle(x - 6, y - 6, 13, 13), aboveWindows, fallingSnow: true);
            layer.ClearChangedTiles();
            layer.DrawFlake(new SnowParticle
                { X = x + .5f, Y = y + .5f, Radius = 4, Tone = 7, DrawAboveWindows = aboveWindows });
            layer.Upload(255);
        }

        layer.UpdateExposedAreas([], area);
        DrawFlakeAt(-228);
        Check(Alpha(-228, 133) > 0, "A flake should draw on open desktop.");
        layer.UpdateExposedAreas([left, right], area);
        DrawFlakeAt(-200);
        Check(Alpha(-228, 133) == 0 && Alpha(-201, 133) == 0 && Alpha(-200, 133) > 0,
            "Moving a window should erase existing flakes and clip new flakes at its edge.");
        DrawFlakeAt(-228, aboveWindows: true);
        Check(Alpha(-228, 133) > 0, "Snow brushed off a drift should remain visible over the window.");
        DrawFlakeAt(-140, y: area.Bottom, aboveWindows: true);
        Check(Alpha(-228, 133) == 0 && Alpha(-140, area.Bottom - 1) > 0 && Alpha(-140, area.Bottom) == 0,
            "Moving brushed snow must clear old pixels and stay above the taskbar.");
        var floating = left with { Bounds = new Rectangle(left.Bounds.X, 85, left.Bounds.Width, 90) };
        layer.UpdateExposedAreas([floating], area);
        DrawFlakeAt(-228, 210);
        Check(layer.ContainsExposedPoint(-228, 210) && !layer.ContainsFallingSnowPoint(-228, 210) &&
            Alpha(-228, 210) == 0, "Falling flakes should be hidden in the open strip below a floating window.");
        DrawFlakeAt(-228, 71);
        Check(Alpha(-228, 71) > 0, "Snow should stay visible above a floating window.");
        DrawFlakeAt(-228, 210, aboveWindows: true);
        Check(Alpha(-228, 210) > 0, "Cursor powder should stay visible below a floating window.");
        layer.UpdateExposedAreas([], area);
        DrawFlakeAt(-228, 210);
        Check(Alpha(-228, 210) > 0, "Minimizing the window should uncover the snowfall below it.");
        layer.UpdateExposedAreas([left, right], area);

        scene.SetScreens([(view, area, 1)]);
        scene.UpdateWindows([], [], []);
        var ground = scene.Screens[0].Ground;
        int y = ground.Y;
        foreach (int x in new[] { -250, -140, -40 })
            for (int j = 0; j < 4; j++) ground.Deposit(x, 10);
        scene.UpdateWindows([left, right], [], []);
        scene.Draw(layer, painter);
        layer.Upload(255);
        Check(Alpha(-140, y - 4) > 0 && Alpha(-250, y - 4) == 0 && Alpha(-40, y - 4) == 0,
            "The ground drift should use the same mask as falling flakes.");
        Check(!scene.BeginBall(new Point(-250, y - 4)) && scene.BeginBall(new Point(-140, y - 4)),
            "Molding should start only on exposed snow.");
        scene.CancelBall();

        layer.UpdateExposedAreas([right], area);
        scene.UpdateWindows([right], [], []);
        scene.Draw(layer, painter);
        layer.Upload(255);
        Check(Alpha(-250, y - 4) > 0 && Alpha(-40, y - 4) == 0,
            "Closing one window should restore snow only in the newly exposed region.");
        Check(!layer.ContainsExposedPoint(-140, area.Bottom), "The taskbar should lie outside the snow mask.");

        var middle = new DesktopWindow(3, Rectangle.FromLTRB(-170, 150, -108, area.Bottom), true);
        layer.UpdateExposedAreas([middle], area);
        scene.UpdateWindows([middle], [], []);
        for (int x = -215; x <= -66; x += SnowBank.Step) ground.Deposit(x, 16);
        ground.Settle(.06f, melt: false);
        float exposedHeight = ground.At(-206);
        float coveredHeight = ground.At(-140);
        scene.Advance(.001f, new Point(-215, y - 5), false);
        scene.Advance(.001f, new Point(-66, y - 5), false);
        Check(ground.At(-206) < exposedHeight && ground.At(-140) == coveredHeight,
            "A fast cursor sweep should not disturb snow underneath a window between its endpoints.");
        var particles = (SnowParticle[])typeof(SnowScene).GetField("particles", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(scene)!;
        Check(particles.Any(particle => particle.Kind == SnowParticleKind.Powder && particle.DrawAboveWindows),
            "Sweeping a drift should emit powder that stays visible above windows.");
    }

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }

    private static void CheckSnowfallBehindWindows()
    {
        var area = new Rectangle(0, 0, 320, 240);
        var window = new DesktopWindow(1, Rectangle.FromLTRB(0, 60, 160, 160), true);
        var exposed = new List<Rectangle>();
        ScreenCoverage.FindExposedAreas([window], area, exposed);
        using var scene = new SnowScene(new Settings { Intensity = 0, Windows = false, Plow = false },
            (x, y) => exposed.Exists(rect => x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom));
        scene.SetScreens([(area, area, 1)]);
        scene.UpdateWindows([window], [], []);
        const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
        var particles = (SnowParticle[])typeof(SnowScene).GetField("particles", fields)!.GetValue(scene)!;
        var count = typeof(SnowScene).GetField("particleCount", fields)!;
        var flakes = typeof(SnowScene).GetField("flakeCount", fields)!;
        particles[0] = new SnowParticle { X = 80, Y = 55, Vy = 100, Radius = 2, Mass = 8 };
        particles[1] = new SnowParticle { X = 240, Y = 55, Vy = 100, Radius = 2, Mass = 8 };
        count.SetValue(scene, 2);
        flakes.SetValue(scene, 2);
        for (int step = 0; step < 50; step++) scene.Advance(.05f, null, false);
        var ground = scene.Screens[0].Ground;
        int midpoint = ground.Bin(160);
        Check(ground.Height.Take(midpoint).Sum() == 0 && ground.Height.Skip(midpoint).Sum() > 0 &&
            (int)count.GetValue(scene)! == 0,
            "Flakes that passed behind a window should not fill the ground underneath it.");

        particles[0] = new SnowParticle { X = 80, Y = 100, Vy = 100, Radius = 2, Mass = 8 };
        count.SetValue(scene, 1);
        flakes.SetValue(scene, 1);
        scene.Advance(.05f, null, false);
        ScreenCoverage.FindExposedAreas([], area, exposed);
        Check((int)count.GetValue(scene)! == 1 && particles[0].Y > 100,
            "Moving a window away should reveal background snow already in flight.");
        scene.UpdateWindows([], [], []);
        for (int step = 0; step < 40; step++) scene.Advance(.05f, null, false);
        Check(ground.Height.Take(midpoint).Sum() == 0,
            "Previously hidden flakes should not suddenly fill the ground when the window is gone.");

        particles[0] = new SnowParticle { X = 80, Y = 55, Vy = 100, Radius = 2, Mass = 8 };
        count.SetValue(scene, 1);
        flakes.SetValue(scene, 1);
        for (int step = 0; step < 50; step++) scene.Advance(.05f, null, false);
        Check(ground.Height.Take(midpoint).Sum() > 0, "New snow should accumulate after the window is gone.");

        int CollapseParticleCount(int intensity, float depth)
        {
            using var coveredScene = new SnowScene(new Settings { Intensity = intensity, Melt = false, Plow = false },
                (_, _) => false);
            coveredScene.SetScreens([(area, area, 1)]);
            var foreground = new DesktopWindow(2, new Rectangle(0, 0, 280, 190), true);
            coveredScene.UpdateWindows([foreground, window], [], []);
            var banks = (Dictionary<nint, SnowBank>)typeof(SnowScene).GetField("windowBanks", fields)!
                .GetValue(coveredScene)!;
            SnowBank hidden = banks[window.Handle];
            Check(!hidden.HasVisibleEdge(80), "The test window should be covered.");
            var flight = (SnowParticle[])typeof(SnowScene).GetField("particles", fields)!.GetValue(coveredScene)!;
            flight[0] = new SnowParticle { X = 80, Y = 55, Vy = 100, Radius = 2, Mass = 8 };
            count.SetValue(coveredScene, 1);
            flakes.SetValue(coveredScene, 1);
            coveredScene.Advance(.05f, null, false);
            Check(hidden.At(80) > 0, "Falling snow should accumulate on a covered window.");
            for (int x = 8; x < 152; x += SnowBank.Step) hidden.Deposit(x, depth, includeCovered: true);
            int before = (int)count.GetValue(coveredScene)!;
            coveredScene.UpdateWindows([foreground], [], [window.Handle]);
            return (int)count.GetValue(coveredScene)! - before;
        }

        int light = CollapseParticleCount(20, 10);
        Check(light > 0 && CollapseParticleCount(100, 10) > light &&
            CollapseParticleCount(20, 24) > light,
            "A thicker bank and higher intensity should each produce more powder when a window closes.");
    }

    private static void CheckTaskbarMinimize()
    {
        const int windowStyle = -16;
        const int wsMinimizeBox = 0x00020000;
        const int wmSystemCommand = 0x0112;
        const int scMinimize = 0xF020;
        const int scRestore = 0xF120;
        const int scClose = 0xF060;

        using var card = new SettingsForm(new Settings { ShowOnTaskbar = true }, SystemIcons.Application,
            () => { }, () => { }, () => { }, () => Task.FromResult(true));
        using var context = new ApplicationContext(card);
        bool exited = false;
        context.ThreadExit += (_, _) => exited = true;
        card.ShowLauncher();
        card.Expand();
        Check(!Native.IsIconic(card.Handle) &&
            ((long)Native.GetWindowLongPtr(card.Handle, windowStyle) & wsMinimizeBox) != 0,
            "The expanded card must advertise support for minimizing from the taskbar.");
        SendMessage(card.Handle, wmSystemCommand, scMinimize, 0);
        Check(Native.IsIconic(card.Handle) && !exited, "Minimizing the card should leave Desktop Snow running.");
        SendMessage(card.Handle, wmSystemCommand, scRestore, 0);
        Check(!Native.IsIconic(card.Handle) && card.Width >= 260,
            "The system restore command should reopen the full settings card.");
        card.ShowInTaskbar = false;
        card.ShowInTaskbar = true;
        Check(!exited, "Changing taskbar visibility must not exit Desktop Snow.");
        SendMessage(card.Handle, wmSystemCommand, scMinimize, 0);
        SendMessage(card.Handle, wmSystemCommand, scClose, 0);
        Check(exited && card.IsDisposed, "Closing a minimized taskbar window must exit Desktop Snow.");
    }

    [DllImport("user32", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
