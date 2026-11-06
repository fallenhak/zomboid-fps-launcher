using System.IO.Compression;
using Zfl.Core;

namespace Zfl.Core.Tests;

public class ModVersionTests {
  [Theory]
  [InlineData("0.1.7", "0.1.7-rc2", true)]
  [InlineData("0.1.7-rc2", "0.1.7-rc2", true)]
  [InlineData("0.1.7-rc3", "0.1.7-rc2", true)]
  [InlineData("0.1.7-rc10", "0.1.7-rc2", true)]
  [InlineData("0.1.6.1", "0.1.7-rc2", false)]
  [InlineData("0.1.6.9", "0.1.7-rc2", false)]
  [InlineData("0.1.7-rc1", "0.1.7-rc2", false)]
  [InlineData("0.1.8", "0.1.7", true)]
  [InlineData(null, "0.1.7", false)]
  [InlineData("0.1.7", null, true)]
  public void WorkshopCatchesUp(string? workshop, string? local, bool expected) =>
    Assert.Equal(expected, ModVersion.AtLeast(workshop, local));
}

public class ModPackTests {
  [Theory]
  [InlineData("https://drive.google.com/file/d/1AbCdEfGhIjKlMnOp/view?usp=sharing")]
  [InlineData("https://drive.google.com/open?id=1AbCdEfGhIjKlMnOp")]
  [InlineData("https://drive.google.com/uc?export=download&id=1AbCdEfGhIjKlMnOp")]
  public void DriveLinksBecomeDirect(string url) =>
    Assert.Equal("https://drive.usercontent.google.com/download?id=1AbCdEfGhIjKlMnOp&export=download&confirm=t", ModPack.DirectUrl(url));

  [Fact] public void OtherLinksStay() => Assert.Equal("https://example.com/a.zip", ModPack.DirectUrl("https://example.com/a.zip"));

  [Fact] public void ReadsDriveVirusScanPage() {
    var html = """
      <form id="download-form" action="https://drive.usercontent.google.com/download" method="get">
      <input type="submit" id="uc-download-link" value="Download anyway"/>
      <input type="hidden" name="id" value="1AbC"><input type="hidden" name="export" value="download">
      <input type="hidden" name="confirm" value="t"><input type="hidden" name="uuid" value="a-b&amp;c"></form>
      """;
    Assert.Equal("https://drive.usercontent.google.com/download?id=1AbC&export=download&confirm=t&uuid=a-b%26c", ModPack.ConfirmUrl(html));
  }

  [Fact] public void TargetsStayInsideModFolders() {
    var l = new InstallLayout(@"C:\Z");
    Assert.Equal(@"C:\Z\Kullanici\Zomboid\mods\Viewpoint\a.txt", ModPack.Target("mods/Viewpoint/a.txt", l));
    Assert.Equal(@"C:\Z\yerel-modlar\3809306528\mods\x", ModPack.Target("yerel-modlar/3809306528/mods/x", l));
    Assert.Null(ModPack.Target("mods/../../oyun/x.dll", l));
    Assert.Null(ModPack.Target("oyun/x.dll", l));
    Assert.Null(ModPack.Target("paket.json", l));
  }

  [Fact] public void ExtractsAndRecordsTimes() {
    var root = Directory.CreateTempSubdirectory("zfl").FullName; var l = new InstallLayout(root);
    var zipPath = Path.Combine(root, "p.zip");
    using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
      void Put(string n, string t) { using var w = new StreamWriter(z.CreateEntry(n).Open()); w.Write(t); }
      Put("paket.json", """{"created":5,"items":{"111":{"updated":100,"folders":["ModA","ModB"]}}}""");
      Put("mods/ModA/common/mod.info", "id=ModA");
      Put("yerel-modlar/222/mods/V/common/mod.info", "id=V");
      Put("../kaçak.txt", "x");
    }
    var m = ModPack.Extract(zipPath, l, null);
    Assert.True(File.Exists(Path.Combine(l.ModsDir, "ModA", "common", "mod.info")));
    Assert.NotNull(l.LocalItem(222));
    Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "kaçak.txt")));
    var s = new LauncherState();
    ModPack.Apply(m, s);
    Assert.Equal(100, s.ModUpdated[111]);
    Assert.Equal(["ModA", "ModB"], s.ModFolders[111]);
    Assert.True(ModPack.HasMods(l));
  }
}

public class FeedSyncTests {
  static Feed F(long[] list, string[] off) => Feed.Builtin with { ListMods = list, DisabledModIds = off };

  [Fact] public void FirstRunTakesWholeList() {
    var s = new LauncherState();
    Assert.True(FeedSync.Apply(s, F([1, 2, 3], ["X"])));
    Assert.Equal([1L, 2, 3], s.UserMods);
    Assert.Equal(["X"], s.DisabledModIds);
    Assert.False(FeedSync.Apply(s, F([1, 2, 3], ["X"])));
  }

  [Fact] public void OnlyFeedChangesApply() {
    var s = new LauncherState();
    FeedSync.Apply(s, F([1, 2, 3], ["X", "Y"]));
    // oyuncu kendi modunu ekledi, 2'yi çıkardı, Y'yi açtı, Z'yi kapattı
    s.UserMods.Add(9); s.UserMods.Remove(2); s.DisabledModIds.Remove("Y"); s.DisabledModIds.Add("Z");
    // Burak 4'ü ekledi, 3'ü çıkardı, X'i açtı, W'yi kapattı
    FeedSync.Apply(s, F([1, 2, 4], ["Y", "W"]));
    Assert.Equal([1L, 9, 4], s.UserMods);
    Assert.Equal(["Z", "W"], s.DisabledModIds);
  }

  [Fact] public void NoListInFeedChangesNothing() {
    var s = new LauncherState { UserMods = [5] };
    Assert.False(FeedSync.Apply(s, Feed.Builtin with { ListMods = null, DisabledModIds = null }));
    Assert.Equal([5L], s.UserMods);
  }
}
