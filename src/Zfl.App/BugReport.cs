using System.IO;
using System.Text;
using Zfl.Core;

namespace Zfl.App;

/// Viewpoint Discord kural 4'ün istediği bilgiler.
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
        sb.AppendLine($"Viewpoint: {MainWindow.ModVersionOf(Path.Combine(l.ModsDir, "Viewpoint")) ?? "kurulu değil"}");
        sb.AppendLine($"Oyun: {GameInfo.Version(l.ZomboidDir) ?? "bilinmiyor (oyun henüz açılmadı)"}");
        sb.AppendLine($"ZombieBuddy: {ZombieBuddyVersion(l) ?? "kurulu değil"}");
        foreach (var g in SystemInfo.Gpus()) sb.AppendLine($"Ekran kartı: {g}");
        sb.AppendLine("Shader paketi ve preset: (oyun içi Viewpoint ayarlarından yaz)");
        sb.AppendLine($"Diğer modlar: {string.Join(", ", mods.Where(m => m.Id is not ("Viewpoint" or "ZombieBuddy")).Select(m => m.Name))}");
        sb.AppendLine($"console.txt: {Path.Combine(l.ZomboidDir, "console.txt")}");
        sb.AppendLine("Launcher: Zomboid FPS Launcher 0.1.0");
        return sb.ToString();
    }
}
