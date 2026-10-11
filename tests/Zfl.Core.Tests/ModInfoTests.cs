using Zfl.Core;

namespace Zfl.Core.Tests;

public class ModInfoTests : IDisposable {
  readonly string dir = Directory.CreateTempSubdirectory("zfl").FullName;
  public void Dispose() => Directory.Delete(dir, true);
  string Write(string rel, string text) { var p = Path.Combine(dir, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); return p; }
  [Fact] public void ReadsCommonViewpoint() {
    Write("mods/Viewpoint/common/mod.info", "name=Viewpoint\nid=Viewpoint\nversionMin=42.21\nversionMax=42.21\nrequire=\\ZombieBuddy\njavaJarFile=media/java/client/Viewpoint.jar\n");
    var m = ModInfo.Read(Path.Combine(dir, "mods/Viewpoint"))!;
    Assert.Equal("Viewpoint", m.Id); Assert.True(m.IsJava);
    Assert.Equal(["ZombieBuddy"], m.Requires);
    Assert.True(ModInfo.SupportsGame(m, "42.21")); Assert.False(ModInfo.SupportsGame(m, "42.22"));
  }
  [Fact] public void Prefers42OverCommon() {
    Write("mods/A/common/mod.info", "id=Old\nname=Old\n");
    Write("mods/A/42/mod.info", "id=New\nname=New\n");
    Assert.Equal("New", ModInfo.Read(Path.Combine(dir, "mods/A"))!.Id);
  }
  [Fact] public void NoBoundsMeansSupported() {
    Write("mods/B/42/mod.info", "id=B\nname=B\n");
    Assert.True(ModInfo.SupportsGame(ModInfo.Read(Path.Combine(dir, "mods/B"))!, "42.30"));
  }
  [Fact] public void FindsAllModsInItem() {
    Write("mods/X/42/mod.info", "id=X\n"); Write("mods/Y/common/mod.info", "id=Y\n");
    Assert.Equal(["X", "Y"], ModInfo.FindInWorkshopItem(dir).Select(m => m.Id).Order());
  }
}
public class ModListTests {
  [Fact] public void DefaultTxt() {
    var t = ModList.DefaultTxt(["ZombieBuddy", "Viewpoint"]);
    Assert.Contains("mod = ZombieBuddy,", t); Assert.Contains("mod = Viewpoint,", t); Assert.DoesNotContain("\\", t);
    Assert.True(t.IndexOf("ZombieBuddy") < t.IndexOf("Viewpoint"));
  }
  [Fact] public void Missing() {
    var a = new ModInfo("Viewpoint", "Viewpoint", "", ["ZombieBuddy"], null, null, true);
    Assert.Equal(["ZombieBuddy"], ModList.Missing([a]));
    var zb = new ModInfo("ZombieBuddy", "ZombieBuddy", "", [], null, null, true);
    Assert.Empty(ModList.Missing([a, zb]));
  }
}
