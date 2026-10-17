using System.Text.RegularExpressions;
namespace Zfl.Core;
/// PZ_Optimization (github.com/xD3I/PZ_Optimization): oyun klasörüne konan Java sınıfları, Atölye'den kurulamıyor.
/// Kurulumu ve kaldırmayı kendi betiği yapar (oyun sürümüne uyan paketi indirir, yazdığı dosyaları listeler).
/// Sınıflar tek bir oyun sürümüne göre derleniyor; oyun güncellenince yeniden kurulmazsa oyun açılmayabilir.
public sealed class PzOptimization(string gameDir, string toolsDir, IProcessRunner run, HttpClient http) {
  public const string Repo = "xD3I/PZ_Optimization";
  public string Manifest => Path.Combine(gameDir, "pzopt-installed.txt");
  public bool Installed => File.Exists(Manifest);
  /// Kurulu paketin hangi oyun sürümü için olduğu (pzopt-installed.txt'deki "# revision=").
  public string? InstalledRevision => Installed ? Revision(File.ReadAllText(Manifest)) : null;
  public static string? Revision(string manifest) => Regex.Match(manifest, @"(?m)^# revision=(\S+)") is { Success: true } m ? m.Groups[1].Value : null;
  async Task<string> ScriptAsync(CancellationToken ct) {
    var dir = Path.Combine(toolsDir, "pzopt"); Directory.CreateDirectory(dir);
    var f = Path.Combine(dir, "install.ps1");
    try { await File.WriteAllBytesAsync(f, await http.GetByteArrayAsync($"https://github.com/{Repo}/releases/latest/download/install.ps1", ct), ct); }
    catch when (File.Exists(f)) { } // internet yoksa önceki kopya
    return f;
  }
  async Task<string?> LatestTagAsync(CancellationToken ct) {
    try {
      using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
      req.Headers.UserAgent.ParseAdd("ZomboidFpsLauncher");
      using var r = await http.SendAsync(req, ct);
      if (!r.IsSuccessStatusCode) return null;
      using var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
      return j.RootElement.GetProperty("tag_name").GetString();
    } catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException or KeyNotFoundException) { return null; }
  }
  Task<(int, string)> RunAsync(string script, string extra, CancellationToken ct) =>
    run.RunAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Dir \"{gameDir}\" {extra}", ct);
  /// Kurar ya da eskisinin yerine yenisini koyar. Bu oyun sürümüne paket yoksa eskisini kaldırır (oyun açılsın diye) ve hatayı döner.
  public async Task<string?> InstallAsync(CancellationToken ct) {
    var s = await ScriptAsync(ct);
    // sürüm adı verilmezse betik, bilgisayarda gh varsa sürümleri yanlış süzüp "paket yok" diyor
    var tag = await LatestTagAsync(ct);
    var (code, output) = await RunAsync(s, tag is null ? "" : $"-Tag \"{tag}\"", ct);
    if (code == 0) return null;
    if (Installed) await RunAsync(s, "-Uninstall", ct);
    return LastError(output);
  }
  public async Task<string?> UninstallAsync(CancellationToken ct) {
    var (code, output) = await RunAsync(await ScriptAsync(ct), "-Uninstall", ct);
    return code == 0 ? null : LastError(output);
  }
  /// Viewpoint gibi Java modlarıyla çakışan iyileştirmelerini kapatsın (varsayılanı yalnız rapor yazmak).
  /// Oyuncu Seçenekler'de kendisi seçtiyse dokunulmaz.
  public static void PreferCompatibility(string zomboidDir) {
    var f = Path.Combine(zomboidDir, "pzopt", "options.ini");
    if (ViewpointSetup.Get(f, "modProfile") is null) ViewpointSetup.SetKeys(f, new Dictionary<string, string> { ["modProfile"] = "compatibility" });
  }
  public static string LastError(string output) =>
    output.Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.StartsWith("error:")) ?? output.Split('\n').LastOrDefault(x => x.Trim().Length > 0)?.Trim() ?? "";
}
