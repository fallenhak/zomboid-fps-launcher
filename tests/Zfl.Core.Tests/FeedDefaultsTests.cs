using Zfl.Core;
namespace Zfl.Core.Tests;
public class FeedDefaultsTests {
  [Fact] public void FeedCarriesOrderAndDisabled() {
    var f = Feed.Parse("""{ "gameVersion": "42.21", "launcherVersion": "0.1.0", "baseMods": [], "modOrder": ["ZombieBuddy", "Viewpoint"], "disabledModIds": ["EQUIPMENT_UI"] }""");
    Assert.Equal(["ZombieBuddy", "Viewpoint"], f.ModOrder);
    Assert.Equal(["EQUIPMENT_UI"], f.DisabledModIds);
  }
  [Fact] public void BuiltinFeedOrderHasTracerFixAfterALife() {
    var o = Feed.Builtin.ModOrder!.ToList();
    Assert.True(o.IndexOf("Viewpoint3DTracerFix") > o.IndexOf("ProjectALifeNPCs"));
  }
  [Fact] public void DefaultsGoIntoSubfolders() {
    Assert.Contains("Lua__ModOptions.ini", GameDefaults.Files);
    Assert.Equal("Lua/ModOptions.ini", GameDefaults.TargetPath("Lua__ModOptions.ini"));
  }
}
