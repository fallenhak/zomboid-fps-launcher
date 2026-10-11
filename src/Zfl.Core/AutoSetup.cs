using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Zfl.Core;

/// ZombieBuddy onay dosyası: temel modların jar'ları önceden onaylanır, oyuncuya sorulmaz.
public static class ZbApprovals {
  public static string FileIn(InstallLayout l) => Path.Combine(l.ZbConfigDir, "mod_approvals.json");
  public static string Hash(string jar) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(jar)));

  /// Verilen modları onaylı yazar; dosyadaki diğer kayıtlara (kullanıcının kararları) dokunmaz.
  public static void Approve(string file, IEnumerable<(string Id, string Hash)> mods) {
    JsonObject root;
    try { root = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsObject() : []; }
    catch (JsonException) { root = []; }
    root["formatVersion"] ??= 2;
    var list = root["mods"] as JsonArray ?? [];
    root["mods"] = list;
    foreach (var (id, hash) in mods) {
      var has = list.OfType<JsonObject>().Any(o => (string?)o["id"] == id && (string?)o["jar_hash"] == hash && (bool?)o["decision"] == true);
      if (!has) list.Add(new JsonObject { ["id"] = id, ["jar_hash"] = hash, ["decision"] = true });
    }
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
  }

  public static void ApproveMods(InstallLayout l, IEnumerable<ModInfo> mods) =>
    Approve(FileIn(l), mods.Select(m => (m.Id, Jar: m.JarPath())).Where(x => x.Jar is not null).Select(x => (x.Id, Hash(x.Jar!))).ToList());
}

/// Viewpoint'in ilk kurulum ekranının yaptığını önceden yapar.
public static class ViewpointSetup {
  public static readonly string[] Presets = ["Potato", "Default", "High", "Ultra"];
  const long CompressionBelowMiB = 12 * 1024;

  public static string SettingsFile(InstallLayout l) => Path.Combine(l.ZomboidDir, "viewpoint-live.properties");
  public static string OptionsFile(InstallLayout l) => Path.Combine(l.ZomboidDir, "options.ini");

  /// Ekran kartı belleğine göre kalite.
  public static string PresetFor(long vramMiB) => vramMiB switch {
    <= 0 => "High", < 4096 => "Potato", < 6144 => "Default", < 10240 => "High", _ => "Ultra",
  };
  public static bool NeedsCompression(long vramMiB) => vramMiB > 0 && vramMiB < CompressionBelowMiB;

  /// key=value dosyasında anahtarları ayarlar (yoksa ekler), diğer satırlara dokunmaz.
  public static void SetKeys(string file, IReadOnlyDictionary<string, string> values) {
    var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : [];
    var left = new Dictionary<string, string>(values);
    for (var i = 0; i < lines.Count; i++) {
      var eq = lines[i].IndexOf('='); if (eq <= 0 || lines[i].TrimStart().StartsWith('#')) continue;
      var k = lines[i][..eq].Trim();
      if (left.Remove(k, out var v)) lines[i] = $"{k}={v}";
    }
    lines.AddRange(left.Select(kv => $"{kv.Key}={kv.Value}"));
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllLines(file, lines);
  }

  public static string? Get(string file, string key) => !File.Exists(file) ? null : File.ReadLines(file)
    .Select(x => x.Split('=', 2)).Where(p => p.Length == 2 && p[0].Trim() == key).Select(p => p[1].Trim()).LastOrDefault();

  public static bool Finished(InstallLayout l) => Get(SettingsFile(l), "onboarding.finished") == "true";

  /// İlk kurulumu bitmiş sayar; kalite seçilmişse onu yazar.
  public static void Apply(InstallLayout l, string preset, bool compression) {
    var pack = Get(SettingsFile(l), "graphics.pack") ?? "vivid";
    SetKeys(SettingsFile(l), new Dictionary<string, string> {
      ["onboarding.finished"] = "true", ["graphics.mode"] = "Modern", ["graphics.pack"] = pack,
      ["graphics.preset"] = preset, [$"packPreset.{pack}"] = preset,
    });
    if (compression) EnableCompression(l);
  }

  /// Takılmaları azaltır: bakılmayan yerler önceden hazırlanır. Gölgeler oyuncunun kendi ayarı, dokunulmaz.
  public static void ApplySmooth(InstallLayout l) =>
    SetKeys(SettingsFile(l), new Dictionary<string, string> { ["chunks.buildOutOfView"] = "true" });

  /// Viewpoint'in performans göstergesi (oyunda ' tuşu, Türkçe klavyede i ile de değişir).
  public static void SetFpsOverlay(InstallLayout l, bool on) =>
    SetKeys(SettingsFile(l), new Dictionary<string, string> { ["overlay.mode"] = on ? "Compact" : "Off" });

  public static void EnableCompression(InstallLayout l) =>
    SetKeys(OptionsFile(l), new Dictionary<string, string> { ["textureCompression"] = "true" });

  /// Viewpoint ilk kurulum ekranı bir sonraki açılışta tekrar çıksın.
  public static void Reset(InstallLayout l) {
    var f = SettingsFile(l);
    if (File.Exists(f)) File.WriteAllLines(f, File.ReadAllLines(f).Where(x => !x.TrimStart().StartsWith("onboarding.")));
  }
}
