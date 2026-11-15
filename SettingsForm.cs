using System.Drawing.Drawing2D;

namespace DesktopSnow;

internal sealed class SettingsForm : Form
{
    public static readonly Color Background = Color.FromArgb(48, 48, 48);
    public static readonly Color Panel = Color.FromArgb(68, 68, 68);
    public static readonly Color Ink = Color.FromArgb(247, 247, 247);
    public static readonly Color Muted = Color.FromArgb(210, 210, 210);
    public static readonly Color Accent = Color.FromArgb(183, 222, 252);
    private readonly CheckBox enabled;
    private readonly Label status;
    private readonly Label saveStatus;
    private readonly NumericUpDown interval;
    private readonly Button mold;
    private readonly Button launcher;
    private readonly Panel card;
    private readonly Bitmap snowImage;
    private const int CardHeight = 694;
    private Region? shape;
    private bool syncing, expanded;

    public SettingsForm(Settings settings, Icon icon, Action changed, Action toggleMolding,
        Action exit, Func<Task<bool>> save)
    {
        using var panelIcon = new Icon(icon, 24, 24);
        snowImage = panelIcon.ToBitmap();
        Icon = icon;
        Text = "Desktop Snow";
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = settings.ShowOnTaskbar;
        TopMost = true;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Background;
        Opacity = .95;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(370, CardHeight);
        KeyPreview = true;

        launcher = new Button
        {
            Text = "Настройки", Image = snowImage, TextImageRelation = TextImageRelation.ImageBeforeText,
            FlatStyle = FlatStyle.Flat, BackColor = Background, ForeColor = Ink,
            Dock = DockStyle.Fill, Cursor = Cursors.Hand
        };
        launcher.FlatAppearance.BorderSize = 0;
        launcher.FlatAppearance.MouseOverBackColor = Panel;
        launcher.Click += (_, _) => Expand();
        Controls.Add(launcher);

        card = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 10), Visible = false };
        Controls.Add(card);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        card.Controls.Add(scroll);
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Margin = Padding.Empty
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(table);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        header.Controls.Add(new PictureBox
        {
            Image = snowImage, SizeMode = PictureBoxSizeMode.CenterImage, Dock = DockStyle.Fill,
            BackColor = Background, Margin = new Padding(0, 6, 4, 6)
        }, 0, 0);
        var title = TextLabel("Desktop Snow", Ink, 16, FontStyle.Bold);
        header.Controls.Add(title, 1, 0);
        header.Controls.Add(HeaderButton("−", "Свернуть настройки", Panel, Collapse), 2, 0);
        header.Controls.Add(HeaderButton("×", "Закрыть приложение", Color.FromArgb(151, 49, 52), exit), 3, 0);
        AddRow(table, header, 52);

        enabled = Toggle("Снегопад", settings.Enabled, value => settings.Enabled = value);
        AddRow(table, enabled, 38);
        AddRow(table, new PercentSlider("Интенсивность", settings.Intensity, value =>
        {
            settings.Intensity = value;
            changed();
        }), 53);
        AddRow(table, new PercentSlider("Прозрачность", settings.Opacity, value =>
        {
            settings.Opacity = value;
            changed();
        }), 53);

        AddRow(table, TextLabel("Оседание снега", Muted, 8, FontStyle.Bold), 28);
        var surfaces = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        surfaces.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        surfaces.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        surfaces.Controls.Add(Toggle("Окна", settings.Windows, value => settings.Windows = value), 0, 0);
        surfaces.Controls.Add(Toggle("Экран", settings.Ground, value => settings.Ground = value), 1, 0);
        AddRow(table, surfaces, 35);

        AddRow(table, TextLabel("Взаимодействие", Muted, 8, FontStyle.Bold), 28);
        AddRow(table, Toggle("Таяние снега", settings.Melt, value => settings.Melt = value), 32);
        AddRow(table, Toggle("Реакция на курсор", settings.Cursor, value => settings.Cursor = value), 32);
        AddRow(table, Toggle("Снежки и снеговики", settings.Snowmen, value => settings.Snowmen = value), 32);
        mold = new Button
        {
            Text = "Слепить снеговика", BackColor = Accent, ForeColor = Background,
            FlatStyle = FlatStyle.Flat, Dock = DockStyle.Fill, Cursor = Cursors.Hand,
            Margin = new Padding(0, 2, 0, 7)
        };
        mold.FlatAppearance.BorderSize = 0;
        mold.Click += (_, _) => toggleMolding();
        AddRow(table, mold, 44);
        AddRow(table, Toggle("Снегоуборщик", settings.Plow, value => settings.Plow = value), 32);
        var schedule = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        schedule.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        schedule.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 59));
        schedule.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        schedule.Controls.Add(TextLabel("Интервал между проездами", Muted, 9), 0, 0);
        interval = new NumericUpDown
        {
            Minimum = 1, Maximum = 60, Value = settings.PlowMinutes, Enabled = settings.Plow,
            AccessibleName = "Интервал между проездами в минутах",
            BackColor = Panel, ForeColor = Ink, Dock = DockStyle.Top, TextAlign = HorizontalAlignment.Center
        };
        interval.ValueChanged += (_, _) => { settings.PlowMinutes = (int)interval.Value; changed(); };
        schedule.Controls.Add(interval, 1, 0);
        schedule.Controls.Add(TextLabel("мин", Muted, 8), 2, 0);
        AddRow(table, schedule, 39);
        AddRow(table, Toggle("Скрывать снег при захвате экрана", settings.HideFromCapture,
            value => settings.HideFromCapture = value), 32);
        AddRow(table, Toggle("Снизить нагрузку на ПК", settings.ReduceLoad, value => settings.ReduceLoad = value), 32);
        AddRow(table, Toggle("Значок на панели задач", settings.ShowOnTaskbar, value =>
        {
            settings.ShowOnTaskbar = value;
            ShowInTaskbar = value;
        }), 32);
        status = TextLabel("", Muted, 8);
        AddRow(table, status, 20);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        saveStatus = TextLabel("", Muted, 9);
        saveStatus.Visible = false;
        footer.Controls.Add(saveStatus, 0, 0);
        var apply = new Button
        {
            Text = "Применить", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
            ForeColor = Ink, BackColor = Panel, Cursor = Cursors.Hand, Margin = new Padding(0, 3, 0, 3)
        };
        apply.FlatAppearance.BorderColor = Accent;
        apply.Click += async (_, _) =>
        {
            ValidateChildren();
            saveStatus.Text = "Сохраняю…";
            saveStatus.Visible = true;
            apply.Enabled = false;
            bool saved = await save();
            if (IsDisposed || Disposing) return;
            apply.Enabled = true;
            if (saveStatus.Text == "Сохраняю…")
                saveStatus.Text = saved ? "Настройки сохранены" : "Ошибка сохранения";
        };
        footer.Controls.Add(apply, 1, 0);
        AddRow(table, footer, 40);
        Resize += (_, _) => RoundCorners();
        RoundCorners();

        CheckBox Toggle(string text, bool initial, Action<bool> write)
        {
            var control = new CheckBox
            {
                Text = text, Checked = initial, AutoSize = true, Anchor = AnchorStyles.Left,
                ForeColor = Ink, BackColor = Background, Padding = new Padding(6, 2, 8, 2),
                FlatStyle = FlatStyle.Flat, Margin = Padding.Empty, Cursor = Cursors.Hand
            };
            control.FlatAppearance.CheckedBackColor = Accent;
            control.CheckedChanged += (_, _) =>
            {
                if (syncing) return;
                write(control.Checked);
                changed();
            };
            return control;
        }
    }

    private static Button HeaderButton(string text, string name, Color hover, Action click)
    {
        var button = new Button
        {
            Text = text, AccessibleName = name, BackColor = Background, ForeColor = Ink,
            Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 14), Margin = new Padding(0, 9, 0, 9)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.Click += (_, _) => click();
        return button;
    }

    public void SettingsChanged()
    {
        saveStatus.Text = "";
        saveStatus.Visible = false;
    }

    public void Sync(Settings settings, bool molding, bool captureSupported, bool canMold)
    {
        syncing = true;
        enabled.Checked = settings.Enabled;
        interval.Enabled = settings.Plow;
        mold.Enabled = canMold;
        mold.Text = molding ? "Закончить лепку (Esc)" : "Слепить снеговика";
        status.Text = !captureSupported && settings.HideFromCapture ? "Windows не подтвердила скрытие слоя" :
            molding ? "Лепка снеговика · Esc для выхода" : "";
        status.Visible = status.Text.Length > 0;
        syncing = false;
    }

    public void ShowLauncher()
    {
        if (Visible) return;
        SetExpanded(ShowInTaskbar);
        PositionPanel(Screen.PrimaryScreen!.WorkingArea);
        if (ShowInTaskbar) WindowState = FormWindowState.Minimized;
        Show();
        if (!ShowInTaskbar) PositionPanel(Screen.FromControl(this).WorkingArea);
    }

    protected override bool ShowWithoutActivation => !expanded;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsSystemMenu = 0x00080000;
            const int WsMinimizeBox = 0x00020000;
            CreateParams parameters = base.CreateParams;
            // Explorer can minimize the borderless card through its taskbar button.
            if (ShowInTaskbar) parameters.Style |= WsSystemMenu | WsMinimizeBox;
            return parameters;
        }
    }

    public void Expand()
    {
        SetExpanded(true);
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        PositionPanel(Screen.FromControl(this).WorkingArea);
        Activate();
        enabled.Select();
    }

    public void Collapse()
    {
        if (!expanded) return;
        Validate();
        if (ShowInTaskbar)
        {
            WindowState = FormWindowState.Minimized;
            return;
        }
        SetExpanded(false);
        PositionPanel(Screen.FromControl(this).WorkingArea);
    }

    private void SetExpanded(bool value)
    {
        expanded = value;
        launcher.Visible = !value;
        card.Visible = value;
    }

    private void PositionPanel(Rectangle area)
    {
        if (WindowState == FormWindowState.Minimized) return;
        int Scale(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96d);
        int inset = Scale(12);
        int width = Math.Min(Scale(expanded ? 370 : 136), Math.Max(1, area.Width - 2 * inset));
        int height = Math.Min(Scale(expanded ? CardHeight : 44), Math.Max(1, area.Height - 2 * inset));
        Bounds = new Rectangle(area.Right - width - inset, area.Bottom - height - inset, width, height);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        PositionPanel(Screen.FromControl(this).WorkingArea);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x007E || message.Msg == 0x001A && message.WParam == 0x002F)
            PositionPanel(Screen.FromControl(this).WorkingArea);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Collapse();
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private void RoundCorners()
    {
        if (Width < 24 || Height < 24) return;
        int diameter = Math.Min(Math.Min(Width, Height), (int)Math.Round(32 * DeviceDpi / 96d));
        using var path = new GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(Width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(Width - diameter, Height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, Height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        Region? previous = shape;
        Region = shape = new Region(path);
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) snowImage.Dispose();
    }

    private static Label TextLabel(string text, Color color, int size, FontStyle style = FontStyle.Regular) =>
        new() { Text = text, ForeColor = color, TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Background, AutoSize = true, Anchor = AnchorStyles.Left,
            Padding = new Padding(6, 2, 6, 2), Margin = Padding.Empty,
            Font = new Font("Segoe UI", size, style) };

    private static void AddRow(TableLayoutPanel table, Control control, int height)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        table.Controls.Add(control, 0, row);
    }
}

internal sealed class PercentSlider : Control
{
    private readonly string caption;
    private readonly Action<int> changed;
    private int value;

    public PercentSlider(string caption, int value, Action<int> changed)
    {
        this.caption = caption;
        this.value = value;
        this.changed = changed;
        AccessibleName = caption;
        AccessibleRole = AccessibleRole.Slider;
        AccessibleDescription = "Стрелки меняют значение. Home и End выбирают 0 и 100 процентов.";
        Dock = DockStyle.Fill;
        BackColor = SettingsForm.Background;
        TabStop = true;
        Margin = new Padding(0, 3, 0, 5);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);
    }

    private float ScaleFactor => DeviceDpi / 96f;
    private int Edge => (int)Math.Round(9 * ScaleFactor);
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        float scale = ScaleFactor;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var baseline = new Rectangle(Edge, 0, Math.Max(1, Width - Edge * 2), (int)(24 * scale));
        TextRenderer.DrawText(g, caption, Font, baseline, SettingsForm.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, $"{value}%", Font, baseline, SettingsForm.Accent,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        float y = Height * .72f;
        float end = Edge + (Width - Edge * 2) * value / 100f;
        using var track = new Pen(Color.FromArgb(86, 101, 121), 4 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new Pen(SettingsForm.Accent, 4 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var thumb = new SolidBrush(SettingsForm.Accent);
        g.DrawLine(track, Edge, y, Width - Edge, y);
        if (value > 0) g.DrawLine(fill, Edge, y, end, y);
        g.FillEllipse(thumb, end - 6 * scale, y - 6 * scale, 12 * scale, 12 * scale);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, new Rectangle(0, (int)(y - 11 * scale), Width - 1, (int)(22 * scale)), SettingsForm.Ink, SettingsForm.Background);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        Capture = true;
        PointToValue(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture && e.Button == MouseButtons.Left) PointToValue(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) Capture = false;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        SetValue(e.KeyCode switch
        {
            Keys.Left or Keys.Down => value - 1,
            Keys.Right or Keys.Up => value + 1,
            Keys.Home => 0,
            Keys.End => 100,
            _ => value
        });
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    private void PointToValue(int x) => SetValue((int)Math.Round((x - Edge) * 100d / Math.Max(1, Width - Edge * 2)));
    private void SetValue(int next)
    {
        next = Math.Clamp(next, 0, 100);
        if (next == value) return;
        value = next;
        changed(value);
        Invalidate();
        AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibility(this);

    private sealed class SliderAccessibility(PercentSlider owner) : ControlAccessibleObject(owner)
    {
        public override string? Value
        {
            get => $"{owner.value}%";
            set
            {
                if (int.TryParse(value?.TrimEnd('%'), out int number)) owner.SetValue(number);
            }
        }
    }
}
