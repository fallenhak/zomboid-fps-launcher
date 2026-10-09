using Zfl.Core;

namespace Zfl.Core.Tests;

public class FeedTests {
  [Fact] public void Parses() {
    var f = Feed.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "feed.json")));
    Assert.Equal(3619862853, f.BaseMods[0].WorkshopId); Assert.Equal("42.21", f.GameVersion);
  }
  [Fact] public void BuiltinStartsWithZbAndViewpoint() =>
    Assert.Equal([3619862853L, 3809306528L], Feed.Builtin.BaseMods.Take(2).Select(m => m.WorkshopId));
}
public class InstallerTests {
  [Fact] public void PlacesZbAndWritesConfigFromBackup() {
    var root = Directory.CreateTempSubdirectory("zfl").FullName; var l = new InstallLayout(root);
    Directory.CreateDirectory(l.GameDir);
    File.WriteAllText(Path.Combine(l.GameDir, "ProjectZomboid64.json"), """{"vmArgs":["-Xmx3072m","-Dzomboid.steam=1"]}""");
    var zb = Path.Combine(root, "zbitem"); var libs = Path.Combine(zb, "mods", "ZombieBuddy", "libs"); Directory.CreateDirectory(libs);
    File.WriteAllText(Path.Combine(libs, "zbNative.dll"), "n"); File.WriteAllText(Path.Combine(libs, "ZombieBuddy.jar"), "j");
    var i = new Installer(l, null!, new Progress<string>());
    i.PlaceZombieBuddy(zb); i.WriteConfig(8192); i.WriteConfig(6144);
    Assert.True(File.Exists(Path.Combine(l.GameDir, "zbNative.dll")));
    Assert.True(File.Exists(Path.Combine(l.GameDir, "ProjectZomboid64.json.orijinal")));
    var json = File.ReadAllText(Path.Combine(l.GameDir, "ProjectZomboid64.json"));
    Assert.Contains("-Xmx6144m", json); Assert.DoesNotContain("-Xmx8192m", json);
  }
  [Fact] public void ModListSkipsZombieBuddy() {
    var root = Directory.CreateTempSubdirectory("zfl").FullName; var l = new InstallLayout(root);
    new Installer(l, null!, new Progress<string>()).WriteModList([
      new ModInfo("ZombieBuddy","ZB","",[],null,null,true), new ModInfo("Viewpoint","V","",["ZombieBuddy"],null,null,true)]);
    var t = File.ReadAllText(Path.Combine(l.ModsDir, "default.txt"));
    Assert.DoesNotContain("ZombieBuddy,", t); Assert.Contains("\\Viewpoint,", t);
  }
}
