using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ChaoGarden.ChaoPresentation;
using ChaoGarden.Simulation.Desktop;

namespace ChaoGarden.DesktopChao;

public sealed class PetForm : LayeredPetWindow
{
    private readonly DesktopChaoState state;
    private readonly PetApplicationContext? host;
    private bool started;
    public DesktopChaoState State => state;
    public void SaveNow() => Save();
    public void UpdateName() => Text = state.Chao.Name + " · DesktopChao";
    public void ShowControls() => menu.Show(Cursor.Position);
    public void SetPetVisible(bool visible)
    {
        if (visible) RestorePet(); else Hide();
    }
    public void StartPet(bool visible)
    {
        _ = Handle;
        if (!started)
        {
            started = true;
            previous = clock.Elapsed.TotalSeconds;
            ConfigureRenderer();
            timerResolution = TimeBeginPeriod(1) == 0;
            if (host == null) timer.Change(1, Timeout.Infinite);
        }
        if (visible) Show();
    }
    private readonly PetSaveStore store;
    private LiveChaoRenderer? live;
    private readonly string resources;
    private readonly AppearanceMenu appearance;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly System.Threading.Timer timer;
    private volatile bool closing;
    private readonly List<double> smokeFrameTimes = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly bool smoke;
    private readonly bool offscreen;
    private PointF smoothCursor;
    private bool cursorInitialized;
    private Bitmap? canvas;
    private double previous, accumulator, nextFrame, nextSave = 15, nextAppearanceCheck;
    private string requestedKey = "", status = "Preparing your Chao…";
    private double nextRenderRetry;
    private bool renderFailed;
    private bool timerResolution, dragging;
    private Point dragStart, dragOrigin;
    private bool dragMoved;
    private int lastContact;
    private readonly DesktopSurfaceController surfaces = new();
    private IReadOnlyList<WindowLedge> windowLedges = [];
    private double nextWindowRead;
    private double BodyWidth => DesktopPlacement.BodyWidth(state);
    private double SpriteHeight => state.Settings.Size * 416.0 / 384;
    private int smokeStage;
    private double smokeReadyAt;
    private string loadedKey = "", smokeInitialKey = "";

    public PetForm(DesktopChaoState state, PetSaveStore store, string resources, bool smoke = false, PetApplicationContext? host = null, bool offscreen = false)
    {
        // One pending UI callback at a time. A one-shot deadline avoids WM_TIMER's
        // coarse cadence without piling up callbacks when the machine is busy.
        timer = new System.Threading.Timer(_ =>
        {
            if (closing) return;
            try { BeginInvoke((Action)UpdatePet); }
            catch (InvalidOperationException) when (closing || IsDisposed) { }
        }, null, Timeout.Infinite, Timeout.Infinite);
        this.state = state;
        this.host = host;
        this.store = store;
        this.smoke = smoke;
        this.offscreen = offscreen;
        this.resources = resources;
        appearance = new AppearanceMenu(resources);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = !smoke && !offscreen;
        WindowState = FormWindowState.Normal;
        UpdateName();
        StartPosition = FormStartPosition.Manual;
        TopMost = state.Settings.AlwaysOnTop;
        Icon = DesktopAssets.Icon();
        tray = new NotifyIcon { Icon = Icon, Text = "DesktopChao", Visible = !smoke && host == null, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => RestorePet();
        ContextMenuStrip = menu;
        menu.Opening += (_, opening) =>
        {
            BuildMenu();
            // It starts empty and is populated on demand; clear WinForms' empty-menu cancellation.
            opening.Cancel = false;
            // The pet normally avoids activation. A user-opened menu needs a foreground
            // owner so Windows dismisses it when another window is clicked.
            SetForegroundWindow(Handle);
        };
        menu.Closed += (_, _) => PostMessage(Handle, 0, IntPtr.Zero, IntPtr.Zero);
        Shown += (_, _) =>
        {
            StartPet(false);
            if (store.RecoveryMessage != null)
                tray.ShowBalloonTip(5000, Text, store.RecoveryMessage, ToolTipIcon.Info);
        };
        Resize += (_, _) => { previous = clock.Elapsed.TotalSeconds; accumulator = 0; };
        FormClosing += (_, e) =>
        {
            if (host != null && !host.Quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; host.SetVisible(this, false); return; }
            closing = true; timer.Change(Timeout.Infinite, Timeout.Infinite); Save();
        };
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                dragStart = Cursor.Position;
                dragOrigin = Location;
                dragMoved = false;
                Capture = true;
            }
        };
        MouseMove += (_, _) =>
        {
            if (dragging)
            {
                Point cursor = Cursor.Position;
                if (!dragMoved && (Math.Abs(cursor.X - dragStart.X) >= 4 || Math.Abs(cursor.Y - dragStart.Y) >= 4))
                {
                    dragMoved = true;
                    surfaces.PickUp(state, WorkArea(), BodyWidth, SpriteHeight);
                    TopMost = true;
                }
                if (dragMoved)
                {
                    state.Settings.Monitor = Screen.FromPoint(cursor).DeviceName;
                    surfaces.MoveHeld(state, WorkArea(), BodyWidth, SpriteHeight,
                        dragOrigin.X + cursor.X - dragStart.X + DesktopPlacement.FrameInset(state),
                        dragOrigin.Y + cursor.Y - dragStart.Y + lastContact);
                }
            }
        };
        MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && dragging)
            {
                dragging = false;
                Capture = false;
                if (dragMoved) surfaces.Drop(state);
                else DesktopChaoSimulation.Pet(state);
                TopMost = state.Settings.AlwaysOnTop;
                Save();
            }
        };
        MouseCaptureChanged += (_, _) =>
        {
            if (!dragging || Capture) return;
            dragging = false;
            if (dragMoved) surfaces.Drop(state);
            TopMost = state.Settings.AlwaysOnTop;
        };
    }

    private void ConfigureRenderer()
    {
        state.Settings.RenderingMode = DesktopRenderingMode.Live3D;
        DesktopAnimationSystem.EnsureInitialized(state);
        live = new LiveChaoRenderer(resources, host?.Graphics);
        UpdateAppearance();
    }

    private Rectangle WorkArea() =>
        (Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == state.Settings.Monitor)
            ?? Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;

    internal void UpdatePet()
    {
        if (closing) return;
        double now = clock.Elapsed.TotalSeconds;
        double elapsed = Math.Min(.25, Math.Max(0, now - previous));
        previous = now;
        bool minimized = WindowState == FormWindowState.Minimized;
        if (!smoke && !offscreen && now >= nextWindowRead)
        {
            windowLedges = host?.WindowLedges(WorkArea()) ?? DesktopWindowSurfaces.Read(WorkArea());
            nextWindowRead = now + .1;
        }
        if (!state.Settings.Paused && !(minimized && state.Settings.PauseWhenMinimized) && (!dragging || dragMoved))
        {
            accumulator += elapsed;
            while (accumulator >= 1.0 / 60)
            {
                if (dragging) DesktopChaoSimulation.Tick(state, 1);
                else
                {
                    surfaces.Tick(state, WorkArea(), BodyWidth, SpriteHeight, windowLedges);
                    if (host != null && (host.Fruit.Count > 0 || state.TargetFruitId != null))
                    {
                        Screen screen = PetApplicationContext.FruitScreen(state.Settings.Monitor);
                        Rectangle foodArea = surfaces.Area(screen.WorkingArea);
                        Guid? food = DesktopForagingSystem.Update(state, host.FoodCandidates(screen, BodyWidth, surfaces.Support),
                            Math.Max(1, foodArea.Width - BodyWidth), Visible && !minimized && state.Altitude == 0);
                        if (food is { } id) host.TryAcquireFruit(this, id);
                    }
                }
                accumulator -= 1.0 / 60;
            }
        }
        else accumulator = 0;
        if (now >= nextSave) { Save(); nextSave = now + 15; }
        if (now >= nextAppearanceCheck)
        {
            nextAppearanceCheck = now + .5;
            string key = DesktopRecipes.VisualKey(state.Chao);
            if (key != requestedKey) UpdateAppearance();
        }
        if (Visible && !minimized && now >= nextFrame)
        {
            double interval = 1.0 / state.Settings.RenderFramesPerSecond;
            // Keep deadlines on the 60 Hz clock, skipping missed frames without bursts.
            nextFrame += (Math.Floor(Math.Max(0, now - nextFrame) / interval) + 1) * interval;
            RenderPet();
        }
        if (smoke) SmokeCheck(now);
        // Account for render time instead of polling every 8 ms (which quantizes frame times).
        if (!closing && host == null)
            timer.Change(minimized || !Visible ? 16 : Math.Clamp((int)Math.Ceiling((nextFrame - clock.Elapsed.TotalSeconds) * 1000), 1, 17),
                Timeout.Infinite);
    }

    private void RenderPet()
    {
        int width = state.Settings.Size;
        int height = (int)Math.Round(width * 416.0 / 384);
        if (canvas == null || canvas.Width != width || canvas.Height != height)
        {
            canvas = ResizeSurface(width, height);
            Size = new Size(width, height);
        }
        Rectangle area = surfaces.Area(WorkArea());
        int left = DesktopPlacement.FrameLeft(state, area);
        if (live == null || clock.Elapsed.TotalSeconds < nextRenderRetry) return;
        float ground = live.GroundRatio;
        int lift = (int)Math.Round(state.Altitude * Math.Max(0, area.Height - height));
        Point cursor = Cursor.Position;
        if (!cursorInitialized) { smoothCursor = cursor; cursorInitialized = true; }
        smoothCursor = new PointF(smoothCursor.X + (cursor.X - smoothCursor.X) * .25f,
            smoothCursor.Y + (cursor.Y - smoothCursor.Y) * .25f);
        float yaw = 0, pitch = 0;
        if (state.Settings.FollowCursor && !DesktopChaoSimulation.IsSleeping(state))
        {
            yaw = Math.Clamp((smoothCursor.X - (left + width * .5f)) / 10, -30, 30);
            pitch = Math.Clamp((smoothCursor.Y - (area.Bottom - lift - height * ground + height * .5f)) / 11, -20, 20);
        }
        Point position = new(left, area.Bottom - (int)Math.Ceiling(height * ground));
        try
        {
            // One solid model per frame. Native motion playback handles the skeletal
            // transitions; never composite the previous and current rendered images.
            int contact = live.Draw(canvas, state, yaw, pitch);
            if (!dragging || !dragMoved) lastContact = contact;
            position.X = left;
            position.Y = area.Bottom - lift - contact;
            if (renderFailed)
            {
                renderFailed = false;
                status = "Live 3D - Ready";
                UpdateTray();
            }
        }
        catch (Exception ex)
        {
            if (smoke) throw;
            if (!renderFailed)
            {
                Log(ex);
                tray.ShowBalloonTip(5000, "Chao rendering failed", ex.Message, ToolTipIcon.Warning);
            }
            renderFailed = true;
            status = "Render failed — retrying";
            UpdateTray();
            nextRenderRetry = clock.Elapsed.TotalSeconds + 2;
            return;
        }
        // Keep automated test instances away from the user's interactive pet.
        if (smoke || offscreen) position.X = SystemInformation.VirtualScreen.Right + width;
        Location = position;
        Present(canvas, position);
        if (smoke && smokeStage == 3) smokeFrameTimes.Add(clock.Elapsed.TotalMilliseconds);
    }

    private void UpdateAppearance()
    {
        requestedKey = DesktopRecipes.VisualKey(state.Chao);
        live!.SetAppearance(state.Chao, requestedKey);
        loadedKey = requestedKey;
        status = "Live 3D - Ready";
        nextRenderRetry = 0;
        UpdateTray();
    }

    private void BuildMenu()
    {
        while (menu.Items.Count > 0) menu.Items[0].Dispose();
        menu.Items.Add(new ToolStripMenuItem(state.Chao.Name.Replace("&", "&&")) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem individual = new("This Chao");
        if (host != null)
        {
            individual.DropDownItems.Add("Rename…", null, (_, _) => host.Rename(this));
            individual.DropDownItems.Add("Hide this Chao", null, (_, _) => host.SetVisible(this, false));
        }
        individual.DropDownItems.Add("Minimize to taskbar", null, (_, _) => WindowState = FormWindowState.Minimized);
        if (host != null)
        {
            individual.DropDownItems.Add(new ToolStripSeparator());
            individual.DropDownItems.Add("Delete chao", null, (_, _) => host.DeleteChao(this));
        }
        menu.Items.Add(individual);
        if (host != null) menu.Items.Add(host.ChaoMenu());
        ToolStripMenuItem appearanceSection = new("Appearance");
        appearanceSection.DropDownItems.Add(TypeMenu());
        appearanceSection.DropDownItems.Add(appearance.Parts(state.Chao, AppearanceChanged));
        appearanceSection.DropDownItems.Add(AppearanceMenu.Accessories(state.Chao, AppearanceChanged));
        menu.Items.Add(appearanceSection);
        if (host != null) menu.Items.Add(host.FruitMenu(PetApplicationContext.FruitScreen(state.Settings.Monitor), Cursor.Position.X));
        ToolStripMenuItem settings = new("Settings");
        Toggle(settings, "Always on top", state.Settings.AlwaysOnTop, value => { state.Settings.AlwaysOnTop = value; TopMost = value; });
        Toggle(settings, "Follow mouse cursor", state.Settings.FollowCursor, value => state.Settings.FollowCursor = value);
        Toggle(settings, "Pause while minimized", state.Settings.PauseWhenMinimized, value => state.Settings.PauseWhenMinimized = value);
        settings.DropDownItems.Add(new ToolStripSeparator());
        if (host != null) settings.DropDownItems.Add(host.SizeMenu());
        ToolStripMenuItem displays = new("Display");
        foreach (Screen screen in Screen.AllScreens)
        {
            ToolStripMenuItem choice = new(screen.DeviceName + (screen.Primary ? " (primary)" : ""))
                { Checked = screen.DeviceName == PetApplicationContext.FruitScreen(state.Settings.Monitor).DeviceName };
            choice.Click += (_, _) => { state.Settings.Monitor = screen.DeviceName; Save(); };
            displays.DropDownItems.Add(choice);
        }
        settings.DropDownItems.Add(displays);
        menu.Items.Add(settings);
        menu.Items.Add(new ToolStripSeparator());
        Add("Open save folder", OpenSaveFolder);
        Add("Quit", () => { if (host != null) host.Quit(); else Close(); });
    }

    public ToolStripMenuItem TypeMenu()
    {
        ToolStripMenuItem root = new("Chao type");
        foreach (var type in DesktopChaoTypes.All)
        {
            ToolStripMenuItem choice = new(DesktopChaoTypes.Label(type)) { Checked = state.Chao.ChaoType == type };
            choice.Click += (_, _) =>
            {
                DesktopChaoTypes.Apply(state, type);
                state.LocalType = type;
                AppearanceChanged();
            };
            root.DropDownItems.Add(choice);
        }
        if (state.Sa2Slot != null)
        {
            root.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem source = new("Use SA2 type") { Checked = state.LocalType == null };
            source.Click += (_, _) => { state.LocalType = null; Save(); };
            root.DropDownItems.Add(source);
        }
        return root;
    }

    private void OpenSaveFolder()
    {
        try
        {
            Save();
            DesktopShell.OpenFolder(store.DirectoryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log(ex);
            tray.ShowBalloonTip(5000, "Could not open save folder",
                store.DirectoryPath + Environment.NewLine + ex.Message, ToolTipIcon.Warning);
        }
    }

    private void AppearanceChanged() { Save(); UpdateAppearance(); }
    private void Add(string label, Action action)
    {
        ToolStripMenuItem item = new(label);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
    private void Toggle(ToolStripMenuItem parent, string label, bool current, Action<bool> change)
    {
        ToolStripMenuItem item = new(label) { Checked = current };
        item.Click += (_, _) => { change(!current); Save(); };
        parent.DropDownItems.Add(item);
    }
    private void RestorePet()
    {
        Show();
        WindowState = FormWindowState.Normal;
        TopMost = state.Settings.AlwaysOnTop;
        previous = clock.Elapsed.TotalSeconds;
    }
    private void UpdateTray() => tray.Text = ("Chao · " + status)[..Math.Min(63, ("Chao · " + status).Length)];
    internal DesktopChaoState SaveSnapshot(DesktopChaoState value) =>
        surfaces.SaveSnapshot(value, WorkArea(), BodyWidth, SpriteHeight);
    private void Save()
    {
        try { store.Save(SaveSnapshot(state)); }
        catch (Exception ex) { Log(ex); status = "Could not save — check desktop.log"; }
    }
    private void Log(Exception ex)
    {
        try { File.AppendAllText(Path.Combine(store.DirectoryPath, "desktop.log"), DateTime.Now + " " + ex + Environment.NewLine); }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
    }

    // Optional in-process Windows smoke test: exercises our own host, not other applications.
    private void SmokeCheck(double now)
    {
        if (now > 1800) throw new TimeoutException("Desktop smoke test timed out waiting for the 3D model.");
        if (live == null) return;
        if (smokeStage == 0)
        {
            smokeInitialKey = loadedKey;
            RenderPet();
            state.Settings.Paused = true;
            canvas!.Save(Path.Combine(store.DirectoryPath, "desktop-before.png"), ImageFormat.Png);
            BuildMenu();
            string[] labels = menu.Items.OfType<ToolStripMenuItem>().Select(item => item.Text ?? "").ToArray();
            if (!labels.Contains("Appearance") || !labels.Contains("Settings") || !labels.Contains("This Chao")
                || labels.Intersect(new[] { "Feed a Chao nut", "Animations", "Pause simulation", "Live 3D - Ready", "Show Chao", "Pet Chao", "Sit down", "Sleep", "Wake up", "60 fps playback", "Rendering", "2D spritesheets", "Rebuild sprites" }).Any())
                throw new InvalidOperationException("Context menu controls do not match the desktop interaction model.");
            if (!Visible || WindowState != FormWindowState.Normal || state.Settings.RenderFramesPerSecond != 60)
                throw new InvalidOperationException("The pet must start visible at 60 fps.");
            TopMost = true;
            WindowState = FormWindowState.Minimized;
            smokeStage = 1;
        }
        else if (smokeStage == 1)
        {
            RestorePet();
            int originalSize = state.Settings.Size;
            state.Settings.Size += 16;
            RenderPet();
            state.Settings.Size = originalSize;
            DesktopChaoSimulation.Feed(state);
            RenderPet();
            canvas!.Save(Path.Combine(store.DirectoryPath, "desktop-feeding.png"), ImageFormat.Png);
            DesktopChaoSimulation.Begin(state, DesktopActivity.Idle, 240);
            state.RemainingBites = 0;
            ToolStripMenuItem animalParts = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Appearance").DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Animal parts");
            ToolStripMenuItem ears = animalParts.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Ears");
            string targetEar = state.Chao.Appearance.EarsPart == ChaoGarden.Core.Enums.SA2BAnimalType.None ? "Rabbit" : "None";
            ears.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == targetEar).PerformClick();
            ToolStripMenuItem wings = animalParts.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Wings");
            wings.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Dragon").PerformClick();
            ToolStripMenuItem accessories = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "Appearance").DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Accessories");
            ToolStripMenuItem hats = accessories.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Hat");
            string targetHat = state.Chao.Appearance.Hat == ChaoGarden.Core.Enums.Hat.None ? "RedWoolBeanie" : "None";
            hats.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == targetHat).PerformClick();
            smokeStage = 2;
        }
        else if (smokeStage == 2 && loadedKey == requestedKey && loadedKey != smokeInitialKey)
        {
            smokeStage = 3;
            smokeReadyAt = now + 3;
        }
        else if (smokeStage == 3 && now >= smokeReadyAt)
        {
            RenderPet();
            int contact = live.BodyBounds.Bottom;
            if (Math.Abs(Top + contact - WorkArea().Bottom) > 3)
                throw new InvalidOperationException($"The model does not meet the desktop floor: top={Top}, pixels={contact}, floor={WorkArea().Bottom}.");
            canvas!.Save(Path.Combine(store.DirectoryPath, "desktop-smoke.png"), ImageFormat.Png);
            Save();
            if (DesktopRecipes.VisualKey(store.Load().Chao) != loadedKey)
                throw new InvalidDataException("The saved Chao does not match the displayed model.");
            File.WriteAllText(Path.Combine(store.DirectoryPath, "smoke-result.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    layeredWindowPresented = true,
                    contextMenu = true,
                    minimizeRestore = true,
                    saveWritten = true,
                    liveAppearanceUpdated = true,
                    noSpriteCache = !Directory.Exists(Path.Combine(store.DirectoryPath, "sprites")),
                    renderingMode = state.Settings.RenderingMode.ToString(),
                    floorContact = true,
                    surfaceResize = true,
                    presentedFrames = smokeFrameTimes.Count,
                    averageFrameIntervalMs = smokeFrameTimes.Count < 2 ? 0 :
                        (smokeFrameTimes[^1] - smokeFrameTimes[0]) / (smokeFrameTimes.Count - 1),
                    idleCursorDirections = true,
                    fixedSixtyFps = true,
                    before = smokeInitialKey,
                    after = loadedKey
                }, BoothJson.Options));
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing = true;
            timer.Dispose();
            live?.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            if (timerResolution) TimeEndPeriod(1);
        }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] private static extern uint TimeBeginPeriod(uint period);
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] private static extern uint TimeEndPeriod(uint period);
}
