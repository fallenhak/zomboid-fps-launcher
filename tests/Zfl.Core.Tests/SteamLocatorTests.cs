using Zfl.Core;

namespace Zfl.Core.Tests;

public class SteamLocatorTests {
  [Fact] public void ParsesLibraries() {
    var vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";
    Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamLocator.LibraryFolders(vdf));
  }
}
public class GameMirrorTests {
  [Fact] public async Task CopiesChangedKeepsOurs() {
    var src = Directory.CreateTempSubdirectory("s").FullName; var dst = Directory.CreateTempSubdirectory("d").FullName;
    File.WriteAllText(Path.Combine(src, "a.txt"), "1");
    Directory.CreateDirectory(Path.Combine(src, "media")); File.WriteAllText(Path.Combine(src, "media", "b.txt"), "2");
    File.WriteAllText(Path.Combine(src, "ProjectZomboid64.json"), "steam");
    File.WriteAllText(Path.Combine(dst, "ProjectZomboid64.json"), "ours");
    File.WriteAllText(Path.Combine(dst, "zbNative.dll"), "zb");
    Assert.Equal(2, await GameMirror.SyncAsync(src, dst, null, default));
    Assert.Equal("ours", File.ReadAllText(Path.Combine(dst, "ProjectZomboid64.json")));
    Assert.True(File.Exists(Path.Combine(dst, "zbNative.dll")));
    Assert.Equal(0, await GameMirror.SyncAsync(src, dst, null, default));
    File.WriteAllText(Path.Combine(src, "a.txt"), "changed");
    Assert.Equal(1, await GameMirror.SyncAsync(src, dst, null, default));
  }
}
