using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Zfl.Core;

namespace Zfl.App;

/// Modlar listesindeki bir satır: bir mod klasörü (ya da henüz inmemiş bir Workshop öğesi).
public sealed class ModRow
{
    /// Sıra anahtarı: mod id'si, inmemişse "ws:<numara>".
    public required string Key { get; init; }
    public long WorkshopId { get; init; }
    public ModInfo? Info { get; init; }
    public required string Name { get; init; }
    public bool IsBase { get; init; }
    public bool Locked { get; init; }
    public bool Enabled { get; set; } = true;
    public bool CanToggle => !Locked;
    public int Position { get; set; }
    public string Note => Locked ? Loc.T("mods.required") : Info is null ? Loc.T("mods.notDownloaded") : IsBase ? Loc.T("mods.base") : Loc.T("mods.yours");
}

public partial class MainWindow : Window
{
    const string DefaultRoot = @"C:\Zomboid FPS";
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly ObservableCollection<ModRow> mods = [];
    LauncherState state = new();
    Feed feed = Feed.Builtin;
    bool feedOnline;
    string? steamGame;
    bool busy;

    public MainWindow()
    {
        InitializeComponent();
        ModList.ItemsSource = mods;
        AddBox.ToolTip = Loc.T("mods.hint");
    }

    InstallLayout? Layout => state.InstallRoot is { } r ? new InstallLayout(r) : null;
    string StatePath => LauncherState.DefaultPath;
    long TotalRam => SystemInfo.TotalRam();

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        state = LauncherState.Load(StatePath);
        if (state.MemoryMb == 0) state.MemoryMb = Memory.DefaultMb(TotalRam);
        steamGame = SteamLocator.FindGame();

        foreach (var lang in Loc.Languages)
        {
            var prev = Loc.Lang; Loc.Lang = lang;
            LangBox.Items.Add(new ComboBoxItem { Content = Loc.T("lang.name"), Tag = lang });
            Loc.Lang = prev;
        }
        LangBox.SelectedIndex = Array.IndexOf(Loc.Languages, Loc.Lang);
        FillGraphics();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            feed = Feed.Parse(await http.GetStringAsync(Feed.Url, cts.Token));
            feedOnline = true;
        }
        catch { feed = Feed.Builtin; feedOnline = false; }

        MemorySlider.Value = state.MemoryMb / 1024.0;
        MemoryHint.Text = Loc.F("set.memHint", TotalRam / (1L << 30));
        RefreshAll();
        await RefreshUpdateStateAsync();
    }

    // ---------- görünüm ----------

    void RefreshAll()
    {
        var l = Layout;
        SteamDirText.Text = steamGame ?? Loc.T("set.notFound");
        InstallRootText.Text = l?.Root ?? Loc.T("set.notInstalled");
        SavesText.Text = l?.ZomboidDir ?? "-";
        if (l is null || !File.Exists(Path.Combine(l.GameDir, "ProjectZomboid64.exe")))
        {
            SetupPanel.Visibility = Visibility.Visible;
            ReadyPanel.Visibility = Visibility.Collapsed;
            SteamGameText.Text = steamGame is null ? Loc.T("setup.noGame") : Loc.F("setup.steamGame", steamGame);
            if (InstallRootBox.Text.Length == 0) InstallRootBox.Text = state.InstallRoot ?? DefaultRoot;
            UpdateSpace();
        }
        else
        {
            SetupPanel.Visibility = Visibility.Collapsed;
            ReadyPanel.Visibility = Visibility.Visible;
            FillStatus(l);
        }
        FillMods();
        ReportBox.Text = l is null ? Loc.T("bug.needSetup") : BugReport.Collect(l, OrderedMods(l));
    }

    void FillStatus(InstallLayout l)
    {
        StatusGrid.Children.Clear();
        StatusGrid.RowDefinitions.Clear();
        var game = GameInfo.Version(l.ZomboidDir);
        var vp = ModVersion(Path.Combine(l.ModsDir, "Viewpoint"));
        var zb = BugReport.ZombieBuddyVersion(l);
        var userCount = state.UserMods.Count(id => !state.DisabledMods.Contains(id));
        string Have(string? v) => v is null ? Loc.T("st.missing") : Loc.T("st.installed");
        var rows = new (string, string, string, Brush)[]
        {
            ("", Loc.T("st.version"), Loc.T("st.state"), Res("Dim")),
            (Loc.T("st.game"), game ?? "-", game is null ? Loc.T("st.notRun") : game == feed.GameVersion ? Loc.T("st.ok") : Loc.F("st.for", feed.GameVersion), game is null || game == feed.GameVersion ? Res("Good") : Res("Warn")),
            ("Viewpoint", vp ?? "-", Have(vp), vp is null ? Res("Bad") : Res("Good")),
            ("ZombieBuddy", zb ?? "-", Have(zb), zb is null ? Res("Bad") : Res("Good")),
            (Loc.T("st.yourMods"), userCount.ToString(), feedOnline ? Loc.T("st.updatesOnPlay") : Loc.T("st.offline"), feedOnline ? Res("Dim") : Res("Warn")),
            (Loc.T("st.memory"), $"{state.MemoryMb / 1024} GB", "", Res("Dim")),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            StatusGrid.RowDefinitions.Add(new RowDefinition());
            var (a, b, c, color) = rows[i];
            var bg = i == 0 ? Res("Title") : i % 2 == 1 ? Res("Row") : Brushes.Transparent;
            for (int col = 0; col < 3; col++)
            {
                var text = new TextBlock { Text = col == 0 ? a : col == 1 ? b : c, FontSize = i == 0 ? 12 : 14 };
                if (i == 0 || col == 2) text.Foreground = i == 0 ? Res("Dim") : color;
                var cell = new Border { Background = bg, Padding = new Thickness(10, 5, 10, 5), Child = text };
                Grid.SetRow(cell, i); Grid.SetColumn(cell, col);
                StatusGrid.Children.Add(cell);
            }
        }
        var build = steamGame is null ? null : GameInfo.BuildId(steamGame);
        var changed = build is not null && state.LastBuildId is not null && build != state.LastBuildId;
        WarningText.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text = Loc.T("warn.gameUpdated");
    }

    void FillMods()
    {
        var selected = (ModList.SelectedItem as ModRow)?.Key;
        mods.Clear();
        var rows = OrderedRows(Layout);
        for (int i = 0; i < rows.Count; i++) { rows[i].Position = i + 1; mods.Add(rows[i]); }
        ModList.SelectedItem = mods.FirstOrDefault(r => r.Key == selected);
    }

    /// Temel + kullanıcı modlarının satırları, varsayılan sırada.
    List<ModRow> Rows(InstallLayout? l)
    {
        var rows = new List<ModRow>();
        void Add(long id, string fallbackName, bool isBase, bool locked)
        {
            var infos = l is null ? [] : FolderNames(l, id).Select(f => ModInfo.Read(Path.Combine(l.ModsDir, f))).OfType<ModInfo>().ToList();
            var enabled = locked || !state.DisabledMods.Contains(id);
            if (infos.Count == 0)
                rows.Add(new ModRow { Key = $"ws:{id}", WorkshopId = id, Name = fallbackName, IsBase = isBase, Locked = locked, Enabled = enabled });
            foreach (var info in infos)
                rows.Add(new ModRow { Key = info.Id, WorkshopId = id, Info = info, Name = infos.Count == 1 && isBase ? fallbackName : info.Name, IsBase = isBase, Locked = locked, Enabled = enabled });
        }
        foreach (var m in feed.BaseMods) Add(m.WorkshopId, m.Name, true, m.Locked);
        foreach (var id in state.UserMods) Add(id, Loc.F("mods.workshop", id), false, false);
        return rows.DistinctBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// Kullanıcının sırası, modların kendi kurallarına (require, loadModAfter...) uydurulmuş hâliyle.
    List<ModRow> OrderedRows(InstallLayout? l)
    {
        var rows = Rows(l);
        var byKey = rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        var ordered = LoadOrder.Merge(rows.Select(r => r.Key).ToList(), state.ModOrder).Select(k => byKey[k]).ToList();
        var sorted = new Queue<ModInfo>(LoadOrder.Sort(ordered.Where(r => r.Info is not null).Select(r => r.Info!).ToList()));
        return ordered.Select(r => r.Info is null ? r : byKey[sorted.Dequeue().Id]).ToList();
    }

    string UserModName(long id)
    {
        if (Layout is { } l && FolderNames(l, id).FirstOrDefault() is { } folder)
            return ModInfo.Read(Path.Combine(l.ModsDir, folder))?.Name ?? folder;
        return Loc.F("mods.workshop", id);
    }

    /// İlerleme satırında Workshop numarası yerine modun adı.
    string ModName(long id) =>
        feed.BaseMods.FirstOrDefault(m => m.WorkshopId == id)?.Name ?? (state.UserMods.Contains(id) ? UserModName(id) : id.ToString());

    Brush Res(string key) => (Brush)FindResource(key);

    void Status(string text)
    {
        StatusText.Text = text;
        if (text.Length > 0) Log.Write(text);
    }

    void ShowProgress(string? text)
    {
        ProgressPanel.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        ProgressText.Text = text ?? "";
    }

    // ---------- kurulum / oyna ----------

    void OnInstallRootChanged(object sender, TextChangedEventArgs e) => UpdateSpace();

    void UpdateSpace()
    {
        var root = InstallRootBox.Text.Trim();
        var err = InstallLayout.Validate(root);
        SetupError.Text = err ?? (steamGame is null ? Loc.T("setup.cantNoGame") : "");
        InstallButton.IsEnabled = err is null && steamGame is not null && !busy;
        if (err is not null || steamGame is null) { SpaceText.Text = ""; return; }
        var need = DirSize(steamGame) + (1L << 30);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
        var free = drive.IsReady ? drive.AvailableFreeSpace : 0;
        SpaceText.Text = Loc.F("setup.space", Gb(need), Gb(free));
        if (free < need) { SetupError.Text = Loc.T("setup.noSpace"); InstallButton.IsEnabled = false; }
    }

    static long dirSizeCache;
    static long DirSize(string dir) => dirSizeCache != 0 ? dirSizeCache
        : dirSizeCache = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    static string Gb(long b) => $"{b / (double)(1L << 30):0.0} GB";

    void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog { Title = Loc.T("setup.pickTitle") };
        if (d.ShowDialog(this) == true) InstallRootBox.Text = Path.Combine(d.FolderName, "Zomboid FPS");
    }

    async void OnInstall(object sender, RoutedEventArgs e)
    {
        state.InstallRoot = Path.GetFullPath(InstallRootBox.Text.Trim());
        state.Save(StatePath);
        if (await PrepareAsync()) { Status(Loc.T("setup.done")); RefreshAll(); await RefreshUpdateStateAsync(); }
    }

    /// null: denetlenmedi; true: güncelleme var; false: her şey güncel.
    bool? updateNeeded;

    /// Oyun ve modlar için güncelleme var mı; internet yoksa null.
    async Task<bool?> CheckUpdatesAsync(InstallLayout l)
    {
        if (steamGame is not null && GameInfo.BuildId(steamGame) is { } build && build != state.LastBuildId)
        {
            Log.Write($"update: game build {state.LastBuildId} -> {build}");
            return true;
        }
        if (OrderedRows(l).FirstOrDefault(r => r.Enabled && r.Info is null) is { } missing)
        {
            Log.Write($"update: not downloaded yet: {missing.Name}");
            return true;
        }
        var ids = feed.BaseMods.Select(m => m.WorkshopId).Concat(state.UserMods).ToList();
        if (Installer.Stray(l, KeepFolders(l, ids)) is [_, ..] stray)
        {
            Log.Write($"update: mods to remove: {string.Join(", ", stray)}");
            return true;
        }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var items = await WorkshopInfo.GetAsync(http, ids, cts.Token);
            var stale = items.Where(i => i.UpdatedUnix > state.ModUpdated.GetValueOrDefault(i.Id)).ToList();
            foreach (var i in stale) Log.Write($"update: mod {i.Title} ({i.Id}) changed on Workshop");
            return stale.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Error("update check", ex);
            return null;
        }
    }

    /// Butonu durumuna göre yazar: denetleniyor / Güncelle / Oyna.
    async Task RefreshUpdateStateAsync()
    {
        if (Layout is not { } l || !File.Exists(Path.Combine(l.GameDir, "ProjectZomboid64.exe"))) return;
        PlayButton.Content = Loc.T("checking");
        PlayButton.IsEnabled = false;
        updateNeeded = await CheckUpdatesAsync(l);
        if (updateNeeded is null) Status(Loc.T("s.noCheck"));
        PlayButton.Content = Loc.T(updateNeeded == true ? "update" : "play");
        PlayButton.IsEnabled = !busy;
    }

    async void OnPlay(object sender, RoutedEventArgs e)
    {
        var l = Layout!;
        // oynamadan önce her seferinde denetle: bu arada güncelleme gelmiş olabilir
        if (updateNeeded != true) await RefreshUpdateStateAsync();
        if (updateNeeded == true)
        {
            Log.Write("updating");
            if (await PrepareAsync()) Status(Loc.T("s.updated"));
            await RefreshUpdateStateAsync();
            return;
        }
        if (!WriteGameFiles(l)) return;
        await StartGameAsync(l);
    }

    /// İndirme yapmadan oyuna gidecek ayarları ve mod listesini yazar.
    bool WriteGameFiles(InstallLayout l)
    {
        try
        {
            var installer = new Installer(l, null!, new Progress<string>());
            installer.WriteConfig(state.MemoryMb);
            var ordered = OrderedMods(l);
            installer.WriteModList(ordered);
            PrepareGameSettings(l);
            var missing = Zfl.Core.ModList.Missing(ordered);
            if (missing.Count > 0) Status(Loc.F("s.missingReq", string.Join(", ", missing)));
            state.Save(StatePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("write game files", ex);
            MessageBox.Show(this, ex.Message, "Zomboid FPS Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    Installer NewInstaller(InstallLayout l, SteamCmd steam, IProgress<string> progress) =>
        new(l, steam, progress, SteamLocator.WorkshopDirs()) { Name = ModName };

    /// Oyunu eşitler, modları indirir/kurar, ayarı ve mod listesini yazar.
    async Task<bool> PrepareAsync()
    {
        var l = Layout!;
        SetBusy(true);
        var progress = new Progress<string>(s => ShowProgress(s));
        try
        {
            if (steamGame is not null)
            {
                ShowProgress(Loc.T("pr.compare"));
                var n = await GameMirror.SyncAsync(steamGame, l.GameDir, new Progress<string>(f => ShowProgress(Loc.F("pr.copy", f))), default);
                if (n > 0) Status(Loc.F("s.filesUpdated", n));
            }
            var steam = new SteamCmd(l.ToolsDir, new ProcessRunner(), http);
            ShowProgress(Loc.T("pr.steamcmd"));
            await steam.EnsureInstalledAsync(default);

            var installer = NewInstaller(l, steam, progress);
            var ids = feed.BaseMods.Select(m => m.WorkshopId).Concat(state.UserMods).ToList();
            try
            {
                // zamanlar indirmeden önce alınır: indirme sırasında gelen güncelleme bir dahaki denetimde yakalanır
                IReadOnlyList<WorkshopItem> times = [];
                try { times = await WorkshopInfo.GetAsync(http, ids, default); } catch (Exception ex) { Log.Error("workshop info", ex); }
                await installer.InstallModsAsync(ids, default);
                foreach (var t in times) state.ModUpdated[t.Id] = t.UpdatedUnix;
                foreach (var gone in Installer.RemoveStray(l, KeepFolders(l, ids))) Log.Write($"removed mod folder no longer in the list: {gone}");
            }
            catch (SteamCmdException ex) when (BaseModsPresent(l))
            {
                // internet yoksa elindekilerle devam
                Status(Loc.T("s.offlineStart"));
                Log.Error("mod download", ex);
            }

            ShowProgress(Loc.T("pr.config"));
            installer.WriteConfig(state.MemoryMb);
            var ordered = OrderedMods(l);
            var missing = Zfl.Core.ModList.Missing(ordered);
            installer.WriteModList(ordered);
            PrepareGameSettings(l);
            if (missing.Count > 0) Status(Loc.F("s.missingReq", string.Join(", ", missing)));

            if (steamGame is not null) state.LastBuildId = GameInfo.BuildId(steamGame);
            state.Save(StatePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("prepare", ex);
            ShowProgress(null);
            MessageBox.Show(this, ex.Message, "Zomboid FPS Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            SetBusy(false);
            ShowProgress(null);
            RefreshAll();
        }
    }

    /// Tıkla oyna: temel Java modlarını ZombieBuddy'ye önceden onaylatır, Viewpoint ilk kurulumunu doldurur.
    void PrepareGameSettings(InstallLayout l)
    {
        ZbApprovals.ApproveMods(l, OrderedRows(l).Where(r => r.IsBase && r.Info is { IsJava: true }).Select(r => r.Info!));
        // Viewpoint 12 GB altı kartlarda doku sıkıştırma istiyor; kurulum ekranı istense de baştan açık olsun
        if (ViewpointSetup.NeedsCompression(SystemInfo.VramMiB())) ViewpointSetup.EnableCompression(l);
        if (state.ViewpointSetupWanted)
        {
            if (ViewpointSetup.Finished(l)) state.ViewpointSetupWanted = false;
            return;
        }
        if (!ViewpointSetup.Finished(l)) ApplyGraphics(l);
    }

    void ApplyGraphics(InstallLayout l)
    {
        var vram = SystemInfo.VramMiB();
        ViewpointSetup.Apply(l, state.GraphicsPreset ?? ViewpointSetup.PresetFor(vram), ViewpointSetup.NeedsCompression(vram));
    }

    bool BaseModsPresent(InstallLayout l) =>
        feed.BaseMods.All(m => FolderNames(l, m.WorkshopId).Any(f => Directory.Exists(Path.Combine(l.ModsDir, f))));

    /// Listedeki modların klasörleri. Biri bile çözülemezse hepsi tutulur (yanlışlıkla silmemek için).
    static List<string> KeepFolders(InstallLayout l, IEnumerable<long> ids)
    {
        var per = ids.Select(id => FolderNames(l, id).ToList()).ToList();
        if (per.Any(f => f.Count == 0) && Directory.Exists(l.ModsDir))
            return Directory.GetDirectories(l.ModsDir).Select(d => Path.GetFileName(d)!).ToList();
        return per.SelectMany(f => f).ToList();
    }

    static readonly Lazy<IReadOnlyList<string>> SteamWorkshopDirs = new(SteamLocator.WorkshopDirs);

    /// Bir Workshop öğesinin içerdiği mod klasör adları (SteamCMD indirmesinden, yoksa Steam'in Atölye klasöründen).
    static IEnumerable<string> FolderNames(InstallLayout l, long id)
    {
        var steam = new SteamCmd(l.ToolsDir, new ProcessRunner(), new HttpClient());
        return new[] { steam.ItemDir(id) }.Concat(SteamWorkshopDirs.Value.Select(d => Path.Combine(d, id.ToString())))
            .Select(ModInfo.FindInWorkshopItem).FirstOrDefault(m => m.Count > 0, [])
            .Select(m => Path.GetFileName(m.Dir));
    }

    /// Oyuna yazılacak sıra: açık ve inmiş modlar, launcher'daki sırayla.
    IReadOnlyList<ModInfo> OrderedMods(InstallLayout l) =>
        LoadOrder.Sort(OrderedRows(l).Where(r => r.Enabled && r.Info is not null).Select(r => r.Info!).ToList());

    /// Mod listesini oyuna yazar; birlikte çalışmayan modları söyler.
    void SaveModList()
    {
        state.Save(StatePath);
        if (Layout is not { } l || !Directory.Exists(l.ModsDir)) return;
        var ordered = OrderedMods(l);
        new Installer(l, null!, new Progress<string>()).WriteModList(ordered);
        if (LoadOrder.Conflicts(ordered).FirstOrDefault() is { Mod: { } m } c)
            ModMessage.Text = Loc.F("mods.conflict", m.Name, ordered.FirstOrDefault(x => x.Id.Equals(c.Other, StringComparison.OrdinalIgnoreCase))?.Name ?? c.Other);
    }

    async Task StartGameAsync(InstallLayout l)
    {
        var exe = Path.Combine(l.GameDir, "ProjectZomboid64.exe");
        var started = DateTime.Now;
        using var p = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = l.GameDir, UseShellExecute = false })!;
        Status(Loc.T("s.started"));
        Log.Write($"game started: {exe}");
        WindowState = WindowState.Minimized;
        await p.WaitForExitAsync();
        Log.Write($"game closed, exit code {p.ExitCode}, ran {(DateTime.Now - started).TotalSeconds:0} s");
        WindowState = WindowState.Normal;
        if (DateTime.Now - started < TimeSpan.FromSeconds(20))
        {
            Status(Loc.T("s.quickExit"));
            var console = Path.Combine(l.ZomboidDir, "console.txt");
            Log.Write(File.Exists(console) ? "console.txt tail:\n" + string.Join("\n", Log.Tail(console, 40)) : "no console.txt: the game stopped before writing one");
            MessageBox.Show(this, Loc.T("msg.quickExit"), "Zomboid FPS Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            Tabs.SelectedItem = BugTab;
        }
        else Status("");
        RefreshAll();
        await RefreshUpdateStateAsync();
    }

    void SetBusy(bool b)
    {
        busy = b;
        PlayButton.IsEnabled = InstallButton.IsEnabled = AddButton.IsEnabled = LangBox.IsEnabled = !b;
    }

    // ---------- modlar ----------

    async void OnAddMod(object sender, RoutedEventArgs e)
    {
        ModMessage.Text = "";
        if (WorkshopInfo.ParseId(AddBox.Text) is not long id) { ModMessage.Text = Loc.T("mods.badLink"); return; }
        if (feed.BaseMods.Any(m => m.WorkshopId == id) || state.UserMods.Contains(id)) { ModMessage.Text = Loc.T("mods.dup"); return; }
        if (Layout is not { } l) { ModMessage.Text = Loc.T("mods.needSetup"); return; }

        SetBusy(true);
        try
        {
            var info = (await WorkshopInfo.GetAsync(http, [id], default)).FirstOrDefault();
            if (info is null) { ModMessage.Text = Loc.T("mods.notFound"); return; }
            ModMessage.Text = Loc.F("mods.downloading", info.Title, info.SizeBytes / (1 << 20));
            var steam = new SteamCmd(l.ToolsDir, new ProcessRunner(), http);
            await steam.EnsureInstalledAsync(default);
            var installed = await NewInstaller(l, steam, new Progress<string>(_ => { })).InstallModsAsync([id], default);
            state.UserMods.Add(id);
            state.Save(StatePath);

            var notes = new List<string> { Loc.F("mods.added", info.Title) };
            var game = GameInfo.Version(l.ZomboidDir) ?? feed.GameVersion;
            if (installed.Any(m => !ModInfo.SupportsGame(m, game))) notes.Add(Loc.F("mods.wrongVer", game));
            if (installed.Any(m => m.IsJava)) notes.Add(Loc.T("mods.java"));
            var missing = Zfl.Core.ModList.Missing(OrderedMods(l));
            if (missing.Count > 0) notes.Add(Loc.F("mods.needs", string.Join(", ", missing)));
            SaveModList();
            ModMessage.Text = string.Join(" ", notes);
            AddBox.Text = "";
        }
        catch (Exception ex) { ModMessage.Text = ex.Message; }
        finally { SetBusy(false); RefreshAll(); }
    }

    void OnRemoveMod(object sender, RoutedEventArgs e)
    {
        if (ModList.SelectedItem is not ModRow row) { ModMessage.Text = Loc.T("mods.pick"); return; }
        if (row.IsBase) { ModMessage.Text = Loc.T("mods.baseNoRemove"); return; }
        if (Layout is { } l)
        {
            foreach (var f in FolderNames(l, row.WorkshopId))
            {
                var dir = Path.Combine(l.ModsDir, f);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
        state.UserMods.Remove(row.WorkshopId);
        state.DisabledMods.Remove(row.WorkshopId);
        ModMessage.Text = Loc.F("mods.removed", row.Name);
        SaveModList();
        RefreshAll();
    }

    void OnToggleMod(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ModRow row || row.Locked) return;
        if (row.Enabled) state.DisabledMods.Remove(row.WorkshopId);
        else if (!state.DisabledMods.Contains(row.WorkshopId)) state.DisabledMods.Add(row.WorkshopId);
        ModMessage.Text = Loc.F(row.Enabled ? "mods.on" : "mods.off", row.Name);
        SaveModList();
        FillMods();
    }

    void OnMoveUp(object sender, RoutedEventArgs e) => Move(-1);
    void OnMoveDown(object sender, RoutedEventArgs e) => Move(+1);

    void Move(int step)
    {
        if (ModList.SelectedItem is not ModRow row) { ModMessage.Text = Loc.T("mods.pick"); return; }
        var keys = OrderedRows(Layout).Select(r => r.Key).ToList();
        var i = keys.IndexOf(row.Key);
        var j = i + step;
        if (i < 0 || j < 0 || j >= keys.Count) return;
        (keys[i], keys[j]) = (keys[j], keys[i]);
        state.ModOrder = keys;
        var after = OrderedRows(Layout).FindIndex(r => r.Key == row.Key);
        ModMessage.Text = after == i ? Loc.F("mods.cantMove", row.Name) : "";
        SaveModList();
        FillMods();
    }

    void OnResetOrder(object sender, RoutedEventArgs e)
    {
        state.ModOrder.Clear();
        ModMessage.Text = Loc.T("mods.orderReset");
        SaveModList();
        FillMods();
    }

    // ---------- ayarlar / hata ----------

    void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LangBox.SelectedItem is not ComboBoxItem { Tag: string lang } || lang == Loc.Lang) return;
        Loc.Lang = state.Language = lang;
        state.Save(StatePath);
        // metinler pencere kurulurken okunuyor; yeni dilde aynı yerde yeni pencere aç
        var next = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = Left, Top = Top };
        Application.Current.MainWindow = next;
        next.Show();
        next.Tabs.SelectedIndex = Tabs.SelectedIndex;
        Close();
    }

    void OnMemoryChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var gb = (int)MemorySlider.Value;
        MemoryText.Text = $"{gb} GB";
        if (!IsLoaded || state.MemoryMb == gb * 1024) return;
        state.MemoryMb = gb * 1024;
        state.Save(StatePath);
        if (Layout is { } l && File.Exists(Path.Combine(l.GameDir, "ProjectZomboid64.json")))
            new Installer(l, null!, new Progress<string>()).WriteConfig(state.MemoryMb);
        Status(Loc.T("s.memSaved"));
    }

    void FillGraphics()
    {
        GraphicsBox.Items.Clear();
        GraphicsBox.Items.Add(new ComboBoxItem { Content = Loc.F("set.gfxAuto", ViewpointSetup.PresetFor(SystemInfo.VramMiB())), Tag = "" });
        foreach (var p in ViewpointSetup.Presets) GraphicsBox.Items.Add(new ComboBoxItem { Content = p, Tag = p });
        GraphicsBox.SelectedIndex = state.GraphicsPreset is { } g ? Array.IndexOf(ViewpointSetup.Presets, g) + 1 : 0;
    }

    void OnGraphicsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || GraphicsBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        var preset = tag.Length == 0 ? null : tag;
        if (preset == state.GraphicsPreset) return;
        state.GraphicsPreset = preset;
        state.ViewpointSetupWanted = false;
        state.Save(StatePath);
        if (Layout is { } l && Directory.Exists(l.ZomboidDir)) ApplyGraphics(l);
        Status(Loc.T("s.gfxSaved"));
    }

    void OnViewpointSetupAgain(object sender, RoutedEventArgs e)
    {
        if (Layout is { } l) ViewpointSetup.Reset(l);
        state.ViewpointSetupWanted = true;
        state.Save(StatePath);
        Status(Loc.T("s.vpSetupAgain"));
    }

    void OnOpenInstall(object sender, RoutedEventArgs e) { if (Layout is { } l) OpenFolder(l.Root); }
    void OnOpenLogs(object sender, RoutedEventArgs e) { if (Layout is { } l) OpenFolder(l.ZomboidDir); }
    static void OpenFolder(string dir) { if (Directory.Exists(dir)) Process.Start("explorer.exe", $"\"{dir}\""); }

    void OnCopyReport(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ReportBox.Text); Status(Loc.T("s.copied")); }
        catch { ReportBox.SelectAll(); ReportBox.Focus(); Status(Loc.T("s.copyFail")); }
    }

    static string? ModVersion(string modFolder)
    {
        var f = ModInfo.InfoFiles(modFolder).FirstOrDefault(File.Exists);
        if (f is null) return null;
        var line = File.ReadLines(f).FirstOrDefault(x => x.StartsWith("modversion=", StringComparison.OrdinalIgnoreCase));
        return line?.Split('=', 2)[1].Trim() ?? "?";
    }

    // ---------- pencere ----------

    void OnDrag(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    void OnClose(object sender, RoutedEventArgs e) => Close();
    void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// Çerçevesiz pencere küçültülüp Alt+Tab ile dönünce Windows onu bazen -32000'de bırakıyor; ekrana geri al.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (Left < SystemParameters.VirtualScreenLeft - Width / 2 || Top < SystemParameters.VirtualScreenTop - 10)
        {
            Left = (SystemParameters.WorkArea.Width - Width) / 2;
            Top = (SystemParameters.WorkArea.Height - Height) / 2;
        }
    }

    internal static string? ModVersionOf(string folder) => ModVersion(folder);
}
