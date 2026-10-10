namespace Zfl.Core;
public sealed record ModInfo(string Id, string Name, string Dir, IReadOnlyList<string> Requires, string? VersionMin, string? VersionMax, bool IsJava) {
  public static ModInfo? Read(string modFolder) {
    var file = InfoFiles(modFolder).FirstOrDefault(File.Exists);
    if (file is null) return null;
    var kv = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadAllLines(file)) {
      var i = line.IndexOf('='); if (i <= 0) continue;
      var k = line[..i].Trim(); var v = line[(i + 1)..].Trim();
      if (!kv.TryGetValue(k, out var l)) kv[k] = l = []; l.Add(v);
    }
    string? One(string k) => kv.TryGetValue(k, out var l) ? l[0] : null;
    var id = One("id"); if (id is null) return null;
    var req = kv.TryGetValue("require", out var r)
      ? r.SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Select(x => x.TrimStart('\\')).Where(x => x.Length > 0).ToList()
      : new List<string>();
    return new ModInfo(id, One("name") ?? id, modFolder, req, One("versionMin"), One("versionMax"), One("javaJarFile") is not null);
  }
  /// mod.info adayları: sürüm klasörleri (42, 42.20.4...) büyükten küçüğe, sonra common, sonra kök.
  public static IEnumerable<string> InfoFiles(string modFolder) {
    var versions = Directory.Exists(modFolder)
      ? Directory.GetDirectories(modFolder).Select(d => Path.GetFileName(d)!)
          .Select(n => (n, v: VerDir(n))).Where(x => x.v is not null).OrderByDescending(x => x.v).Select(x => x.n)
      : [];
    return versions.Append("common").Append("").Select(s => Path.Combine(modFolder, s, "mod.info"));
  }
  static Version? VerDir(string n) => Version.TryParse(n.Contains('.') ? n : n + ".0", out var v) ? v : null;
  static Version? V(string? s) => Version.TryParse(s, out var v) ? v : null;
  public static bool SupportsGame(ModInfo m, string gameVersion) {
    var g = V(gameVersion); if (g is null) return true;
    if (V(m.VersionMin) is { } lo && g < lo) return false;
    if (V(m.VersionMax) is { } hi && g > hi) return false;
    return true;
  }
  public static IReadOnlyList<ModInfo> FindInWorkshopItem(string itemDir) {
    var mods = Path.Combine(itemDir, "mods");
    if (!Directory.Exists(mods)) return [];
    return Directory.GetDirectories(mods).Select(Read).OfType<ModInfo>().ToList();
  }
}
public static class ModList {
  public static string DefaultTxt(IEnumerable<string> modIds) =>
    "VERSION = 1,\n\nmods\n{\n" + string.Concat(modIds.Select(id => $"    mod = \\{id},\n")) + "}\n";
  public static IReadOnlyList<string> Missing(IEnumerable<ModInfo> mods) {
    var list = mods.ToList(); var have = list.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    return list.SelectMany(m => m.Requires).Where(r => !have.Contains(r)).Distinct().ToList();
  }
}
