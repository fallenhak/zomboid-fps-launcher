using Zfl.Core;

namespace Zfl.Core.Tests;

public class VersionFolderTests : IDisposable {
  readonly string dir = Directory.CreateTempSubdirectory("zfl").FullName;
  public void Dispose() => Directory.Delete(dir, true);
  [Fact] public void ReadsModInfoFromVersionedFolder() {
    // 6244 3D models paketi: mod.info yalnız 42.20.4 klasöründe
    var m = Path.Combine(dir, "PZVoxelStudioViewpoint");
    Directory.CreateDirectory(Path.Combine(m, "42.20.4")); Directory.CreateDirectory(Path.Combine(m, "common", "media"));
    File.WriteAllText(Path.Combine(m, "42.20.4", "mod.info"), "id=PZVoxelStudioViewpoint\nname=Voxel\n");
    Assert.Equal("PZVoxelStudioViewpoint", ModInfo.Read(m)!.Id);
  }
  [Fact] public void HighestVersionFolderWins() {
    var m = Path.Combine(dir, "A");
    foreach (var (v, id) in new[] { ("42", "Base"), ("42.20.4", "Newer"), ("common", "Common") }) {
      Directory.CreateDirectory(Path.Combine(m, v)); File.WriteAllText(Path.Combine(m, v, "mod.info"), $"id={id}\n");
    }
    Assert.Equal("Newer", ModInfo.Read(m)!.Id);
  }
}

public class FallbackTests : IDisposable {
  readonly string dir = Directory.CreateTempSubdirectory("zfl").FullName;
  public void Dispose() => Directory.Delete(dir, true);
  class FailRunner : IProcessRunner {
    public Task<(int, string)> RunAsync(string exe, string args, CancellationToken ct) => Task.FromResult((1, "ERROR! Download item 1 failed (File Not Found)."));
  }
  [Fact] public async Task UsesLocalCopyWhenWorkshopItemIsGone() {
    var l = new InstallLayout(Path.Combine(dir, "kurulum"));
    var backup = Path.Combine(dir, "steamworkshop");
    var mi = Path.Combine(backup, "1", "mods", "Viewpoint", "common", "mod.info");
    Directory.CreateDirectory(Path.GetDirectoryName(mi)!); File.WriteAllText(mi, "id=Viewpoint\nname=Viewpoint\n");
    var steam = new SteamCmd(l.ToolsDir, new FailRunner(), new HttpClient());
    var got = await new Installer(l, steam, new Progress<string>(), [backup]).InstallModsAsync([1], default);
    Assert.Equal("Viewpoint", got.Single().Id);
    Assert.True(File.Exists(Path.Combine(l.ModsDir, "Viewpoint", "common", "mod.info")));
  }
  [Fact] public async Task StillFailsWhenNoCopyAnywhere() {
    var l = new InstallLayout(Path.Combine(dir, "kurulum"));
    var steam = new SteamCmd(l.ToolsDir, new FailRunner(), new HttpClient());
    await Assert.ThrowsAsync<SteamCmdException>(() => new Installer(l, steam, new Progress<string>()).InstallModsAsync([1], default));
  }
}

public class LocTests {
  [Fact] public void BothLanguagesHaveSameKeys() => Assert.Equal(Loc.Keys("en").Order(), Loc.Keys("tr").Order());
  [Fact] public void UnknownSavedLanguageFallsBackToSystem() => Assert.Equal(Loc.SystemDefault(), Loc.Resolve("de"));
  [Fact] public void SavedLanguageWins() => Assert.Equal("en", Loc.Resolve("en"));
}
