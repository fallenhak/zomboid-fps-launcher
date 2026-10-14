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
  /// SteamCMD'nin indirirken dosyaları biriktirdiği yer; bitince ItemDir'e taşınır.
  public string StagingDir(long id) => Path.Combine(Home, "steamapps", "workshop", "downloads", AppId.ToString(), id.ToString());
  public async Task<string> DownloadAsync(long itemId, CancellationToken ct) {
    var (done, output) = await DownloadManyAsync([itemId], ct);
    if (done.Contains(itemId)) return ItemDir(itemId);
    throw new SteamCmdException(Loc.F("err.download", itemId) + $"\n{output.Split('\n').LastOrDefault(l => l.Trim().Length > 0)}");
  }
  /// Hepsini tek oturumda indirir: her mod için ayrı giriş yapmak mod başına ~6 saniye kaybettiriyordu.
  /// İnmeyenler bir kez daha denenir (ilk çalıştırmada SteamCMD kendini güncelleyip çıkabiliyor).
  public async Task<(IReadOnlySet<long> Done, string Output)> DownloadManyAsync(IReadOnlyList<long> ids, CancellationToken ct) {
    var done = new HashSet<long>(); string output = "";
    for (int attempt = 0; attempt < 2; attempt++) {
      var left = ids.Where(id => !done.Contains(id)).ToList();
      if (left.Count == 0) break;
      var args = "+login anonymous " + string.Join(" ", left.Select(id => $"+workshop_download_item {AppId} {id}")) + " +quit";
      (_, output) = await run.RunAsync(Exe, args, ct);
      foreach (var id in left) if (output.Contains($"Success. Downloaded item {id}")) done.Add(id);
      if (left.All(id => output.Contains($"Download item {id} failed"))) break;
    }
    return (done, output);
  }
}
