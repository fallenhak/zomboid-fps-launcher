using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Zfl.Core;

/// Drive'daki hazır mod paketi. Id değişince (yeni paket) yalnız henüz paket açmamış kurulumlar indirir.
public sealed record FeedPack(string Url, long SizeBytes, string Id);

/// Paketin içindeki paket.json: her Atölye öğesinin paketlendiği andaki güncellenme zamanı ve klasörleri.
public sealed record PackItem(long Updated, IReadOnlyList<string> Folders);
public sealed record PackManifest(long Created, Dictionary<long, PackItem> Items);

/// İlk kurulumda yüzlerce modu Atölye'den tek tek indirmek yerine tek zip.
/// Zip düzeni: mods/<klasör>/..., yerel-modlar/<numara>/mods/..., paket.json.
/// Sonrası bildiğimiz yol: Atölye'de paketten yeni olan modlar SteamCMD ile iner.
public static partial class ModPack {
  [GeneratedRegex(@"/d/([\w-]{10,})|[?&]id=([\w-]{10,})")] private static partial Regex DriveIdRx();
  [GeneratedRegex(@"<form[^>]*action=""([^""]+)""", RegexOptions.IgnoreCase)] private static partial Regex FormRx();
  [GeneratedRegex(@"<input[^>]*type=""hidden""[^>]*name=""([^""]+)""[^>]*value=""([^""]*)""", RegexOptions.IgnoreCase)] private static partial Regex HiddenRx();

  /// Drive paylaşım bağlantısını doğrudan indirme adresine çevirir; Drive değilse olduğu gibi.
  public static string DirectUrl(string url) {
    if (!url.Contains("drive.google.com") && !url.Contains("drive.usercontent.google.com")) return url;
    var m = DriveIdRx().Match(url);
    if (!m.Success) return url;
    var id = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
    return $"https://drive.usercontent.google.com/download?id={id}&export=download&confirm=t";
  }

  /// Drive büyük dosyada "virüs taraması yapılamadı" sayfası döndürür; içindeki formdan asıl indirme adresi.
  public static string? ConfirmUrl(string html) {
    var f = FormRx().Match(html);
    if (!f.Success) return null;
    var q = HiddenRx().Matches(html).Select(m => $"{m.Groups[1].Value}={Uri.EscapeDataString(WebUtility.HtmlDecode(m.Groups[2].Value))}");
    return WebUtility.HtmlDecode(f.Groups[1].Value) + "?" + string.Join("&", q);
  }

  /// İndirir; yarıda kalan .part dosyası varsa kaldığı yerden sürdürür.
  public static async Task DownloadAsync(HttpClient http, string url, string dest, long expected, IProgress<double?>? pct, CancellationToken ct) {
    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
    var part = dest + ".part";
    var target = DirectUrl(url);
    for (int attempt = 0; attempt < 3; attempt++) {
      long have = File.Exists(part) ? new FileInfo(part).Length : 0;
      using var req = new HttpRequestMessage(HttpMethod.Get, target);
      if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
      using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
      if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && have > 0 && (expected == 0 || have == expected)) break;
      resp.EnsureSuccessStatusCode();
      if (resp.Content.Headers.ContentType?.MediaType == "text/html") {
        var next = ConfirmUrl(await resp.Content.ReadAsStringAsync(ct)) ?? throw new InvalidDataException(Loc.T("err.packPage"));
        target = next;
        continue;
      }
      var append = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
      long done = append ? have : 0;
      long total = expected > 0 ? expected : (resp.Content.Headers.ContentLength ?? 0) + done;
      await using (var src = await resp.Content.ReadAsStreamAsync(ct))
      await using (var dst = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20)) {
        var buf = new byte[1 << 20]; int n; var last = DateTime.MinValue;
        while ((n = await src.ReadAsync(buf, ct)) > 0) {
          await dst.WriteAsync(buf.AsMemory(0, n), ct);
          done += n;
          if ((DateTime.Now - last).TotalMilliseconds > 250) { pct?.Report(total > 0 ? Math.Min(1, (double)done / total) : null); last = DateTime.Now; }
        }
      }
      break;
    }
    var got = File.Exists(part) ? new FileInfo(part).Length : 0;
    if (got == 0 || (expected > 0 && got != expected)) throw new InvalidDataException(Loc.F("err.packSize", got / 1048576, expected / 1048576));
    File.Move(part, dest, true);
  }

  /// Zip'i kurulum klasörüne açar ve paket.json'u döndürür. Yalnız mods/ ve yerel-modlar/ altına yazar.
  public static PackManifest Extract(string zipPath, InstallLayout l, IProgress<double?>? pct) {
    using var zip = ZipFile.OpenRead(zipPath);
    var manifestEntry = zip.GetEntry("paket.json") ?? throw new InvalidDataException(Loc.T("err.packManifest"));
    PackManifest manifest;
    using (var r = new StreamReader(manifestEntry.Open())) manifest = ParseManifest(r.ReadToEnd());
    long total = zip.Entries.Sum(e => e.Length), done = 0;
    foreach (var e in zip.Entries) {
      var dst = Target(e.FullName, l);
      if (dst is null) continue;
      if (e.FullName.EndsWith('/')) { Directory.CreateDirectory(dst); continue; }
      Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
      e.ExtractToFile(dst, true);
      done += e.Length;
      pct?.Report(total > 0 ? (double)done / total : null);
    }
    return manifest;
  }

  /// Zip içindeki yolun diskteki yeri; izin verilen klasörlerin dışına çıkan ya da tanınmayan girdiler null.
  public static string? Target(string entry, InstallLayout l) {
    var e = entry.Replace('\\', '/');
    (string prefix, string dir)[] roots = [("mods/", l.ModsDir), ("yerel-modlar/", l.LocalModsDir)];
    foreach (var (prefix, dir) in roots) {
      if (!e.StartsWith(prefix, StringComparison.Ordinal) || e.Length == prefix.Length) continue;
      var full = Path.GetFullPath(Path.Combine(dir, e[prefix.Length..]));
      var root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
      return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
    return null;
  }

  static readonly JsonSerializerOptions O = new() { PropertyNameCaseInsensitive = true };
  public static PackManifest ParseManifest(string json) => JsonSerializer.Deserialize<PackManifest>(json, O) ?? throw new InvalidDataException(Loc.T("err.packManifest"));

  /// Paketteki zamanları ve klasörleri kayda geçirir: Atölye'de bundan yeni olan modlar sonra iner.
  public static void Apply(PackManifest m, LauncherState s) {
    foreach (var (id, item) in m.Items) {
      s.ModUpdated[id] = item.Updated;
      if (item.Folders.Count > 0) s.ModFolders[id] = [.. item.Folders];
    }
  }

  /// Mod klasöründe launcher'ın kurduğu bir mod var mı (oyunun kendi koyduğu examplemod sayılmaz).
  public static bool HasMods(InstallLayout l) =>
    Directory.Exists(l.ModsDir) && Directory.EnumerateDirectories(l.ModsDir).Any(d => !Path.GetFileName(d).Equals("examplemod", StringComparison.OrdinalIgnoreCase));
}

/// Burak'ın listesini (feed) herkesin listesine taşır. Yalnız feed'deki değişiklikler uygulanır:
/// oyuncunun kendi eklediği/kapattığı modlar korunur.
public static class FeedSync {
  public static bool Apply(LauncherState s, Feed f) {
    var changed = false;
    if (f.ListMods is { } list) {
      foreach (var id in list) if (!s.FeedListSeen.Contains(id) && !s.UserMods.Contains(id)) { s.UserMods.Add(id); changed = true; }
      foreach (var id in s.FeedListSeen) if (!list.Contains(id)) changed |= s.UserMods.Remove(id);
      if (!s.FeedListSeen.SequenceEqual(list)) { s.FeedListSeen = [.. list]; changed = true; }
    }
    if (f.DisabledModIds is { } off) {
      var cmp = StringComparer.OrdinalIgnoreCase;
      foreach (var k in off) if (!s.FeedDisabledSeen.Contains(k, cmp) && !s.DisabledModIds.Contains(k, cmp)) { s.DisabledModIds.Add(k); changed = true; }
      foreach (var k in s.FeedDisabledSeen) if (!off.Contains(k, cmp)) changed |= s.DisabledModIds.RemoveAll(x => cmp.Equals(x, k)) > 0;
      if (!s.FeedDisabledSeen.SequenceEqual(off, cmp)) { s.FeedDisabledSeen = [.. off]; changed = true; }
    }
    return changed;
  }
}

/// Mod sürümü karşılaştırma: 0.1.7 > 0.1.7-rc2 > 0.1.7-rc1 > 0.1.6.1.
public static partial class ModVersion {
  [GeneratedRegex(@"^\s*v?(\d+(?:\.\d+)*)\s*(?:[-+ ]?\s*(.*))?$")] private static partial Regex Rx();
  [GeneratedRegex(@"\d+|\D+")] private static partial Regex Parts();
  public static int Compare(string a, string b) {
    var ma = Rx().Match(a); var mb = Rx().Match(b);
    if (!ma.Success || !mb.Success) return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    var na = ma.Groups[1].Value.Split('.').Select(long.Parse).ToList();
    var nb = mb.Groups[1].Value.Split('.').Select(long.Parse).ToList();
    for (int i = 0; i < Math.Max(na.Count, nb.Count); i++) {
      var c = (i < na.Count ? na[i] : 0).CompareTo(i < nb.Count ? nb[i] : 0);
      if (c != 0) return c;
    }
    var pa = ma.Groups[2].Value.Trim(); var pb = mb.Groups[2].Value.Trim();
    // ek yoksa sürümün kendisi (release), ekli olandan (rc, nightly) yenidir
    if (pa.Length == 0 || pb.Length == 0) return pa.Length == pb.Length ? 0 : pa.Length == 0 ? 1 : -1;
    var xa = Parts().Matches(pa.ToLowerInvariant()).Select(m => m.Value).ToList();
    var xb = Parts().Matches(pb.ToLowerInvariant()).Select(m => m.Value).ToList();
    for (int i = 0; i < Math.Min(xa.Count, xb.Count); i++) {
      var c = long.TryParse(xa[i], out var ia) && long.TryParse(xb[i], out var ib) ? ia.CompareTo(ib) : string.CompareOrdinal(xa[i], xb[i]);
      if (c != 0) return c;
    }
    return xa.Count.CompareTo(xb.Count);
  }
  /// Atölye sürümü yerel kopyaya yetişti mi (aynı ya da daha yeni).
  public static bool AtLeast(string? workshop, string? local) =>
    workshop is not null && (local is null || Compare(workshop, local) >= 0);
}
