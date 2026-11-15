namespace DesktopSnow;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            MessageBox.Show("Требуется Windows 10 версии 2004 или новее.", "Desktop Snow");
            return;
        }
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\DesktopSnow", out bool first);
        if (!first)
        {
            nint running = Native.FindWindow(null, SnowHost.WindowName);
            if (running != 0)
            {
                Native.GetWindowThreadProcessId(running, out uint process);
                Native.AllowSetForegroundWindow(process);
                Native.PostMessage(running, SnowHost.OpenSettingsMessage, 0, 0);
            }
            return;
        }
        Application.ThreadException += (_, e) => Settings.Log(e.Exception.ToString());
        using var desktopSnow = new TrayApplication();
        Application.Run(desktopSnow);
    }
}

internal sealed class TrayApplication : ApplicationContext
{
    private readonly Settings settings;
    private readonly SnowHost host;
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem power, molding;
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 400 };
    private Task<bool> pendingSave = Task.FromResult(true);
    private SettingsForm? form;
    private bool enabled, hide, surfaceWindows, surfaceGround, snowmen, plow, reduceLoad;
    private int minutes;

    public TrayApplication()
    {
        settings = Settings.Load();
        enabled = settings.Enabled;
        hide = settings.HideFromCapture;
        surfaceWindows = settings.Windows;
        surfaceGround = settings.Ground;
        snowmen = settings.Snowmen;
        plow = settings.Plow;
        reduceLoad = settings.ReduceLoad;
        minutes = settings.PlowMinutes;
        host = new SnowHost(settings);
        host.StatusChanged += Sync;
        host.OpenSettings += ShowForm;
        using Stream iconAsset = typeof(TrayApplication).Assembly.GetManifestResourceStream("Snowplow.ico")!;
        icon = new Icon(iconAsset);
        power = new ToolStripMenuItem("", null, (_, _) => { settings.Enabled = !settings.Enabled; Apply(); });
        molding = new ToolStripMenuItem("", null, (_, _) => ToggleMolding());
        menu = new ContextMenuStrip();
        menu.Items.AddRange([power, molding, new ToolStripMenuItem("Настройки", null, (_, _) => ShowForm()),
            new ToolStripSeparator(), new ToolStripMenuItem("Выход", null, (_, _) => ExitThread())]);
        tray = new NotifyIcon { Icon = icon, Text = "Desktop Snow", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowForm(); };
        saveTimer.Tick += (_, _) => _ = SaveInBackground();
        Sync();
        SettingsForm panel = EnsureForm();
        MainForm = panel;
        panel.ShowLauncher();
    }

    private void ToggleMolding()
    {
        if (host.IsMolding)
        {
            host.EndMolding();
            return;
        }
        if (!host.BeginMolding())
        {
            tray.ShowBalloonTip(3500, "Desktop Snow", "Не удалось включить лепку. Проверьте, свободна ли клавиша Esc.", ToolTipIcon.Warning);
            return;
        }
        form?.Collapse();
        tray.ShowBalloonTip(4500, "Лепка снеговика",
            "Прокатите снежок по сугробу с зажатой ЛКМ. Перетащите три комка друг на друга. Esc — выход.", ToolTipIcon.Info);
    }

    private void ShowForm()
    {
        SettingsForm panel = EnsureForm();
        panel.ShowLauncher();
        panel.Expand();
    }

    private SettingsForm EnsureForm()
    {
        form ??= new SettingsForm(settings, icon, Apply, ToggleMolding, ExitThread, SaveInBackground);
        Sync();
        return form;
    }
    private void Apply()
    {
        if (enabled != settings.Enabled)
        {
            enabled = settings.Enabled;
            host.SetEnabled();
        }
        if (hide != settings.HideFromCapture)
        {
            hide = settings.HideFromCapture;
            host.SetCaptureExclusion();
        }
        if (surfaceWindows != settings.Windows || surfaceGround != settings.Ground)
        {
            surfaceWindows = settings.Windows;
            if (surfaceGround != settings.Ground)
            {
                surfaceGround = settings.Ground;
                host.ResetPlow();
            }
            host.RefreshSurfaces();
        }
        if (snowmen != settings.Snowmen)
        {
            snowmen = settings.Snowmen;
            if (!snowmen) host.ClearSnowmen();
        }
        if (plow != settings.Plow || minutes != settings.PlowMinutes)
        {
            plow = settings.Plow;
            minutes = settings.PlowMinutes;
            host.ResetPlow();
        }
        if (reduceLoad != settings.ReduceLoad)
        {
            reduceLoad = settings.ReduceLoad;
            host.UpdateFrameRate();
        }
        Sync();
        form?.SettingsChanged();
        if (settings.Opacity == 0) host.EndMolding();
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void Sync()
    {
        power.Text = settings.Enabled ? "Выключить снег" : "Включить снег";
        molding.Text = host.IsMolding ? "Выйти из лепки (Esc)" : "Слепить снеговика";
        molding.Enabled = CanMold;
        tray.Text = host.IsMolding ? "Desktop Snow · Лепка снеговика" :
            settings.Enabled ? "Desktop Snow" : "Desktop Snow · Снег выключен";
        form?.Sync(settings, host.IsMolding, host.CaptureSupported, CanMold);
    }

    private bool CanMold => settings.Enabled && settings.Opacity > 0 && settings.Snowmen &&
        (settings.Windows || settings.Ground);

    private Task<bool> SaveInBackground()
    {
        saveTimer.Stop();
        Settings snapshot = settings.Copy();
        Task previous = pendingSave;
        pendingSave = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            return snapshot.Save();
        });
        return pendingSave;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            saveTimer.Dispose();
            pendingSave.GetAwaiter().GetResult();
            settings.Save();
            host.StatusChanged -= Sync;
            host.OpenSettings -= ShowForm;
            host.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            icon.Dispose();
        }
        base.Dispose(disposing);
    }

}
