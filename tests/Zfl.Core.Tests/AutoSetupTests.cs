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
