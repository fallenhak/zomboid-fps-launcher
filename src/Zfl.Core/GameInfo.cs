using System.Text.RegularExpressions;

namespace Zfl.Core;

public static partial class GameInfo
{
    [GeneratedRegex(@"""buildid""\s+""(?<b>\d+)""")] private static partial Regex BuildRx();

    /// Steam'in appmanifest'indeki buildid; oyun güncellenince değişir.
    public static string? BuildId(string steamGameDir)
    {
        var manifest = Path.Combine(steamGameDir, "..", "..", "appmanifest_108600.acf");
        if (!File.Exists(manifest)) return null;
        var m = BuildRx().Match(File.ReadAllText(manifest));
        return m.Success ? m.Groups["b"].Value : null;
    }

    /// Oyunun ilk açılışta yazdığı "42.21.0 4a0e9546ec" → "42.21".
    public static string? Version(string zomboidDir)
    {
        var f = Path.Combine(zomboidDir, "version.txt");
        if (!File.Exists(f)) return null;
        return ShortVersion(File.ReadAllText(f));
    }

    public static string? ShortVersion(string text)
    {
        var first = text.Trim().Split(' ', 2)[0];
        var parts = first.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : null;
    }
}
