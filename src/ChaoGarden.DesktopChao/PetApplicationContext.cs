using ChaoGarden.Simulation.Desktop;

namespace ChaoGarden.DesktopChao;

/// <summary>Owns the roster, tray lifetime and shared UI-thread graphics. Hiding a pet does not destroy its simulation.</summary>
public sealed class PetApplicationContext : ApplicationContext
{
    private readonly PetRosterStore store;
    private readonly PetRoster roster;
    private readonly string resources;
    private readonly bool offscreen;
    private readonly Dictionary<Guid, PetForm> forms = new();
    private readonly Dictionary<Guid, DesktopFruitWindow> fruitWindows = new();
    private bool fruitSavePending, fruitLayoutDirty;
    private double nextFruitSave;
    public int ChaoSize => roster.Size ?? 240;
    public IReadOnlyList<DesktopFruitState> Fruit => roster.Fruit;
    public static Screen FruitScreen(string monitor) => Screen.AllScreens.FirstOrDefault(s => s.DeviceName == monitor)
        ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
    private readonly ContextMenuStrip menu = new();
    private readonly NotifyIcon tray;
    private readonly Control dispatcher = new();
    private readonly System.Threading.Timer frameTimer;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private double nextFrame, nextSync = 2;
    private bool polling;
    private readonly Dictionary<Rectangle, (double At, IReadOnlyList<WindowLedge> Ledges)> windowCache = new();
    public IReadOnlyList<WindowLedge> WindowLedges(Rectangle screen)
    {
        if (!windowCache.TryGetValue(screen, out var cached) || clock.Elapsed.TotalSeconds - cached.At >= .1)
        {
            cached = (clock.Elapsed.TotalSeconds, DesktopWindowSurfaces.Read(screen));
            windowCache[screen] = cached;
        }
        return cached.Ledges;
    }
    public bool Quitting { get; private set; }
    public SharedPetGraphics Graphics { get; }
    public string SyncStatus { get; private set; } = "SA2 synchronization off";
    public IReadOnlyCollection<PetForm> Pets => forms.Values;

    public PetApplicationContext(string data, string resources, bool offscreen = false)
    {
        this.resources = resources;
        this.offscreen = offscreen;
        store = new PetRosterStore(data);
        roster = store.Load();
        // Read every save before opening windows or changing anything on disk.
        var saved = roster.Chao.Select(entry => (entry, state: store.LoadPet(roster, entry))).ToArray();
        // A crash between the per-Chao pickup save and roster removal cannot duplicate fruit.
        HashSet<Guid> acquired = saved.Where(s => s.state.LastAcquiredFruitId != null).Select(s => s.state.LastAcquiredFruitId!.Value).ToHashSet();
        roster.Fruit.RemoveAll(f => acquired.Contains(f.Id));
        roster.Size ??= saved.FirstOrDefault().state?.Settings.Size ?? 240;
        store.Save(roster);
        Graphics = new SharedPetGraphics(resources);
        tray = new NotifyIcon { Icon = DesktopAssets.Icon(),
            Text = "DesktopChao", Visible = !offscreen, ContextMenuStrip = menu };
        menu.Opening += (_, e) =>
        {
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
            menu.Items.Add(ChaoMenu());
            menu.Items.Add(FruitMenu(Screen.FromPoint(Cursor.Position), Cursor.Position.X));
            ToolStripMenuItem settings = new("Settings");
            settings.DropDownItems.Add(SizeMenu());
            menu.Items.Add(settings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open save folder", null, (_, _) => Guard(() => DesktopShell.OpenFolder(store.DirectoryPath)));
            menu.Items.Add("Quit", null, (_, _) => Quit());
            e.Cancel = false;
        };
        tray.DoubleClick += (_, _) => { foreach (PetForm form in forms.Values) SetVisible(form, true); };
        foreach (var item in saved) Open(item.state, item.entry);
        foreach (DesktopFruitState fruit in roster.Fruit) fruitWindows.Add(fruit.Id, new(fruit, this, offscreen));
        _ = dispatcher.Handle;
        frameTimer = new System.Threading.Timer(_ =>
        {
            if (Quitting) return;
            try { dispatcher.BeginInvoke((Action)UpdatePets); }
            catch (InvalidOperationException) when (Quitting || dispatcher.IsDisposed) { }
        }, null, 1, Timeout.Infinite);
    }
    private void UpdatePets()
    {
        if (Quitting) return;
        double now = clock.Elapsed.TotalSeconds;
        // One pending callback for the roster prevents competing render callbacks from starving menus/timers.
        // Resolve food supports before projecting candidates for the Chao's current surface.
        foreach (DesktopFruitWindow fruit in fruitWindows.Values) fruit.UpdateFruit();
        foreach (PetForm form in forms.Values) form.UpdatePet();
        if ((fruitSavePending || fruitLayoutDirty) && now >= nextFruitSave) { nextFruitSave = now + 15; SaveFruit(); }
        if (now >= nextSync) { nextSync = now + 2; _ = PollSa2(); }
        const double interval = 1.0 / 60;
        nextFrame += (Math.Floor(Math.Max(0, now - nextFrame) / interval) + 1) * interval;
        if (!Quitting) frameTimer.Change(Math.Clamp((int)Math.Ceiling((nextFrame - clock.Elapsed.TotalSeconds) * 1000), 1, 17), Timeout.Infinite);
    }

    private PetForm Open(DesktopChaoState state, PetRosterEntry entry)
    {
        state.Settings.Size = roster.Size ?? 240;
        PetForm form = new(state, store.PetStore(roster, state.Chao.Id), resources, host: this, offscreen: offscreen);
        forms.Add(state.Chao.Id, form);
        form.StartPet(entry.Visible);
        return form;
    }
    public PetForm AddChao(DesktopChaoState? imported = null)
    {
        DesktopChaoState state = imported ?? PetRosterStore.NewChao(forms.Values.Select(f => f.State.Random.Seed));
        state.Settings.Size = roster.Size ?? 240;
        PetRosterEntry entry = new() { Id = state.Chao.Id };
        store.PetStore(roster, entry.Id).Save(state);
        roster.Chao.Add(entry);
        try { store.Save(roster); }
        catch { roster.Chao.Remove(entry); throw; }
        return Open(state, entry);
    }
    public void SetVisible(PetForm form, bool visible)
    {
        PetRosterEntry entry = roster.Chao.Single(e => e.Id == form.State.Chao.Id);
        entry.Visible = visible;
        form.SetPetVisible(visible);
        SaveRoster();
    }
    public ToolStripMenuItem ChaoMenu()
    {
        ToolStripMenuItem root = new("Chao");
        foreach (PetForm form in forms.Values)
        {
            bool visible = roster.Chao.Single(e => e.Id == form.State.Chao.Id).Visible;
            ToolStripMenuItem chao = new(form.State.Chao.Name.Replace("&", "&&")) { Checked = visible };
            ToolStripMenuItem show = new("Visible on desktop") { Checked = visible };
            show.Click += (_, _) => SetVisible(form, !visible);
            chao.DropDownItems.Add(show);
            chao.DropDownItems.Add("Rename…", null, (_, _) => Rename(form));
            chao.DropDownItems.Add(form.TypeMenu());
            chao.DropDownItems.Add("Delete chao", null, (_, _) => DeleteChao(form));
            chao.DropDownItems.Add("Controls…", null, (_, _) => form.ShowControls());
            root.DropDownItems.Add(chao);
        }
        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add("Add chao", null, (_, _) => Guard(() => AddChao()));
        root.DropDownItems.Add("Import from .chao file…", null, (_, _) => ImportFile());
        ToolStripMenuItem process = new("Import one from SA2…") { Enabled = Sa2DesktopSync.IsAdministrator,
            ToolTipText = "Run DesktopChao as administrator and enter a Chao Garden in SA2." };
        process.Click += async (_, _) => await ImportFromProcess();
        root.DropDownItems.Add(process);
        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add(SyncMenu());
        return root;
    }
    public ToolStripMenuItem FruitMenu(Screen screen, int x)
    {
        ToolStripMenuItem root = new("Spawn fruit");
        foreach (var group in DesktopFruitCatalog.Available(resources).GroupBy(f => f.Group))
        {
            ToolStripMenuItem category = new(group.Key);
            foreach (var definition in group)
            {
                ToolStripMenuItem item = new(definition.Name) { Enabled = roster.Fruit.Count < 40 };
                item.Click += (_, _) => Guard(() => SpawnFruit(definition.FruitType, screen, x));
                category.DropDownItems.Add(item);
            }
            root.DropDownItems.Add(category);
        }
        root.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem clear = new("Clear uneaten fruit") { Enabled = roster.Fruit.Count > 0 };
        clear.Click += (_, _) => Guard(() => { foreach (Guid id in fruitWindows.Keys.ToArray()) RemoveFruit(id); });
        root.DropDownItems.Add(clear);
        return root;
    }
    public DesktopFruitState SpawnFruit(ChaoGarden.Core.Enums.FruitType type, Screen screen, int x)
    {
        if (roster.Fruit.Count >= 40) throw new InvalidOperationException("There are already 40 fruit on the desktop.");
        if (!DesktopFruitCatalog.Available(resources).Any(f => f.FruitType == type)) throw new ArgumentException("This fruit's model is unavailable.");
        DesktopFruitState fruit = new() { Type = type, Monitor = screen.DeviceName,
            Position = Math.Clamp((x - screen.WorkingArea.Left) / (double)screen.WorkingArea.Width, .03, .97) };
        DesktopFruitWindow window = new(fruit, this, offscreen);
        roster.Fruit.Add(fruit);
        try { store.Save(roster); }
        catch { roster.Fruit.Remove(fruit); window.Dispose(); throw; }
        fruitWindows.Add(fruit.Id, window);
        fruitSavePending = false;
        return fruit;
    }
    public void RemoveFruit(Guid id) => Guard(() =>
    {
        int index = roster.Fruit.FindIndex(f => f.Id == id);
        if (index < 0) return;
        DesktopFruitState fruit = roster.Fruit[index]; roster.Fruit.RemoveAt(index);
        try { store.Save(roster); }
        catch { roster.Fruit.Insert(index, fruit); throw; }
        if (fruitWindows.Remove(id, out DesktopFruitWindow? window)) window.Dispose();
        fruitSavePending = false;
    });
    public void ScheduleFruitSave() => fruitLayoutDirty = true;
    public void SaveFruit() => Guard(() => { fruitSavePending = true; store.Save(roster); fruitSavePending = fruitLayoutDirty = false; });
    public IReadOnlyList<DesktopFoodCandidate> FoodCandidates(Screen screen, double bodyWidth, WindowLedge? support = null)
    {
        List<DesktopFoodCandidate> candidates = [];
        foreach (DesktopFruitWindow window in fruitWindows.Values)
            if (FruitScreen(window.Fruit.Monitor).DeviceName == screen.DeviceName
                && window.Surface.TryProjectFood(window.Fruit, screen.WorkingArea, bodyWidth, support, out double position))
                candidates.Add(new(window.Fruit, position));
        return candidates;
    }
    public bool TryAcquireFruit(PetForm form, Guid id)
    {
        if (fruitSavePending) return false;
        DesktopFruitState? fruit = roster.Fruit.FirstOrDefault(f => f.Id == id);
        if (fruit == null || DesktopChaoSimulation.IsFeeding(form.State)
            || !fruitWindows.TryGetValue(id, out DesktopFruitWindow? fruitWindow)
            || fruitWindow.Surface.Dragging || fruitWindow.Surface.Falling) return false;
        try
        {
            // Persist the prepared handoff before removing the loose item. The live mutation is identical and consumes no RNG.
            DesktopChaoState next = ChaoGarden.ChaoPresentation.BoothJson.Clone(form.State);
            if (!DesktopChaoSimulation.Feed(next, fruit.Type, fruit.Bites, fruit.Id)) return false;
            store.PetStore(roster, form.State.Chao.Id).Save(form.SaveSnapshot(next));
            DesktopChaoSimulation.Feed(form.State, fruit.Type, fruit.Bites, fruit.Id);
            roster.Fruit.Remove(fruit);
            if (fruitWindows.Remove(id, out DesktopFruitWindow? window)) window.Dispose();
            SaveFruit();
            return true;
        }
        catch (Exception ex)
        {
            form.State.TargetFruitId = null;
            tray.ShowBalloonTip(5000, "Could not pick up fruit", ex.Message, ToolTipIcon.Warning);
            return false;
        }
    }
    public void SetSize(int pixels)
    {
        if (pixels is < 128 or > 480) throw new ArgumentOutOfRangeException(nameof(pixels));
        int? previous = roster.Size;
        roster.Size = pixels;
        try { store.Save(roster); }
        catch { roster.Size = previous; throw; }
        foreach (PetForm form in forms.Values) { form.State.Settings.Size = pixels; form.SaveNow(); }
    }
    public ToolStripMenuItem SizeMenu()
    {
        ToolStripMenuItem root = new("Size (all Chao)");
        foreach (int pixels in new[] { 160, 240, 320, 384 })
        {
            ToolStripMenuItem choice = new(pixels + " px") { Checked = roster.Size == pixels };
            choice.Click += (_, _) => Guard(() => SetSize(pixels));
            root.DropDownItems.Add(choice);
        }
        return root;
    }
    public void DeleteChao(PetForm form) => Guard(() =>
    {
        store.Remove(roster, form.State);
        forms.Remove(form.State.Chao.Id);
        // Dispose directly: ordinary window Close is deliberately mapped to Hide.
        form.Dispose();
    });
    public PetForm ImportChao(ChaoGarden.Core.Models.Entities.ChaoEntity source) =>
        AddChao(DesktopChaoImport.Copy(source, forms.Values.Select(f => f.State.Random.Seed)));
    private void ImportFile()
    {
        using OpenFileDialog dialog = new() { Title = "Import Chao", Filter = "SA2 Chao files (*.chao)|*.chao", CheckFileExists = true };
        if (dialog.ShowDialog() == DialogResult.OK) Guard(() => ImportChao(DesktopChaoImport.ReadFile(dialog.FileName)));
    }
    private bool importing;
    private async Task ImportFromProcess()
    {
        if (importing) return;
        importing = true;
        try
        {
            List<Sa2DesktopSnapshot> snapshots = await Task.Run(Sa2DesktopSync.Read);
            if (Quitting) return;
            if (snapshots.Count == 0) throw new InvalidOperationException("No hatched Chao found. Enter a Chao Garden in SA2 first.");
            using Form dialog = new() { Text = "Import one Chao from SA2", ClientSize = new Size(380, 310),
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
                MinimizeBox = false, MaximizeBox = false, TopMost = true };
            ListBox choices = new() { Left = 16, Top = 16, Width = 348, Height = 234 };
            foreach (Sa2DesktopSnapshot snapshot in snapshots)
                choices.Items.Add($"Slot {snapshot.Slot + 1} · {snapshot.Chao.Name} · {DesktopChaoTypes.Label(snapshot.Chao.ChaoType)}");
            choices.SelectedIndex = 0;
            Button ok = new() { Text = "Import", Left = 190, Top = 266, DialogResult = DialogResult.OK };
            Button cancel = new() { Text = "Cancel", Left = 280, Top = 266, DialogResult = DialogResult.Cancel };
            dialog.Controls.AddRange([choices, ok, cancel]); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
            if (dialog.ShowDialog() == DialogResult.OK && !Quitting && choices.SelectedIndex >= 0)
                ImportChao(snapshots[choices.SelectedIndex].Chao);
        }
        catch (Exception ex) { if (!Quitting) tray.ShowBalloonTip(5000, "Could not import Chao", ex.Message, ToolTipIcon.Warning); }
        finally { importing = false; }
    }
    public ToolStripMenuItem SyncMenu()
    {
        ToolStripMenuItem root = new("SA2 synchronization");
        bool admin = Sa2DesktopSync.IsAdministrator;
        ToolStripMenuItem enabled = new("Mirror Chao from SA2 (read only)") { Checked = roster.SyncSa2, Enabled = admin || roster.SyncSa2 };
        enabled.Click += async (_, _) =>
        {
            roster.SyncSa2 = !roster.SyncSa2;
            SaveRoster();
            if (roster.SyncSa2) await PollSa2();
            else SyncStatus = "SA2 synchronization off";
        };
        root.DropDownItems.Add(enabled);
        root.DropDownItems.Add(new ToolStripMenuItem(admin ? SyncStatus : "Requires running DesktopChao as administrator") { Enabled = false });
        root.DropDownItems.Add(new ToolStripMenuItem("Appearance/stats only · independent desktop behavior") { Enabled = false });
        return root;
    }
    private async Task PollSa2()
    {
        if (Quitting || polling || !roster.SyncSa2 || !Sa2DesktopSync.IsAdministrator) return;
        polling = true;
        try
        {
            var snapshots = await Task.Run(Sa2DesktopSync.Read);
            if (Quitting || !roster.SyncSa2) return;
            foreach (var snapshot in snapshots)
            {
                if (roster.ExcludedSa2Slots.Contains(snapshot.Slot)) continue;
                PetForm? form = forms.Values.FirstOrDefault(f => f.State.Sa2Slot == snapshot.Slot);
                if (form == null)
                {
                    DesktopChaoState state = PetRosterStore.NewChao(forms.Values.Select(f => f.State.Random.Seed));
                    state.Sa2Slot = snapshot.Slot;
                    Sa2DesktopSync.Apply(state, snapshot.Chao);
                    form = AddChao(state);
                }
                else Sa2DesktopSync.Apply(form.State, snapshot.Chao);
                form.UpdateName();
            }
            SyncStatus = $"Connected · {snapshots.Count} Chao · {DateTime.Now:T}";
        }
        catch (Exception ex)
        {
            SyncStatus = "Waiting for SA2: " + ex.Message;
            // A closed process, empty garden or failed read never removes or resets saved Chao.
        }
        finally { polling = false; }
    }
    public void Rename(PetForm form)
    {
        using Form dialog = new() { Text = "Rename Chao", ClientSize = new Size(320, 108),
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false, MaximizeBox = false, TopMost = true };
        TextBox name = new() { Text = form.State.Chao.Name, MaxLength = 32, Left = 16, Top = 16, Width = 288 };
        Button ok = new() { Text = "Save", Left = 144, Top = 62, DialogResult = DialogResult.OK };
        Button cancel = new() { Text = "Cancel", Left = 229, Top = 62, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange([name, ok, cancel]);
        dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => { name.SelectAll(); name.Focus(); };
        if (dialog.ShowDialog() == DialogResult.OK) Guard(() =>
        {
            PetRosterStore.Rename(form.State, name.Text);
            form.UpdateName(); form.SaveNow();
        });
    }
    private void SaveRoster() => Guard(() => store.Save(roster));
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { tray.ShowBalloonTip(5000, "DesktopChao", ex.Message, ToolTipIcon.Warning); }
    }
    public void Quit()
    {
        if (Quitting) return;
        Quitting = true;
        frameTimer.Change(Timeout.Infinite, Timeout.Infinite);
        foreach (PetForm form in forms.Values) form.Close();
        SaveRoster();
        ExitThread();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Quitting = true;
            frameTimer.Dispose();
            dispatcher.Dispose();
            foreach (PetForm form in forms.Values) form.Dispose();
            foreach (DesktopFruitWindow fruit in fruitWindows.Values) fruit.Dispose();
            tray.Visible = false; tray.Dispose(); menu.Dispose(); Graphics.Dispose();
        }
        base.Dispose(disposing);
    }
}
