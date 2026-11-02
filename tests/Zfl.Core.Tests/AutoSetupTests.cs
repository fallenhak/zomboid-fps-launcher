using System.Text.Json.Nodes;
using Zfl.Core;

namespace Zfl.Core.Tests;

public class AutoSetupTests : IDisposable {
  readonly string dir = Directory.CreateTempSubdirectory("zfl").FullName;
  public void Dispose() => Directory.Delete(dir, true);
  InstallLayout L => new(dir);

  [Fact] public void JarFoundInOtherVersionFolder() {
    // Viewpoint: mod.info common'da, jar 42 klasöründe
    var m = Path.Combine(dir, "Viewpoint");
    Directory.CreateDirectory(Path.Combine(m, "common"));
    Directory.CreateDirectory(Path.Combine(m, "42", "media", "java", "client"));
    File.WriteAllText(Path.Combine(m, "common", "mod.info"), "id=Viewpoint\njavaJarFile=media/java/client/Viewpoint.jar\n");
    File.WriteAllText(Path.Combine(m, "42", "media", "java", "client", "Viewpoint.jar"), "x");
    Assert.EndsWith(Path.Combine("42", "media", "java", "client", "Viewpoint.jar"), ModInfo.Read(m)!.JarPath());
  }

  [Fact] public void ApprovalKeepsUserDecisionsAndAddsOnce() {
    var f = Path.Combine(dir, "z", "mod_approvals.json");
    Directory.CreateDirectory(Path.GetDirectoryName(f)!);
    File.WriteAllText(f, """{"formatVersion":2,"mods":[{"id":"Other","jar_hash":"aa","decision":false}]}""");
    ZbApprovals.Approve(f, [("Viewpoint", "bb")]);
    ZbApprovals.Approve(f, [("Viewpoint", "bb")]);
    var mods = JsonNode.Parse(File.ReadAllText(f))!["mods"]!.AsArray();
    Assert.Equal(2, mods.Count);
    Assert.False((bool)mods[0]!["decision"]!);
    Assert.Equal("bb", (string)mods[1]!["jar_hash"]!);
  }

  [Fact] public void HashIsSha256Hex() {
    var j = Path.Combine(dir, "a.jar"); File.WriteAllText(j, "abc");
    Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ZbApprovals.Hash(j));
  }

  [Fact] public void ApplyMarksFinishedAndKeepsOtherKeys() {
    Directory.CreateDirectory(L.ZomboidDir);
    File.WriteAllText(ViewpointSetup.SettingsFile(L), "camera.distance=2\ngraphics.pack=normal\n");
    File.WriteAllText(ViewpointSetup.OptionsFile(L), "textureCompression=false\nfov=90\n");
    ViewpointSetup.Apply(L, "High", compression: true);
    Assert.True(ViewpointSetup.Finished(L));
    Assert.Equal("2", ViewpointSetup.Get(ViewpointSetup.SettingsFile(L), "camera.distance"));
    Assert.Equal("High", ViewpointSetup.Get(ViewpointSetup.SettingsFile(L), "packPreset.normal"));
    Assert.Equal("true", ViewpointSetup.Get(ViewpointSetup.OptionsFile(L), "textureCompression"));
    Assert.Equal("90", ViewpointSetup.Get(ViewpointSetup.OptionsFile(L), "fov"));
    ViewpointSetup.Reset(L);
    Assert.False(ViewpointSetup.Finished(L));
  }

  [Theory]
  [InlineData(2048, "Potato", true)] [InlineData(8192, "High", true)] [InlineData(16384, "Ultra", false)] [InlineData(0, "High", false)]
  public void PresetByVram(long vram, string preset, bool compress) {
    Assert.Equal(preset, ViewpointSetup.PresetFor(vram));
    Assert.Equal(compress, ViewpointSetup.NeedsCompression(vram));
  }

  [Fact] public void LaunchConfigDropsZgc() {
    // oyunun gerçek dosyası: üstte G1, Windows 10+ bölümünde ZGC; ikisi birden Java'yı açmaz
    var json = LaunchConfig.Apply("""{"vmArgs":["-XX:+UseG1GC","-Xmx3072m"],"windows":{"6.1":{"vmArgs":["-XX:+UseG1GC"]},"10.0.17134":{"vmArgs":["-XX:+UseZGC"]}}}""", L, 8192);
    Assert.DoesNotContain("UseZGC", json);
    Assert.Single(System.Text.RegularExpressions.Regex.Matches(json[..json.IndexOf("windows")], "UseG1GC"));
  }
}

public class PerfTests {
  [Fact] public void SteamFlagFollowsSetting() {
    var l = new InstallLayout(@"C:\Z");
    Assert.Contains("-Dzomboid.steam=1", LaunchConfig.Apply("""{"vmArgs":[]}""", l, 4096, steam: true));
    Assert.Contains("-Dzomboid.steam=0", LaunchConfig.Apply("""{"vmArgs":["-Dzomboid.steam=1"]}""", l, 4096));
  }
  [Fact] public void SmoothAndOverlay() {
    var l = new InstallLayout(Directory.CreateTempSubdirectory("zfl").FullName);
    Directory.CreateDirectory(l.ZomboidDir);
    File.WriteAllText(ViewpointSetup.SettingsFile(l), "graphics.pack=default\npack.default.shadowResolution=2048\n");
    ViewpointSetup.ApplySmooth(l); ViewpointSetup.SetFpsOverlay(l, true);
    var f = ViewpointSetup.SettingsFile(l);
    Assert.Equal("2048", ViewpointSetup.Get(f, "pack.default.shadowResolution"));
    Assert.Equal("true", ViewpointSetup.Get(f, "chunks.buildOutOfView"));
    Assert.Equal("Compact", ViewpointSetup.Get(f, "overlay.mode"));
  }
}

public class StrayModTests {
  [Fact] public void RemovesFoldersNotInList() {
    var l = new InstallLayout(Directory.CreateTempSubdirectory("zfl").FullName);
    foreach (var n in new[] { "Viewpoint", "ViewpointTileFix" }) Directory.CreateDirectory(Path.Combine(l.ModsDir, n));
    File.WriteAllText(Path.Combine(l.ModsDir, "default.txt"), "");
    Assert.Equal(["ViewpointTileFix"], Installer.RemoveStray(l, ["viewpoint"]));
    Assert.True(Directory.Exists(Path.Combine(l.ModsDir, "Viewpoint")));
    Assert.True(File.Exists(Path.Combine(l.ModsDir, "default.txt")));
    Assert.False(Directory.Exists(Path.Combine(l.ModsDir, "ViewpointTileFix")));
  }
}

public class GameDefaultsTests {
  [Fact] public void WritesOnlyMissingFilesAndSkipsSetup() {
    var l = new InstallLayout(Directory.CreateTempSubdirectory("zfl").FullName);
    Directory.CreateDirectory(l.ZomboidDir);
    File.WriteAllText(ViewpointSetup.OptionsFile(l), "mine=1\n");
    var written = GameDefaults.ApplyIfMissing(l, "EN");
    Assert.DoesNotContain("options.ini", written);
    Assert.Equal("mine=1\n", File.ReadAllText(ViewpointSetup.OptionsFile(l)));
    Assert.True(ViewpointSetup.Finished(l));
    Assert.Null(ViewpointSetup.Get(ViewpointSetup.SettingsFile(l), "packs.ViewpointTileFix"));
  }
  [Fact] public void SetupWantedLeavesViewpointFilesToViewpoint() {
    var l = new InstallLayout(Directory.CreateTempSubdirectory("zfl").FullName);
    var written = GameDefaults.ApplyIfMissing(l, "EN", null, viewpointSetup: true);
    Assert.Contains("options.ini", written);
    Assert.False(File.Exists(ViewpointSetup.SettingsFile(l)));
    ViewpointSetup.ApplyFixes(l);
    Assert.Equal("512", ViewpointSetup.Get(ViewpointSetup.SettingsFile(l), "memory.lookMiB"));
    Assert.False(ViewpointSetup.Finished(l));
  }
  [Fact] public void LocalItemNeedsModsFolder() {
    var l = new InstallLayout(Directory.CreateTempSubdirectory("zfl").FullName);
    Assert.Null(l.LocalItem(1));
    Directory.CreateDirectory(Path.Combine(l.LocalModsDir, "1", "mods", "X"));
    Assert.Equal(Path.Combine(l.LocalModsDir, "1"), l.LocalItem(1));
  }
  [Fact] public void OptionsHaveNoMachineSpecificLines() {
    var t = GameDefaults.Read("options.ini");
    foreach (var k in new[] { "width=", "height=", "fullScreen=", "voiceRecordDeviceName=", "language=" })
      Assert.DoesNotContain("\n" + k, "\n" + t);
  }
}

public class FirstRunTests {
  [Fact] public void OptionsGetBorderlessScreenSize() {
    var l = new InstallLayout(Directory.CreateTempSubdirectory("zfl").FullName);
    GameDefaults.ApplyIfMissing(l, "EN", (2560, 1440));
    var o = ViewpointSetup.OptionsFile(l);
    Assert.Equal("true", ViewpointSetup.Get(o, "borderless"));
    Assert.Equal("false", ViewpointSetup.Get(o, "fullScreen"));
    Assert.Equal("2560", ViewpointSetup.Get(o, "width"));
    Assert.Equal("1440", ViewpointSetup.Get(o, "height"));
    Assert.Equal("240", ViewpointSetup.Get(o, "frameRate"));
  }
  [Fact] public void ModListKeepsGameFromResettingIt() {
    var l = new InstallLayout(Directory.CreateTempSubdirectory("zfl").FullName);
    new Installer(l, null!, new Progress<string>()).WriteModList([]);
    Assert.True(File.Exists(Path.Combine(l.ModsDir, "reset-mods-42_00.txt")));
  }
}

public class ViewpointNativesTests {
  [Fact(Skip = "internetten ~100 MB indirir; elle çalıştır")] public async Task DownloadsSignedFiles() {
    var l = new InstallLayout(Directory.CreateTempSubdirectory("zfl").FullName);
    var got = await ViewpointNatives.EnsureAsync(l, new HttpClient { Timeout = TimeSpan.FromMinutes(10) }, true, default);
    Assert.Equal(5, got.Count);
  }
}
