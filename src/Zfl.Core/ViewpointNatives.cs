using System.IO.Compression;
using System.Security.Cryptography;
namespace Zfl.Core;

/// Viewpoint'in DLSS / FSR 3 dosyaları: oyun menüsünde "indir" demek yerine launcher önceden koyar.
/// Adresler ve imzalar Viewpoint 0.1.6.1'in kendi listesinden (viewpoint.upscale.NativeFiles); imzası tutmayan dosyayı Viewpoint kullanmaz.
public static class ViewpointNatives {
  public sealed record Lib(string Name, string Sha256, string Url, bool InZip = false);

  const string Streamline = "https://github.com/NVIDIA-RTX/Streamline/releases/download/v2.14.1/streamline-sdk-v2.14.1.zip";
  public static readonly Lib[] Fsr = [
    new("amd_fidelityfx_vk.dll", "a1624cc4238fef046f30c4d80ce3f47be63fc5f5373f49e3ee9edb9960f54c78",
      "https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/v1.1.4/PrebuiltSignedDLL/amd_fidelityfx_vk.dll"),
  ];
  public static readonly Lib[] Dlss = [
    new("sl.interposer.dll", "8c87c9499461da561edd529aa9bf7831d67d7b94ebb1c1a5ed54ef4934e1ea4c", Streamline, true),
    new("sl.common.dll", "82924a8954dd671e09351c5de0eb87ad0eb25b944cc9f9ab955ca1d9950de15d", Streamline, true),
    new("sl.dlss.dll", "73bf52c0cfaa5900a8f3f4a91306e4625e7cca696dfb305aae44c9f97b582e1f", Streamline, true),
    new("nvngx_dlss.dll", "3975567b8943c53acce397f2b72380092f84f162d00b0d2c7d08a1025c563983",
      "https://raw.githubusercontent.com/NVIDIA/DLSS/v310.9.1/lib/Windows_x86_64/rel/nvngx_dlss.dll"),
  ];

  public static string Folder(InstallLayout l) => Path.Combine(l.ZomboidDir, "viewpoint-natives");
  static string Hash(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));
  static bool Ready(string dir, Lib x) {
    var f = Path.Combine(dir, x.Name);
    return File.Exists(f) && Hash(File.ReadAllBytes(f)) == x.Sha256;
  }

  /// Eksik ya da bozuk olanları indirir; NVIDIA kartı yoksa DLSS atlanır. Yazdıklarının adını döner.
  public static async Task<IReadOnlyList<string>> EnsureAsync(InstallLayout l, HttpClient http, bool nvidia, CancellationToken ct) {
    var dir = Folder(l); Directory.CreateDirectory(dir);
    var want = (nvidia ? Fsr.Concat(Dlss) : Fsr).Where(x => !Ready(dir, x)).ToList();
    var written = new List<string>();
    foreach (var x in want.Where(x => !x.InZip)) {
      var b = await http.GetByteArrayAsync(x.Url, ct);
      if (Hash(b) != x.Sha256) continue;
      await File.WriteAllBytesAsync(Path.Combine(dir, x.Name), b, ct);
      written.Add(x.Name);
    }
    var zipped = want.Where(x => x.InZip).ToList();
    if (zipped.Count > 0) {
      using var zip = new ZipArchive(new MemoryStream(await http.GetByteArrayAsync(Streamline, ct)));
      foreach (var x in zipped) {
        // aynı ad zip'te birkaç kez var (geliştirici sürümleri); imzası tutanı alınır
        foreach (var e in zip.Entries.Where(e => e.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase))) {
          using var ms = new MemoryStream(); using (var s = e.Open()) await s.CopyToAsync(ms, ct);
          if (Hash(ms.ToArray()) != x.Sha256) continue;
          await File.WriteAllBytesAsync(Path.Combine(dir, x.Name), ms.ToArray(), ct);
          written.Add(x.Name);
          break;
        }
      }
    }
    return written;
  }
}
