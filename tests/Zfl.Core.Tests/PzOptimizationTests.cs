using Zfl.Core;
namespace Zfl.Core.Tests;
public class PzOptimizationTests {
  [Fact] public void ReadsRevisionFromManifest() =>
    Assert.Equal("4a0e9546ec", PzOptimization.Revision("# files written by install.ps1 - do not edit\n# revision=4a0e9546ec installed=2026-10-17T10:00:00Z\nzombie/X.class\n"));
  [Fact] public void NoRevisionLine() => Assert.Null(PzOptimization.Revision("zombie/X.class"));
  [Fact] public void PicksErrorLine() =>
    Assert.Equal("error: no release has pzopt-x-classes.zip", PzOptimization.LastError("game dir: x\nerror: no release has pzopt-x-classes.zip\n\n"));
}
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
  [Fact] public void CompatibilityProfileKeepsPlayersChoice() {
    var d = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    PzOptimization.PreferCompatibility(d);
    var f = Path.Combine(d, "pzopt", "options.ini");
    Assert.Equal("compatibility", ViewpointSetup.Get(f, "modProfile"));
    ViewpointSetup.SetKeys(f, new Dictionary<string, string> { ["modProfile"] = "performance" });
    PzOptimization.PreferCompatibility(d);
    Assert.Equal("performance", ViewpointSetup.Get(f, "modProfile"));
  }
}
