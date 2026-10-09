using Zfl.Core;

namespace Zfl.Core.Tests;

public class InstallLayoutTests {
  [Fact] public void Paths() {
    var l = new InstallLayout(@"C:\Oyunlar\Zomboid FPS");
    Assert.Equal(@"C:\Oyunlar\Zomboid FPS\Kullanici\Zomboid\mods", l.ModsDir);
    Assert.Equal(@"C:\Oyunlar\Zomboid FPS\oyun", l.GameDir);
  }
  [Theory]
  [InlineData(@"C:\", false)]
  [InlineData(@"C:\Windows\X", false)]
  [InlineData(@"relative\x", false)]
  [InlineData(@"D:\Z", true)]
  [InlineData(@"C:\Oyunlar\Zomboid FPS", true)]
  public void Validate(string root, bool ok) => Assert.Equal(ok, InstallLayout.Validate(root) is null);
  [Fact] public void ModInfoDepthIsEnoughForZombieBuddy() {
    // mod.info = Mods/<Mod>/42/mod.info ; ZB: parent(mod.info), 4 kez daha parent, sonra getParent().getFileName() — null ise NPE
    var l = new InstallLayout(@"D:\Z");
    var info = Path.Combine(l.ModsDir, "Viewpoint", "42", "mod.info");
    var p = Path.GetDirectoryName(info)!;
    for (int i = 0; i < 4; i++) p = Path.GetDirectoryName(p)!;
    Assert.False(string.IsNullOrEmpty(Path.GetFileName(Path.GetDirectoryName(p))));
  }
}
public class MemoryTests {
  [Theory]
  [InlineData(8L<<30, 4096)] [InlineData(16L<<30, 8192)] [InlineData(64L<<30, 12288)] [InlineData(4L<<30, 4096)]
  public void Default(long total, int mb) => Assert.Equal(mb, Memory.DefaultMb(total));
}
