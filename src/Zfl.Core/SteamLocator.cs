using System.Text.RegularExpressions; using Microsoft.Win32;
using System.Runtime.Versioning;
namespace Zfl.Core;
public static partial class SteamLocator {
  [GeneratedRegex("\"path\"\\s+\"(?<p>[^\"]+)\"")] private static partial Regex PathRx();
  public static IReadOnlyList<string> LibraryFolders(string vdf) =>
    PathRx().Matches(vdf).Select(m => m.Groups["p"].Value.Replace(@"\\", @"\")).ToList();
  [SupportedOSPlatform("windows")]
  static IReadOnlyList<string> Libraries() {
    var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
    if (steam is null) return [];
    var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
    return File.Exists(vdf) ? LibraryFolders(File.ReadAllText(vdf)) : [steam];
  }
  [SupportedOSPlatform("windows")]
  public static string? FindGame() =>
    Libraries().Select(l => Path.Combine(l, "steamapps", "common", "ProjectZomboid"))
               .FirstOrDefault(d => File.Exists(Path.Combine(d, "ProjectZomboid64.exe")));
  /// Steam'in abone olunan Atölye modlarını tuttuğu klasörler (yedek kaynak).
  [SupportedOSPlatform("windows")]
  public static IReadOnlyList<string> WorkshopDirs() =>
    Libraries().Select(l => Path.Combine(l, "steamapps", "workshop", "content", SteamCmd.AppId.ToString())).Where(Directory.Exists).ToList();
}
public static class GameMirror {
  static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase) { "ProjectZomboid64.json" };
  // Steam kopyasında eski ZombieBuddy olabilir; bunları launcher moddan koyar, Steam'den hiç alınmaz
  static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase) { "ZombieBuddy.jar", "zbNative.dll" };
  public static Task<int> SyncAsync(string src, string dst, IProgress<string>? p, CancellationToken ct) => Task.Run(() => {
    int n = 0;
    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
      ct.ThrowIfCancellationRequested();
      var rel = Path.GetRelativePath(src, f);
      var target = Path.Combine(dst, rel);
      if (Protected.Contains(rel) && File.Exists(target) || Skipped.Contains(rel)) continue;
      var s = new FileInfo(f); var t = new FileInfo(target);
      if (t.Exists && t.Length == s.Length && t.LastWriteTimeUtc == s.LastWriteTimeUtc) continue;
      Directory.CreateDirectory(t.DirectoryName!);
      File.Copy(f, target, true); File.SetLastWriteTimeUtc(target, s.LastWriteTimeUtc);
      n++; p?.Report(rel);
    }
    return n;
  }, ct);
}
