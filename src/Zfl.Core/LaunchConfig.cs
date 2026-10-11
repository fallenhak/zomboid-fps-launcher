using System.Text.Json; using System.Text.Json.Nodes;
namespace Zfl.Core;
public static class LaunchConfig {
  static readonly string[] Ours = ["-Xmx", "-Xms", "-Dzomboid.steam=", "-Duser.home=", "-Ddeployment.user.cachedir=", "-agentlib:zbNative", "-javaagent:ZombieBuddy"];
  static string Fwd(string p) => p.Replace('\\', '/');
  public static string Apply(string originalJson, InstallLayout l, int memoryMb) {
    var root = JsonNode.Parse(originalJson)!.AsObject();
    var old = root["vmArgs"]?.AsArray().Select(n => n!.GetValue<string>()) ?? [];
    var args = new JsonArray();
    // Viewpoint ZGC ile açılınca çöp toplayıcıyı değiştirip oyunu yeniden başlatmak ister; baştan G1 kullanılır.
    foreach (var a in old.Where(a => !Ours.Any(a.StartsWith) && a is not ("-XX:+UseZGC" or "-XX:+ZGenerational"))) args.Add(a);
    if (!args.Any(a => (string?)a == "-XX:+UseG1GC")) args.Add("-XX:+UseG1GC");
    args.Add($"-Xmx{memoryMb}m");
    args.Add("-Dzomboid.steam=0");
    args.Add($"-Duser.home={Fwd(l.UserHome)}");
    args.Add($"-agentlib:zbNative=config_dir={Fwd(l.ZbConfigDir)}");
    root["vmArgs"] = args;
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
  }
}
