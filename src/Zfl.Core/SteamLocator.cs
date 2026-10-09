using System.Text.RegularExpressions; using Microsoft.Win32;
namespace Zfl.Core;
public static partial class SteamLocator {
  [GeneratedRegex("\"path\"\\s+\"(?<p>[^\"]+)\"")] private static partial Regex PathRx();
  public static IReadOnlyList<string> LibraryFolders(string vdf) =>
    PathRx().Matches(vdf).Select(m => m.Groups["p"].Value.Replace(@"\\", @"\")).ToList();
  public static string? FindGame() {
    var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
    if (steam is null) return null;
    var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
    var libs = File.Exists(vdf) ? LibraryFolders(File.ReadAllText(vdf)) : [steam];
    return libs.Select(l => Path.Combine(l, "steamapps", "common", "ProjectZomboid"))
               .FirstOrDefault(d => File.Exists(Path.Combine(d, "ProjectZomboid64.exe")));
  }
}
public static class GameMirror {
  static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase) { "ProjectZomboid64.json" };
  public static Task<int> SyncAsync(string src, string dst, IProgress<string>? p, CancellationToken ct) => Task.Run(() => {
    int n = 0;
    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
      ct.ThrowIfCancellationRequested();
      var rel = Path.GetRelativePath(src, f);
      var target = Path.Combine(dst, rel);
      if (Protected.Contains(rel) && File.Exists(target)) continue;
      var s = new FileInfo(f); var t = new FileInfo(target);
      if (t.Exists && t.Length == s.Length && t.LastWriteTimeUtc == s.LastWriteTimeUtc) continue;
      Directory.CreateDirectory(t.DirectoryName!);
      File.Copy(f, target, true); File.SetLastWriteTimeUtc(target, s.LastWriteTimeUtc);
      n++; p?.Report(rel);
    }
    return n;
  }, ct);
}
