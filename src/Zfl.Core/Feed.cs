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
  public async Task<IReadOnlyList<ModInfo>> InstallModsAsync(IEnumerable<long> ids, CancellationToken ct) {
    var result = new List<ModInfo>();
    foreach (var id in ids) {
      p.Report(Loc.F("pr.download", Name(id)));
      var item = await DownloadOrFallbackAsync(id, ct);
      if (id == 3619862853) PlaceZombieBuddy(item);
      // kopyalama arka planda: büyük paketlerde pencere donmasın
      result.AddRange(await Task.Run(() => ModInfo.FindInWorkshopItem(item).Select(m => {
        // önce yan klasöre kopyalanır, bitince yerine konur: yarıda kalırsa eksik mod "kurulu" görünmez
        var dst = Path.Combine(l.ModsDir, Path.GetFileName(m.Dir));
        var tmp = dst + ".kuruluyor";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        CopyDir(m.Dir, tmp);
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        Directory.Move(tmp, dst);
        return ModInfo.Read(dst)!;
      }).ToList(), ct));
    }
    return result;
  }
  /// Atölye'den iner; inmezse (internet yok ya da mod Atölye'den kalkmış) eldeki kopyayı kullanır:
  /// önce SteamCMD'nin önceki indirmesi, sonra verilen klasörler (Steam'in kendi Atölye klasörü gibi).
  async Task<string> DownloadOrFallbackAsync(long id, CancellationToken ct) {
    try { return await steam.DownloadAsync(id, ct); }
    catch (SteamCmdException) {
      var local = new[] { steam.ItemDir(id) }.Concat((fallbackDirs ?? []).Select(d => Path.Combine(d, id.ToString())))
        .FirstOrDefault(d => ModInfo.FindInWorkshopItem(d).Count > 0);
      if (local is null) throw;
      return local;
    }
  }
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
  static void CopyDir(string src, string dst) {
    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
      var t = Path.Combine(dst, Path.GetRelativePath(src, f)); Directory.CreateDirectory(Path.GetDirectoryName(t)!); File.Copy(f, t, true);
    }
  }
}
