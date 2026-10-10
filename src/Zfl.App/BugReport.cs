using System.IO;
using System.Text;
using Zfl.Core;

namespace Zfl.App;

/// Viewpoint Discord kural 4'ün istediği bilgiler. Viewpoint ekibi okuyacağı için hep İngilizce.
static class BugReport
{
    public static string? ZombieBuddyVersion(InstallLayout l)
    {
        var f = Path.Combine(l.ModsDir, "ZombieBuddy", "java", "VERSION");
        return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
    }

    public static string Collect(InstallLayout l, IReadOnlyList<ModInfo> mods)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Viewpoint: {MainWindow.ModVersionOf(Path.Combine(l.ModsDir, "Viewpoint")) ?? "not installed"}");
        sb.AppendLine($"Game: {GameInfo.Version(l.ZomboidDir) ?? "unknown (not launched yet)"}");
        sb.AppendLine($"ZombieBuddy: {ZombieBuddyVersion(l) ?? "not installed"}");
        foreach (var g in SystemInfo.Gpus()) sb.AppendLine($"GPU: {g}");
        sb.AppendLine("Shader pack / preset: (check Viewpoint settings in game)");
        sb.AppendLine($"Other mods: {string.Join(", ", mods.Where(m => m.Id is not ("Viewpoint" or "ZombieBuddy")).Select(m => m.Name))}");
        sb.AppendLine($"console.txt: {Path.Combine(l.ZomboidDir, "console.txt")}");
        sb.AppendLine("Launcher: Zomboid FPS Launcher 0.1.0");
        return sb.ToString();
    }
}
