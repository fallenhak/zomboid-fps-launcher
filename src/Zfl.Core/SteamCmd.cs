using System.Diagnostics; using System.IO.Compression;
namespace Zfl.Core;
public interface IProcessRunner { Task<(int Code, string Output)> RunAsync(string exe, string args, CancellationToken ct); }
public sealed class ProcessRunner : IProcessRunner {
  public async Task<(int, string)> RunAsync(string exe, string args, CancellationToken ct) {
    var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
    using var p = Process.Start(psi)!;
    var o = p.StandardOutput.ReadToEndAsync(ct); var e = p.StandardError.ReadToEndAsync(ct);
    await p.WaitForExitAsync(ct);
    return (p.ExitCode, await o + await e);
  }
}
public sealed class SteamCmdException(string m) : Exception(m);
public sealed class SteamCmd(string toolsDir, IProcessRunner run, HttpClient http) {
  public const long AppId = 108600;
  string Home => Path.Combine(toolsDir, "steamcmd");
  string Exe => Path.Combine(Home, "steamcmd.exe");
  public string ItemDir(long id) => Path.Combine(Home, "steamapps", "workshop", "content", AppId.ToString(), id.ToString());
  public async Task EnsureInstalledAsync(CancellationToken ct) {
    if (File.Exists(Exe)) return;
    Directory.CreateDirectory(Home);
    var zip = Path.Combine(Home, "steamcmd.zip");
    await File.WriteAllBytesAsync(zip, await http.GetByteArrayAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", ct), ct);
    ZipFile.ExtractToDirectory(zip, Home, true);
    await run.RunAsync(Exe, "+quit", ct); // ilk çalıştırma kendini günceller
  }
  public async Task<string> DownloadAsync(long itemId, CancellationToken ct) {
    var args = $"+login anonymous +workshop_download_item {AppId} {itemId} +quit";
    string output = "";
    for (int attempt = 0; attempt < 2; attempt++) {
      (_, output) = await run.RunAsync(Exe, args, ct);
      if (output.Contains($"Success. Downloaded item {itemId}")) return ItemDir(itemId);
      if (output.Contains("ERROR")) break;
    }
    throw new SteamCmdException(Loc.F("err.download", itemId) + $"\n{output.Split('\n').LastOrDefault(l => l.Trim().Length > 0)}");
  }
}
