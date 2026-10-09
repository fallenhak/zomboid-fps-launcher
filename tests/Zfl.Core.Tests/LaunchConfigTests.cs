using Zfl.Core;

namespace Zfl.Core.Tests;

public class LaunchConfigTests {
  const string Orig = """{"mainClass":"zombie/gameStates/MainScreenState","classpath":[".","projectzomboid.jar"],"vmArgs":["-Djava.awt.headless=true","-Xmx3072m","-Dzomboid.steam=1","-Djava.library.path=win64/;."]}""";
  static string[] Args(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("vmArgs").EnumerateArray().Select(e => e.GetString()!).ToArray();
  [Fact] public void SetsIsolationArgs() {
    var a = Args(LaunchConfig.Apply(Orig, new InstallLayout(@"C:\Oyunlar\Zomboid FPS"), 8192));
    Assert.Contains("-Xmx8192m", a); Assert.DoesNotContain("-Xmx3072m", a);
    Assert.Contains("-Dzomboid.steam=0", a); Assert.DoesNotContain("-Dzomboid.steam=1", a);
    Assert.Contains("-Duser.home=C:/Oyunlar/Zomboid FPS/Kullanici", a);
    Assert.Contains("-agentlib:zbNative=config_dir=C:/Oyunlar/Zomboid FPS/zombie_buddy", a);
    Assert.Contains("-Djava.library.path=win64/;.", a);
  }
  [Fact] public void Idempotent() {
    var l = new InstallLayout(@"D:\Z");
    var once = LaunchConfig.Apply(Orig, l, 6144);
    Assert.Equal(Args(once), Args(LaunchConfig.Apply(once, l, 6144)));
  }
  [Fact] public void KeepsOtherKeys() =>
    Assert.Contains("MainScreenState", LaunchConfig.Apply(Orig, new InstallLayout(@"D:\Z"), 4096));
}
