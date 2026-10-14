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

public class BatchDownloadTests {
  class Runner(Func<string, string> answer) : IProcessRunner {
    public List<string> Args = [];
    public Task<(int, string)> RunAsync(string exe, string args, CancellationToken ct) { Args.Add(args); return Task.FromResult((0, answer(args))); }
  }
  [Fact] public async Task OneLoginForAllMods() {
    var r = new Runner(_ => "Success. Downloaded item 1 to \"x\"\nSuccess. Downloaded item 2 to \"y\"");
    var (done, _) = await new SteamCmd(Directory.CreateTempSubdirectory("zfl").FullName, r, new HttpClient()).DownloadManyAsync([1, 2], default);
    Assert.Single(r.Args);
    Assert.Equal("+login anonymous +workshop_download_item 108600 1 +workshop_download_item 108600 2 +quit", r.Args[0]);
    Assert.Equal([1L, 2L], done.Order());
  }
  [Fact] public async Task RetriesOnlyTheOnesThatDidNotCome() {
    var r = new Runner(a => a.Contains(" 1 ") ? "Success. Downloaded item 1 to \"x\"" : "Success. Downloaded item 2 to \"y\"");
    var (done, _) = await new SteamCmd(Directory.CreateTempSubdirectory("zfl").FullName, r, new HttpClient()).DownloadManyAsync([1, 2], default);
    Assert.Equal(2, r.Args.Count);
    Assert.DoesNotContain(" 1 ", r.Args[1]);
    Assert.Equal([1L, 2L], done.Order());
  }
}

public class SyncDirTests : IDisposable {
  readonly string dir = Directory.CreateTempSubdirectory("zfl").FullName;
  public void Dispose() => Directory.Delete(dir, true);
  [Fact] public void CopiesChangedSkipsSameDeletesGone() {
    string src = Path.Combine(dir, "src"), dst = Path.Combine(dir, "dst");
    Directory.CreateDirectory(Path.Combine(src, "a")); Directory.CreateDirectory(Path.Combine(dst, "old"));
    File.WriteAllText(Path.Combine(src, "a", "same.txt"), "x"); File.WriteAllText(Path.Combine(src, "new.txt"), "yeni");
    Installer.SyncDir(src, dst);
    Directory.CreateDirectory(Path.Combine(dst, "old")); File.WriteAllText(Path.Combine(dst, "old", "gone.txt"), "eski");
    var sameTime = File.GetLastWriteTimeUtc(Path.Combine(dst, "a", "same.txt"));
    File.WriteAllText(Path.Combine(src, "new.txt"), "değişti");
    int n = 0;
    Installer.SyncDir(src, dst, () => Interlocked.Increment(ref n));
    Assert.Equal(2, n);
    Assert.Equal("değişti", File.ReadAllText(Path.Combine(dst, "new.txt")));
    Assert.Equal(sameTime, File.GetLastWriteTimeUtc(Path.Combine(dst, "a", "same.txt")));
    Assert.False(Directory.Exists(Path.Combine(dst, "old")));
  }
}

public class ShaderPackTests : IDisposable {
  readonly string dir = Directory.CreateTempSubdirectory("zfl").FullName;
  public void Dispose() => Directory.Delete(dir, true);
  InstallLayout L => new(Path.Combine(dir, "kurulum"));
  string Zip(string name, string entry) {
    var z = Path.Combine(dir, name);
    using var a = System.IO.Compression.ZipFile.Open(z, System.IO.Compression.ZipArchiveMode.Create);
    a.CreateEntry(entry);
    return z;
  }
  [Fact] public void AddsMinecraftZipAndLists() {
    Assert.Equal("BSL v8", ShaderPacks.Add(L, Zip("BSL v8.zip", "shaders/final.fsh")));
    var p = Assert.Single(ShaderPacks.List(L));
    Assert.True(p.IsZip);
    ShaderPacks.Remove(p);
    Assert.Empty(ShaderPacks.List(L));
  }
  [Fact] public void RejectsZipThatIsNotAPack() =>
    Assert.Throws<ShaderPackException>(() => ShaderPacks.Add(L, Zip("foto.zip", "resim.png")));
  [Fact] public void ViewpointFolderGetsSafeName() {
    var src = Path.Combine(dir, "Güzel Paket!");
    Directory.CreateDirectory(src); File.WriteAllText(Path.Combine(src, "pack.properties"), "x");
    var name = ShaderPacks.Add(L, src);
    Assert.Matches("^[A-Za-z0-9_-]+$", name);
    Assert.True(File.Exists(Path.Combine(ShaderPacks.Dir(L), name, "pack.properties")));
    Assert.Throws<ShaderPackException>(() => ShaderPacks.Add(L, src));
  }
}
