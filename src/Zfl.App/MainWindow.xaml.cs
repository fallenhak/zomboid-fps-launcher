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

public sealed class ModRow
{
    public long WorkshopId { get; init; }
    public required string Name { get; set; }
    public bool IsBase { get; init; }
    public bool Enabled { get; set; } = true;
    public bool CanToggle => !IsBase;
    public string Note { get; set; } = "";
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
    }

    InstallLayout? Layout => state.InstallRoot is { } r ? new InstallLayout(r) : null;
    string StatePath => LauncherState.DefaultPath;
    long TotalRam => SystemInfo.TotalRam();

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        state = LauncherState.Load(StatePath);
        if (state.MemoryMb == 0) state.MemoryMb = Memory.DefaultMb(TotalRam);
        steamGame = SteamLocator.FindGame();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            feed = Feed.Parse(await http.GetStringAsync(Feed.Url, cts.Token));
            feedOnline = true;
        }
        catch { feed = Feed.Builtin; feedOnline = false; }

        MemorySlider.Value = state.MemoryMb / 1024.0;
        MemoryHint.Text = $"Bilgisayarda {TotalRam / (1L << 30)} GB var. En az 4 GB.";
        RefreshAll();
    }

    // ---------- görünüm ----------

    void RefreshAll()
    {
        var l = Layout;
        SteamDirText.Text = steamGame ?? "bulunamadı";
        InstallRootText.Text = l?.Root ?? "henüz kurulmadı";
        SavesText.Text = l?.ZomboidDir ?? "-";
        if (l is null || !File.Exists(Path.Combine(l.GameDir, "ProjectZomboid64.exe")))
        {
            SetupPanel.Visibility = Visibility.Visible;
            ReadyPanel.Visibility = Visibility.Collapsed;
            SteamGameText.Text = steamGame is null
                ? "Steam'de Project Zomboid bulunamadı. Oyun yüklü değilse önce Steam'den yükle, sonra launcher'ı yeniden aç."
                : $"Steam'deki oyun: {steamGame}";
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
        ReportBox.Text = l is null ? "Önce kurulumu tamamla." : BugReport.Collect(l, OrderedMods(l));
    }

    void FillStatus(InstallLayout l)
    {
        StatusGrid.Children.Clear();
        StatusGrid.RowDefinitions.Clear();
        var game = GameInfo.Version(l.ZomboidDir);
        var vp = ModVersion(Path.Combine(l.ModsDir, "Viewpoint"));
        var zb = BugReport.ZombieBuddyVersion(l);
        var userCount = state.UserMods.Count(id => !state.DisabledMods.Contains(id));
        var rows = new (string, string, string, Brush)[]
        {
            ("", "Sürüm", "Durum", Res("Dim")),
            ("Oyun", game ?? "-", game is null ? "henüz açılmadı" : game == feed.GameVersion ? "✔ destekleniyor" : $"Viewpoint {feed.GameVersion} için", game is null || game == feed.GameVersion ? Res("Good") : Res("Warn")),
            ("Viewpoint", vp ?? "-", vp is null ? "kurulu değil" : "✔ kurulu", vp is null ? Res("Bad") : Res("Good")),
            ("ZombieBuddy", zb ?? "-", zb is null ? "kurulu değil" : "✔ kurulu", zb is null ? Res("Bad") : Res("Good")),
            ("Senin modların", userCount.ToString(), feedOnline ? "Oyna'da güncellenir" : "güncellemeler kontrol edilemedi", feedOnline ? Res("Dim") : Res("Warn")),
            ("Bellek", $"{state.MemoryMb / 1024} GB", "", Res("Dim")),
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
        WarningText.Text = "Oyun Steam'de güncellendi. Viewpoint yeni sürümü henüz desteklemiyor olabilir. Yine de oynayabilirsin; sorun çıkarsa Viewpoint'in güncellenmesini bekle.";
    }

    void FillMods()
    {
        mods.Clear();
        foreach (var m in feed.BaseMods)
            mods.Add(new ModRow { WorkshopId = m.WorkshopId, Name = m.Name, IsBase = true, Note = "temel" });
        foreach (var id in state.UserMods)
            mods.Add(new ModRow { WorkshopId = id, Name = UserModName(id), Enabled = !state.DisabledMods.Contains(id) });
    }

    string UserModName(long id)
    {
        if (Layout is { } l && FolderNames(l, id).FirstOrDefault() is { } folder)
            return ModInfo.Read(Path.Combine(l.ModsDir, folder))?.Name ?? folder;
        return $"Workshop {id}";
    }

    Brush Res(string key) => (Brush)FindResource(key);

    void Status(string text) => StatusText.Text = text;

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
        SetupError.Text = err ?? (steamGame is null ? "Steam'de oyun bulunamadığı için kurulum yapılamaz." : "");
        InstallButton.IsEnabled = err is null && steamGame is not null && !busy;
        if (err is not null || steamGame is null) { SpaceText.Text = ""; return; }
        var need = DirSize(steamGame) + (1L << 30);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
        var free = drive.IsReady ? drive.AvailableFreeSpace : 0;
        SpaceText.Text = $"Gereken alan: {Gb(need)}. Boş alan: {Gb(free)}.";
        if (free < need) { SetupError.Text = "Bu sürücüde yeterli boş alan yok. Başka bir klasör seç ya da yer aç."; InstallButton.IsEnabled = false; }
    }

    static long dirSizeCache;
    static long DirSize(string dir) => dirSizeCache != 0 ? dirSizeCache
        : dirSizeCache = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    static string Gb(long b) => $"{b / (double)(1L << 30):0.0} GB";

    void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog { Title = "Kurulum klasörünü seç" };
        if (d.ShowDialog(this) == true) InstallRootBox.Text = Path.Combine(d.FolderName, "Zomboid FPS");
    }

    async void OnInstall(object sender, RoutedEventArgs e)
    {
        state.InstallRoot = Path.GetFullPath(InstallRootBox.Text.Trim());
        state.Save(StatePath);
        if (await PrepareAsync()) { Status("Kurulum tamamlandı"); RefreshAll(); }
    }

    async void OnPlay(object sender, RoutedEventArgs e)
    {
        if (!await PrepareAsync()) return;
        await StartGameAsync(Layout!);
    }

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
                ShowProgress("Oyun dosyaları karşılaştırılıyor...");
                var n = await GameMirror.SyncAsync(steamGame, l.GameDir, new Progress<string>(f => ShowProgress($"Kopyalanıyor: {f}")), default);
                if (n > 0) Status($"{n} oyun dosyası güncellendi");
            }
            var steam = new SteamCmd(l.ToolsDir, new ProcessRunner(), http);
            ShowProgress("SteamCMD hazırlanıyor...");
            await steam.EnsureInstalledAsync(default);

            var installer = new Installer(l, steam, progress);
            var ids = feed.BaseMods.Select(m => m.WorkshopId).Concat(state.UserMods).ToList();
            try
            {
                await installer.InstallModsAsync(ids, default);
            }
            catch (SteamCmdException ex) when (BaseModsPresent(l))
            {
                // internet yoksa elindekilerle devam
                Status("Güncellemeler kontrol edilemedi, son hâliyle açılıyor");
                Debug.WriteLine(ex);
            }

            ShowProgress("Ayarlar yazılıyor...");
            installer.WriteConfig(state.MemoryMb);
            var ordered = OrderedMods(l);
            var missing = Zfl.Core.ModList.Missing(ordered);
            installer.WriteModList(ordered);
            if (missing.Count > 0) Status($"Eksik gerekli modlar: {string.Join(", ", missing)}");

            if (steamGame is not null) state.LastBuildId ??= GameInfo.BuildId(steamGame);
            state.Save(StatePath);
            return true;
        }
        catch (Exception ex)
        {
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

    bool BaseModsPresent(InstallLayout l) =>
        feed.BaseMods.All(m => FolderNames(l, m.WorkshopId).Any(f => Directory.Exists(Path.Combine(l.ModsDir, f))));

    /// Bir Workshop öğesinin içerdiği mod klasör adları (SteamCMD indirme klasöründen).
    static IEnumerable<string> FolderNames(InstallLayout l, long id)
    {
        var steam = new SteamCmd(l.ToolsDir, new ProcessRunner(), new HttpClient());
        return ModInfo.FindInWorkshopItem(steam.ItemDir(id)).Select(m => Path.GetFileName(m.Dir));
    }

    /// Temel paket (feed sırası) + açık kullanıcı modları (eklenme sırası).
    IReadOnlyList<ModInfo> OrderedMods(InstallLayout l)
    {
        var ids = feed.BaseMods.Select(m => m.WorkshopId).Concat(state.UserMods.Where(id => !state.DisabledMods.Contains(id)));
        return ids.SelectMany(id => FolderNames(l, id))
                  .Select(f => ModInfo.Read(Path.Combine(l.ModsDir, f)))
                  .OfType<ModInfo>().ToList();
    }

    async Task StartGameAsync(InstallLayout l)
    {
        var exe = Path.Combine(l.GameDir, "ProjectZomboid64.exe");
        var started = DateTime.Now;
        using var p = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = l.GameDir, UseShellExecute = false })!;
        Status("Oyun açıldı");
        WindowState = WindowState.Minimized;
        await p.WaitForExitAsync();
        WindowState = WindowState.Normal;
        if (DateTime.Now - started < TimeSpan.FromSeconds(20))
        {
            Status("Oyun hemen kapandı");
            MessageBox.Show(this, "Oyun açıldıktan hemen sonra kapandı. Hata bildir sekmesindeki bilgileri ve console.txt dosyasını Viewpoint ekibine gönderebilirsin.",
                "Zomboid FPS Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            Tabs.SelectedItem = BugTab;
        }
        else Status("");
        if (steamGame is not null) { state.LastBuildId = GameInfo.BuildId(steamGame); state.Save(StatePath); }
        RefreshAll();
    }

    void SetBusy(bool b)
    {
        busy = b;
        PlayButton.IsEnabled = InstallButton.IsEnabled = AddButton.IsEnabled = !b;
    }

    // ---------- modlar ----------

    async void OnAddMod(object sender, RoutedEventArgs e)
    {
        ModMessage.Text = "";
        if (WorkshopInfo.ParseId(AddBox.Text) is not long id) { ModMessage.Text = "Bağlantıda Workshop numarası bulunamadı. Mod sayfasının adresini kopyalayıp yapıştır."; return; }
        if (feed.BaseMods.Any(m => m.WorkshopId == id) || state.UserMods.Contains(id)) { ModMessage.Text = "Bu mod zaten listede."; return; }
        if (Layout is not { } l) { ModMessage.Text = "Önce Oyna sekmesinden kurulumu tamamla."; return; }

        SetBusy(true);
        try
        {
            var info = (await WorkshopInfo.GetAsync(http, [id], default)).FirstOrDefault();
            if (info is null) { ModMessage.Text = "Bu numarada bir Project Zomboid modu bulunamadı."; return; }
            ModMessage.Text = $"İndiriliyor: {info.Title} ({info.SizeBytes / (1 << 20)} MB)";
            var steam = new SteamCmd(l.ToolsDir, new ProcessRunner(), http);
            await steam.EnsureInstalledAsync(default);
            var installed = await new Installer(l, steam, new Progress<string>(_ => { })).InstallModsAsync([id], default);
            state.UserMods.Add(id);
            state.Save(StatePath);

            var notes = new List<string>();
            var game = GameInfo.Version(l.ZomboidDir) ?? feed.GameVersion;
            if (installed.Any(m => !ModInfo.SupportsGame(m, game))) notes.Add($"Bu mod {game} için yapılmamış, sorun çıkarabilir.");
            if (installed.Any(m => m.IsJava)) notes.Add("Java modu: bilgisayarında tam erişimle çalışır, yalnız güvendiğin modları ekle. Oyunda ZombieBuddy onay soracak.");
            var missing = Zfl.Core.ModList.Missing(OrderedMods(l));
            if (missing.Count > 0) notes.Add($"Çalışması için şu modlar da gerekli: {string.Join(", ", missing)}. Workshop sayfasındaki \"Required items\" bölümüne bak.");
            new Installer(l, steam, new Progress<string>()).WriteModList(OrderedMods(l));
            ModMessage.Text = notes.Count == 0 ? $"{info.Title} eklendi." : $"{info.Title} eklendi. " + string.Join(" ", notes);
            AddBox.Text = "";
        }
        catch (Exception ex) { ModMessage.Text = ex.Message; }
        finally { SetBusy(false); RefreshAll(); }
    }

    void OnRemoveMod(object sender, RoutedEventArgs e)
    {
        if (ModList.SelectedItem is not ModRow row) { ModMessage.Text = "Önce listeden bir mod seç."; return; }
        if (row.IsBase) { ModMessage.Text = "Temel modlar kaldırılamaz."; return; }
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
        state.Save(StatePath);
        if (Layout is { } l2) new Installer(l2, null!, new Progress<string>()).WriteModList(OrderedMods(l2));
        ModMessage.Text = $"{row.Name} kaldırıldı.";
        RefreshAll();
    }

    void OnToggleMod(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ModRow row || row.IsBase) return;
        if (row.Enabled) state.DisabledMods.Remove(row.WorkshopId);
        else if (!state.DisabledMods.Contains(row.WorkshopId)) state.DisabledMods.Add(row.WorkshopId);
        state.Save(StatePath);
        if (Layout is { } l) new Installer(l, null!, new Progress<string>()).WriteModList(OrderedMods(l));
        ModMessage.Text = row.Enabled ? $"{row.Name} açıldı." : $"{row.Name} kapatıldı.";
    }

    // ---------- ayarlar / hata ----------

    void OnMemoryChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var gb = (int)MemorySlider.Value;
        MemoryText.Text = $"{gb} GB";
        if (!IsLoaded || state.MemoryMb == gb * 1024) return;
        state.MemoryMb = gb * 1024;
        state.Save(StatePath);
        if (Layout is { } l && File.Exists(Path.Combine(l.GameDir, "ProjectZomboid64.json")))
            new Installer(l, null!, new Progress<string>()).WriteConfig(state.MemoryMb);
        Status("Bellek ayarı kaydedildi");
    }

    void OnOpenInstall(object sender, RoutedEventArgs e) { if (Layout is { } l) OpenFolder(l.Root); }
    void OnOpenLogs(object sender, RoutedEventArgs e) { if (Layout is { } l) OpenFolder(l.ZomboidDir); }
    static void OpenFolder(string dir) { if (Directory.Exists(dir)) Process.Start("explorer.exe", $"\"{dir}\""); }

    void OnCopyReport(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ReportBox.Text); Status("Kopyalandı"); }
        catch { ReportBox.SelectAll(); ReportBox.Focus(); Status("Kopyalanamadı, metni seçip Ctrl+C ile kopyala"); }
    }

    static string? ModVersion(string modFolder)
    {
        foreach (var sub in new[] { "42", "common", "" })
        {
            var f = Path.Combine(modFolder, sub, "mod.info");
            if (!File.Exists(f)) continue;
            var line = File.ReadLines(f).FirstOrDefault(x => x.StartsWith("modversion=", StringComparison.OrdinalIgnoreCase));
            return line?.Split('=', 2)[1].Trim() ?? "?";
        }
        return null;
    }

    // ---------- pencere ----------

    void OnDrag(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    void OnClose(object sender, RoutedEventArgs e) => Close();
    void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    internal static string? ModVersionOf(string folder) => ModVersion(folder);
}
