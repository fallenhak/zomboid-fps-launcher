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
        var dst = Path.Combine(l.ModsDir, Path.GetFileName(m.Dir));
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        CopyDir(m.Dir, dst);
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
  public void PlaceZombieBuddy(string zbItemDir) {
    var libs = Path.Combine(zbItemDir, "mods", "ZombieBuddy", "libs");
    Directory.CreateDirectory(l.GameDir);
    foreach (var f in new[] { "zbNative.dll", "ZombieBuddy.jar" }) File.Copy(Path.Combine(libs, f), Path.Combine(l.GameDir, f), true);
  }
  public void WriteConfig(int memoryMb) {
    var cfg = Path.Combine(l.GameDir, "ProjectZomboid64.json"); var bak = cfg + ".orijinal";
    if (!File.Exists(bak)) File.Copy(cfg, bak);
    Directory.CreateDirectory(l.ZbConfigDir);
    File.WriteAllText(cfg, LaunchConfig.Apply(File.ReadAllText(bak), l, memoryMb));
  }
  public void WriteModList(IEnumerable<ModInfo> ordered) {
    Directory.CreateDirectory(l.ModsDir);
    File.WriteAllText(Path.Combine(l.ModsDir, "default.txt"), ModList.DefaultTxt(ordered.Where(m => !m.Id.Equals("ZombieBuddy", StringComparison.OrdinalIgnoreCase)).Select(m => m.Id)));
  }
  static void CopyDir(string src, string dst) {
    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
      var t = Path.Combine(dst, Path.GetRelativePath(src, f)); Directory.CreateDirectory(Path.GetDirectoryName(t)!); File.Copy(f, t, true);
    }
  }
}
