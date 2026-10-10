namespace Zfl.Core;
public sealed record InstallLayout(string Root) {
  public string GameDir => Path.Combine(Root, "oyun");
  public string UserHome => Path.Combine(Root, "Kullanici");
  public string ZomboidDir => Path.Combine(UserHome, "Zomboid");
  public string ModsDir => Path.Combine(ZomboidDir, "mods");
  public string ZbConfigDir => Path.Combine(Root, "zombie_buddy");
  public string ToolsDir => Path.Combine(Root, "araclar");
  public string StateFile => Path.Combine(Root, "launcher", "state.json");
  public static string? Validate(string root) {
    if (!Path.IsPathFullyQualified(root)) return Loc.T("path.full");
    var full = Path.GetFullPath(root).TrimEnd('\\');
    if (Path.GetPathRoot(full)!.TrimEnd('\\') == full) return Loc.T("path.drive");
    var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    if (!string.IsNullOrEmpty(win) && full.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return Loc.T("path.windows");
    return null;
  }
}
public static class Memory {
  public static int DefaultMb(long totalBytes) => (int)Math.Clamp(totalBytes / 2 / (1 << 20), 4096, 12288);
}
