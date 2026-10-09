using System.Text.Json;
namespace Zfl.Core;
public sealed record FeedMod(long WorkshopId, string Name);
public sealed record Feed(string GameVersion, IReadOnlyList<FeedMod> BaseMods, string LauncherVersion, string? LauncherUrl) {
  static readonly JsonSerializerOptions O = new() { PropertyNameCaseInsensitive = true };
  public static Feed Parse(string json) => JsonSerializer.Deserialize<Feed>(json, O) ?? throw new InvalidDataException("feed boş");
  public const string Url = "https://raw.githubusercontent.com/fallenhak/zomboid-fps-launcher/main/feed.json";
  public static Feed Builtin { get; } = new("42.21", [
    new(3619862853, "ZombieBuddy"), new(3809306528, "Project Viewpoint"), new(3810302175, "6244 3D models for Viewpoint"),
    new(3814178814, "Complete Roofs"), new(3813172420, "TREE in 3D"), new(3811256204, "Viewpoint QOL")], "0.1.0", null);
}
public sealed class LauncherState {
  public string? InstallRoot { get; set; }
  public int MemoryMb { get; set; }
  public List<long> UserMods { get; set; } = [];
  public List<long> DisabledMods { get; set; } = [];
  public string? LastGameVersion { get; set; }
  public string? LastBuildId { get; set; }
  static readonly JsonSerializerOptions O = new() { WriteIndented = true };
  public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZomboidFpsLauncher", "state.json");
  public static LauncherState Load(string path) {
    try { return JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(path)) ?? new(); } catch { return new(); }
  }
  public void Save(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(this, O)); }
}
public sealed class Installer(InstallLayout l, SteamCmd steam, IProgress<string> p) {
  public async Task<IReadOnlyList<ModInfo>> InstallModsAsync(IEnumerable<long> ids, CancellationToken ct) {
    var result = new List<ModInfo>();
    foreach (var id in ids) {
      p.Report($"İndiriliyor: {id}");
      var item = await steam.DownloadAsync(id, ct);
      if (id == 3619862853) PlaceZombieBuddy(item);
      foreach (var m in ModInfo.FindInWorkshopItem(item)) {
        var dst = Path.Combine(l.ModsDir, Path.GetFileName(m.Dir));
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        CopyDir(m.Dir, dst);
        result.Add(ModInfo.Read(dst)!);
      }
    }
    return result;
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
