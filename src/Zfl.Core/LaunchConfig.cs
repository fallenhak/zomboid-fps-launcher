using System.Text.Json; using System.Text.Json.Nodes;
namespace Zfl.Core;
public static class LaunchConfig {
  static readonly string[] Ours = ["-Xmx", "-Xms", "-Dzomboid.steam=", "-Duser.home=", "-Ddeployment.user.cachedir=", "-agentlib:zbNative", "-javaagent:ZombieBuddy"];
  static bool IsZgc(string a) => a is "-XX:+UseZGC" or "-XX:+ZGenerational";
  static string Fwd(string p) => p.Replace('\\', '/');
  /// steam: true ise oyun Steam'e bağlanır (çok oyunculu sunucular, Shift+Tab); Steam açık olmalı.
  public static string Apply(string originalJson, InstallLayout l, int memoryMb, bool steam = false) {
    var root = JsonNode.Parse(originalJson)!.AsObject();
    var old = root["vmArgs"]?.AsArray().Select(n => n!.GetValue<string>()) ?? [];
    var args = new JsonArray();
    foreach (var a in old.Where(a => !Ours.Any(a.StartsWith) && !IsZgc(a))) args.Add(a);
    args.Add($"-Xmx{memoryMb}m");
    args.Add($"-Dzomboid.steam={(steam ? 1 : 0)}");
    args.Add($"-Duser.home={Fwd(l.UserHome)}");
    args.Add($"-agentlib:zbNative=config_dir={Fwd(l.ZbConfigDir)}");
    root["vmArgs"] = args;
    // Viewpoint ZGC ile açılınca çöp toplayıcıyı değiştirip oyunu yeniden başlatmak ister. Oyunun "windows"
    // bölümü Windows 10+ için ZGC ekliyor; G1 ile birlikte iki toplayıcı seçilince Java hiç açılmaz. Hepsinden çıkarılır.
    if (root["windows"] is JsonObject win)
      foreach (var (_, v) in win)
        if (v?["vmArgs"] is JsonArray va)
          foreach (var n in va.Where(n => IsZgc(n!.GetValue<string>())).ToList()) va.Remove(n);
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
  }
}
