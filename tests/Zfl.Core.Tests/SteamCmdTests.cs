using Zfl.Core;

namespace Zfl.Core.Tests;

public class SteamCmdTests {
  class FakeRunner(params string[] outputs) : IProcessRunner {
    public int Calls; public string? LastArgs;
    public Task<(int, string)> RunAsync(string exe, string args, CancellationToken ct) { LastArgs = args; return Task.FromResult((0, outputs[Math.Min(Calls++, outputs.Length - 1)])); }
  }
  [Fact] public async Task RetriesOnceWhenSelfUpdateEatsFirstRun() {
    var dir = Directory.CreateTempSubdirectory("zfl").FullName;
    var r = new FakeRunner("Update complete, launching...", "Success. Downloaded item 3809306528 to \"x\"");
    var s = new SteamCmd(dir, r, new HttpClient());
    await s.DownloadAsync(3809306528, default);
    Assert.Equal(2, r.Calls);
    Assert.Contains("+login anonymous +workshop_download_item 108600 3809306528 +quit", r.LastArgs);
  }
  [Fact] public async Task ThrowsReadableErrorOnFailure() {
    var s = new SteamCmd(Directory.CreateTempSubdirectory("zfl").FullName, new FakeRunner("ERROR! Download item 1 failed (Failure)."), new HttpClient());
    var e = await Assert.ThrowsAsync<SteamCmdException>(() => s.DownloadAsync(1, default));
    Assert.Contains("1", e.Message);
  }
  [Fact] public void ItemDir() =>
    Assert.EndsWith(Path.Combine("steamapps", "workshop", "content", "108600", "42"), new SteamCmd(@"C:\t", new FakeRunner(""), new HttpClient()).ItemDir(42));
}
