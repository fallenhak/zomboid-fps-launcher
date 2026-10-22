using Zfl.Core;
namespace Zfl.Core.Tests;
public class PzOptimizationTests {
  [Fact] public void ReadsRevisionFromManifest() =>
    Assert.Equal("4a0e9546ec", PzOptimization.Revision("# files written by install.ps1 - do not edit\n# revision=4a0e9546ec installed=2026-10-17T10:00:00Z\nzombie/X.class\n"));
  [Fact] public void NoRevisionLine() => Assert.Null(PzOptimization.Revision("zombie/X.class"));
  [Fact] public void PicksErrorLine() =>
    Assert.Equal("error: no release has pzopt-x-classes.zip", PzOptimization.LastError("game dir: x\nerror: no release has pzopt-x-classes.zip\n\n"));
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
