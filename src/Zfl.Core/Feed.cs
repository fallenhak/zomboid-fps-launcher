using System.Text.Json;
namespace Zfl.Core;
/// Locked: herkeste açık kalır, kapatılamaz (ZombieBuddy, Viewpoint).
public sealed record FeedMod(long WorkshopId, string Name, bool Locked = false);
public sealed record Feed(string GameVersion, IReadOnlyList<FeedMod> BaseMods, string LauncherVersion, string? LauncherUrl) {
  static readonly JsonSerializerOptions O = new() { PropertyNameCaseInsensitive = true };
  public static Feed Parse(string json) => JsonSerializer.Deserialize<Feed>(json, O) ?? throw new InvalidDataException("feed boş");
  public const string Url = "https://raw.githubusercontent.com/fallenhak/zomboid-fps-launcher/main/feed.json";
  /// İnternet yokken: repodaki feed.json'un derlemeye gömülü kopyası.
  public static Feed Builtin { get; } = Parse(new StreamReader(typeof(Feed).Assembly.GetManifestResourceStream("feed.json")!).ReadToEnd());
}
public sealed class LauncherState {
  public string? InstallRoot { get; set; }
  public int MemoryMb { get; set; }
  public List<long> UserMods { get; set; } = [];
  public List<long> DisabledMods { get; set; } = [];
  public string? LastGameVersion { get; set; }
  public string? LastBuildId { get; set; }
  public string? Language { get; set; }
  /// Viewpoint grafik kalitesi; null = ekran kartına göre otomatik.
  public string? GraphicsPreset { get; set; }
  /// Oyuncu Viewpoint ilk kurulum ekranını tekrar görmek istedi; o ekran bitene kadar launcher ayar yazmaz.
  public bool ViewpointSetupWanted { get; set; }
  /// Kurulu Workshop öğelerinin Steam'deki güncellenme zamanı (unix); güncelleme denetimi buna bakar.
  public Dictionary<long, long> ModUpdated { get; set; } = [];
  /// Oyun Steam'e bağlı açılsın (sunuculara girmek ve Shift+Tab için).
  public bool SteamMode { get; set; } = true;
  /// Takılmaları azaltan Viewpoint ayarları.
  public bool Smooth { get; set; } = true;
  /// Viewpoint'in FPS göstergesi açık gelsin.
  public bool ShowFps { get; set; } = true;
  /// Oyun -debug ile açılsın (geliştirici menüleri).
  public bool DebugMode { get; set; }
  /// Kullanıcının mod sırası (mod id'leri; henüz inmemiş Workshop öğeleri için "ws:<numara>").
  public List<string> ModOrder { get; set; } = [];
  static readonly JsonSerializerOptions O = new() { WriteIndented = true };
  public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZomboidFpsLauncher", "state.json");
  public static LauncherState Load(string path) {
    try { return JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(path)) ?? new(); } catch { return new(); }
  }
  public void Save(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(this, O)); }
}
public sealed class Installer(InstallLayout l, SteamCmd steam, IProgress<string> p, IEnumerable<string>? fallbackDirs = null) {
  public Func<long, string> Name { get; init; } = id => id.ToString();
  /// İndirilecek boyut (bayt); yüzde bundan hesaplanır. Bilinmiyorsa 0.
  public Func<long, long> Size { get; init; } = _ => 0;
  /// 0–1 arası ilerleme; null = ne kadar süreceği belli değil.
  public IProgress<double?>? Percent { get; init; }
  public async Task<IReadOnlyList<ModInfo>> InstallModsAsync(IEnumerable<long> ids, CancellationToken ct) {
    var list = ids.ToList();
    var items = await DownloadAllAsync(list, ct);
    var result = new List<ModInfo>();
    foreach (var id in list) {
      var item = items[id];
      if (id == 3619862853) PlaceZombieBuddy(item);
      var mods = ModInfo.FindInWorkshopItem(item);
      var files = mods.Sum(m => Directory.EnumerateFiles(m.Dir, "*", SearchOption.AllDirectories).Count());
      int copied = 0;
      p.Report(Loc.F("pr.install", Name(id)));
      // kopyalama arka planda: büyük paketlerde pencere donmasın
      result.AddRange(await Task.Run(() => mods.Select(m => {
        var dst = Path.Combine(l.ModsDir, Path.GetFileName(m.Dir));
        // yarıda kalırsa işaret dosyası kalır, mod "kurulu" sayılmaz
        var mark = Unfinished(l, Path.GetFileName(m.Dir));
        Directory.CreateDirectory(l.ModsDir); File.WriteAllText(mark, "");
        SyncDir(m.Dir, dst, () => Percent?.Report(files == 0 ? null : (double)Interlocked.Increment(ref copied) / files));
        File.Delete(mark);
        return ModInfo.Read(dst)!;
      }).ToList(), ct));
    }
    return result;
  }
  public static string Unfinished(InstallLayout l, string folder) => Path.Combine(l.ModsDir, folder + ".kuruluyor");
  /// Hepsi tek SteamCMD oturumunda iner; bu arada indirme klasörünün dolmasına bakıp yüzde gösterilir.
  async Task<Dictionary<long, string>> DownloadAllAsync(List<long> ids, CancellationToken ct) {
    var result = new Dictionary<long, string>();
    if (ids.Count == 0) return result;
    p.Report(ids.Count == 1 ? Loc.F("pr.download", Name(ids[0])) : Loc.F("pr.downloadMany", ids.Count));
    long total = ids.Sum(Size);
    var start = DateTime.Now;
    var run = steam.DownloadManyAsync(ids, ct);
    var seen = new HashSet<long>(); var finished = new HashSet<long>();
    while (await Task.WhenAny(run, Task.Delay(500, ct)) != run) {
      if (total <= 0) { Percent?.Report(null); continue; }
      long got = 0;
      foreach (var id in ids) {
        var st = steam.StagingDir(id);
        if (Directory.Exists(st)) { seen.Add(id); got += DirSize(st); }
        else if (seen.Contains(id)) { finished.Add(id); got += Size(id); }
      }
      var frac = Math.Min(1, (double)got / total);
      Percent?.Report(frac);
      var secs = (DateTime.Now - start).TotalSeconds;
      p.Report(Loc.F("pr.downloadMb", got / 1048576, total / 1048576, secs > 1 ? got / 1048576.0 / secs : 0));
    }
    var (done, output) = await run;
    foreach (var id in ids) {
      if (done.Contains(id)) { result[id] = steam.ItemDir(id); continue; }
      result[id] = LocalCopy(id) ?? throw new SteamCmdException(Loc.F("err.download", Name(id)) + $"\n{output.Split('\n').LastOrDefault(x => x.Trim().Length > 0)}");
    }
    return result;
  }
  static long DirSize(string d) {
    try { return Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }); }
    catch { return 0; }
  }
  /// Atölye'den inmezse (internet yok ya da mod Atölye'den kalkmış) eldeki kopya:
  /// önce SteamCMD'nin önceki indirmesi, sonra verilen klasörler (Steam'in kendi Atölye klasörü gibi).
  string? LocalCopy(long id) =>
    new[] { steam.ItemDir(id) }.Concat((fallbackDirs ?? []).Select(d => Path.Combine(d, id.ToString())))
      .FirstOrDefault(d => ModInfo.FindInWorkshopItem(d).Count > 0);
  /// Mod klasörü launcher'ındır: listede olmayan klasörler (temel listeden çıkan modlar gibi) silinir.
  public static IReadOnlyList<string> Stray(InstallLayout l, IEnumerable<string> keepFolders) {
    if (!Directory.Exists(l.ModsDir)) return [];
    var keep = keepFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
    keep.Add("examplemod"); // oyun her açılışta kendisi koyuyor
    return Directory.GetDirectories(l.ModsDir).Select(d => Path.GetFileName(d)!).Where(n => !keep.Contains(n)).ToList();
  }
  public static IReadOnlyList<string> RemoveStray(InstallLayout l, IEnumerable<string> keepFolders) {
    var stray = Stray(l, keepFolders);
    foreach (var n in stray) Directory.Delete(Path.Combine(l.ModsDir, n), true);
    return stray;
  }
  /// Oyun klasöründeki ZombieBuddy'yi kurulu modla aynı tutar; farklıysa ZB açılışta "yeni sürüm kuruldu, yeniden başlat" der.
  public void SyncZombieBuddy() {
    var libs = Path.Combine(l.ModsDir, "ZombieBuddy", "libs");
    foreach (var f in new[] { "zbNative.dll", "ZombieBuddy.jar" }) {
      var src = Path.Combine(libs, f); var dst = Path.Combine(l.GameDir, f);
      if (File.Exists(src) && (!File.Exists(dst) || !File.ReadAllBytes(src).AsSpan().SequenceEqual(File.ReadAllBytes(dst)))) File.Copy(src, dst, true);
    }
  }
  public void PlaceZombieBuddy(string zbItemDir) {
    var libs = Path.Combine(zbItemDir, "mods", "ZombieBuddy", "libs");
    Directory.CreateDirectory(l.GameDir);
    foreach (var f in new[] { "zbNative.dll", "ZombieBuddy.jar" }) File.Copy(Path.Combine(libs, f), Path.Combine(l.GameDir, f), true);
  }
  public void WriteConfig(int memoryMb, bool steam = false) {
    var cfg = Path.Combine(l.GameDir, "ProjectZomboid64.json"); var bak = cfg + ".orijinal";
    if (!File.Exists(bak)) File.Copy(cfg, bak);
    Directory.CreateDirectory(l.ZbConfigDir);
    File.WriteAllText(cfg, LaunchConfig.Apply(File.ReadAllText(bak), l, memoryMb, steam));
  }
  public void WriteModList(IEnumerable<ModInfo> ordered) {
    Directory.CreateDirectory(l.ModsDir);
    File.WriteAllText(Path.Combine(l.ModsDir, "default.txt"), ModList.DefaultTxt(ordered.Select(m => m.Id)));
  }
  /// Yalnız değişen dosyaları kopyalar (boyut ya da tarih farklı), kaynakta olmayanları siler.
  /// 3D modeller gibi 37 bin dosyalı paketlerde baştan kopyalamak dakikalar sürüyordu.
  public static void SyncDir(string src, string dst, Action? onFile = null) {
    var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Parallel.ForEach(Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).ToList(), new ParallelOptions { MaxDegreeOfParallelism = 8 }, f => {
      var rel = Path.GetRelativePath(src, f);
      lock (want) want.Add(rel);
      var t = Path.Combine(dst, rel);
      var fi = new FileInfo(f); var ti = new FileInfo(t);
      if (!ti.Exists || ti.Length != fi.Length || ti.LastWriteTimeUtc != fi.LastWriteTimeUtc) {
        Directory.CreateDirectory(ti.DirectoryName!);
        File.Copy(f, t, true);
        File.SetLastWriteTimeUtc(t, fi.LastWriteTimeUtc);
      }
      onFile?.Invoke();
    });
    if (!Directory.Exists(dst)) return;
    foreach (var t in Directory.EnumerateFiles(dst, "*", SearchOption.AllDirectories).ToList())
      if (!want.Contains(Path.GetRelativePath(dst, t))) File.Delete(t);
    foreach (var d in Directory.EnumerateDirectories(dst, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
      if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
  }

}
