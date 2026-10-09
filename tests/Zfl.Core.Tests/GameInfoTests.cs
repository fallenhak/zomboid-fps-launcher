using Zfl.Core;

namespace Zfl.Core.Tests;

public class GameInfoTests
{
    [Fact]
    public void ReadsBuildIdFromManifest()
    {
        var lib = Directory.CreateTempSubdirectory("lib").FullName;
        var game = Path.Combine(lib, "common", "ProjectZomboid");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(lib, "appmanifest_108600.acf"), "\"AppState\"\n{\n\t\"buildid\"\t\t\"25485521\"\n}");
        Assert.Equal("25485521", GameInfo.BuildId(game));
    }

    [Theory]
    [InlineData("42.21.0 4a0e9546ec", "42.21")]
    [InlineData("42.22.1\n", "42.22")]
    [InlineData("garbage", null)]
    public void ShortVersion(string text, string? expected) => Assert.Equal(expected, GameInfo.ShortVersion(text));
}
