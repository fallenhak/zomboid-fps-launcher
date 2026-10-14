using System.IO.Compression;
using System.Text.RegularExpressions;
namespace Zfl.Core;
/// Viewpoint'in shader paketi klasörü. Oyuncu paketi buraya koyar, seçimi oyunda yapar (Delete > Graphics > Modern).
/// Viewpoint paketi: pack.properties olan klasör. Minecraft paketi: .zip'i olduğu gibi ya da içinde shaders klasörü olan klasör.
public static partial class ShaderPacks {
  public sealed record Pack(string Name, string Path, bool IsZip);
  [GeneratedRegex("[^A-Za-z0-9_-]+")] private static partial Regex BadChars();
  public static string Dir(InstallLayout l) => System.IO.Path.Combine(l.ZomboidDir, "viewpoint-shaderpacks");
  public static IReadOnlyList<Pack> List(InstallLayout l) {
    var d = Dir(l);
    if (!Directory.Exists(d)) return [];
    var zips = Directory.GetFiles(d, "*.zip").Select(f => new Pack(System.IO.Path.GetFileNameWithoutExtension(f), f, true));
    var dirs = Directory.GetDirectories(d).Where(IsPackFolder).Select(f => new Pack(System.IO.Path.GetFileName(f), f, false));
    return zips.Concat(dirs).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
  }
  static bool IsPackFolder(string d) => File.Exists(System.IO.Path.Combine(d, "pack.properties")) || Directory.Exists(System.IO.Path.Combine(d, "shaders"));
  static bool IsPackZip(string zip) {
    try {
      using var z = ZipFile.OpenRead(zip);
      return z.Entries.Any(e => e.FullName.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase) || e.FullName.Equals("pack.properties", StringComparison.OrdinalIgnoreCase));
    } catch (InvalidDataException) { return false; }
  }
  /// Paketi klasöre kopyalar, adını döner. Shader paketi değilse ShaderPackException.
  public static string Add(InstallLayout l, string source) {
    var d = Dir(l); Directory.CreateDirectory(d);
    if (File.Exists(source)) {
      if (!source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !IsPackZip(source)) throw new ShaderPackException(Loc.T("sh.notPack"));
      var dst = System.IO.Path.Combine(d, System.IO.Path.GetFileName(source));
      if (File.Exists(dst)) throw new ShaderPackException(Loc.T("sh.exists"));
      File.Copy(source, dst);
      return System.IO.Path.GetFileNameWithoutExtension(source);
    }
    if (!Directory.Exists(source) || !IsPackFolder(source)) throw new ShaderPackException(Loc.T("sh.notPack"));
    // Viewpoint klasör adında yalnız harf, rakam, _ ve - kabul ediyor
    var name = BadChars().Replace(System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(source)), "_").Trim('_');
    if (name.Length == 0) name = "pack";
    var target = System.IO.Path.Combine(d, name);
    if (Directory.Exists(target)) throw new ShaderPackException(Loc.T("sh.exists"));
    Installer.SyncDir(source, target);
    return name;
  }
  public static void Remove(Pack p) {
    if (p.IsZip) File.Delete(p.Path); else Directory.Delete(p.Path, true);
  }
}
public sealed class ShaderPackException(string m) : Exception(m);
