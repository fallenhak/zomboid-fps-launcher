using System.IO;

namespace Zfl.App;

/// Launcher günlüğü: %LOCALAPPDATA%\ZomboidFpsLauncher\logs\launcher.log (2 MB'ı geçince .old'a döner).
static class Log
{
    public static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZomboidFpsLauncher", "logs", "launcher.log");
    static readonly object Gate = new();

    public static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File)!);
                if (new FileInfo(File) is { Exists: true, Length: > 2 << 20 }) System.IO.File.Move(File, File + ".old", true);
                System.IO.File.AppendAllText(File, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void Error(string where, Exception ex) => Write($"ERROR {where}: {ex}");

    /// Son satırlar (hata raporu için).
    public static IEnumerable<string> Tail(string file, int n)
    {
        try
        {
            using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new StreamReader(s).ReadToEnd().Split('\n').TakeLast(n).Select(x => x.TrimEnd('\r'));
        }
        catch (IOException) { return []; }
    }
}
