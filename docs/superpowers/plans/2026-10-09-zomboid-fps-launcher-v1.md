# Zomboid FPS Launcher v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tek exe WPF launcher: Steam'deki Project Zomboid'i izole bir klasöre kopyalar, ZombieBuddy + Viewpoint + temel paketi resmi Workshop'tan SteamCMD ile indirip kurar, oyunu izole başlatır.

**Architecture:** İki proje. `Zfl.Core` (net10.0 sınıf kütüphanesi) tüm mantığı UI'sız tutar ve xUnit ile test edilir; dosya sistemi ve süreç işleri küçük arayüzlerin arkasında. `Zfl.App` (net10.0-windows WPF) yalnız ekran + Core çağrıları. Durum `<Kurulum>/launcher/state.json`'da.

**Tech Stack:** .NET 10, WPF, xUnit, System.Text.Json. Ek paket yok.

**Spec:** `docs/spec.md`

## Global Constraints
- Ürün adı: "Zomboid FPS Launcher". Arayüzde atıf: "Project Viewpoint (ellu & norkus) kullanır. Modlar resmi Steam Workshop'tan iner."
- Launcher hiçbir mod dosyası taşımaz; modlar yalnız SteamCMD ile Workshop'tan (`108600`).
- vmArgs: `-Xmx<N>m`, `-Dzomboid.steam=0`, `-Duser.home=<Kurulum>/Kullanici` (ileri eğik çizgi), `-agentlib:zbNative=config_dir=<Kurulum>/zombie_buddy`.
- Bellek varsayılanı: sistem RAM / 2, en az 4096 MB, en çok 12288 MB.
- Workshop id'leri: ZombieBuddy 3619862853, Viewpoint 3809306528.
- Mod veri yolu: `<Kurulum>/Kullanici/Zomboid/mods/<ModAdı>`; `mod.info`'dan 6 üst klasör var olmalı (ZB 2.3.4 hatası).
- Arayüz metni Türkçe, efekt yok, renkler taslaktaki gibi (#141414 pencere, #5a5a5a çizgi, #e6e6e6 yazı).
- Derleme/test: `dotnet build -m:1`, düşük öncelik (Burak oyun oynarken).

## Review Focus
- Kurulum yolu sürücü köküne çok yakın (ör. `D:\Z`) → `InstallPath.Validate` reddeder ya da `Kullanici` katmanıyla yeterli derinlik sağlar (Task 2 testi).
- Kurulum yolunda boşluk / Türkçe karakter (`C:\Oyunlar\Zomboid FPS`) → json'a doğru kaçışla yazılır (Task 3 testi).
- `mod.info` hem `42/` hem `common/` altında ya da yalnız `common/` → ikisi de okunur, `42` öncelikli (Task 4 testi).
- `ProjectZomboid64.json` zaten bizim argümanları içeriyor (ikinci çalıştırma) → argümanlar çoğalmaz (Task 3 testi).
- SteamCMD ilk çalıştırmada kendini güncelleyip "Success" yazmadan çıkar → bir kez yeniden denenir (Task 5 testi).

---

### Task 1: Çözüm iskeleti

**Files:**
- Create: `Zfl.sln`, `src/Zfl.Core/Zfl.Core.csproj`, `tests/Zfl.Core.Tests/Zfl.Core.Tests.csproj`, `.gitignore`

- [ ] **Step 1:** Komutlar:
```bash
dotnet new sln -n Zfl
dotnet new classlib -n Zfl.Core -o src/Zfl.Core -f net10.0
dotnet new xunit -n Zfl.Core.Tests -o tests/Zfl.Core.Tests -f net10.0
dotnet sln add src/Zfl.Core tests/Zfl.Core.Tests
dotnet add tests/Zfl.Core.Tests reference src/Zfl.Core
dotnet new gitignore
```
- [ ] **Step 2:** `dotnet test -m:1` → varsayılan test geçer.
- [ ] **Step 3:** Commit `chore: solution skeleton`.

### Task 2: Kurulum yolu ve bellek hesabı

**Files:** Create `src/Zfl.Core/InstallLayout.cs`, `src/Zfl.Core/Memory.cs`; Test `tests/Zfl.Core.Tests/InstallLayoutTests.cs`, `MemoryTests.cs`

**Interfaces — Produces:**
- `record InstallLayout(string Root)` özellikleri: `GameDir` = Root/oyun, `UserHome` = Root/Kullanici, `ZomboidDir` = UserHome/Zomboid, `ModsDir` = ZomboidDir/mods, `ZbConfigDir` = Root/zombie_buddy, `ToolsDir` = Root/araclar, `StateFile` = Root/launcher/state.json.
- `static string? InstallLayout.Validate(string root)` → hata metni ya da null.
- `static int Memory.DefaultMb(long totalBytes)`.

- [ ] **Step 1: Failing tests**
```csharp
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
```
- [ ] **Step 2:** `dotnet test -m:1` → derleme hatası (tipler yok).
- [ ] **Step 3: Implementation**
```csharp
namespace Zfl.Core;
public sealed record InstallLayout(string Root) {
  public string GameDir => Path.Combine(Root, "oyun");
  public string UserHome => Path.Combine(Root, "Kullanici");
  public string ZomboidDir => Path.Combine(UserHome, "Zomboid");
  public string ModsDir => Path.Combine(ZomboidDir, "mods");
  public string ZbConfigDir => Path.Combine(Root, "zombie_buddy");
  public string ToolsDir => Path.Combine(Root, "araclar");
  public string StateFile => Path.Combine(Root, "launcher", "state.json");
  public static string? Validate(string root) {
    if (!Path.IsPathFullyQualified(root)) return "Tam bir klasör yolu seç (ör. C:\\Oyunlar\\Zomboid FPS).";
    var full = Path.GetFullPath(root).TrimEnd('\\');
    if (Path.GetPathRoot(full)!.TrimEnd('\\') == full) return "Sürücünün kendisine kurulamaz, içinde bir klasör seç.";
    var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    if (!string.IsNullOrEmpty(win) && full.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return "Windows klasörüne kurulamaz.";
    return null;
  }
}
public static class Memory {
  public static int DefaultMb(long totalBytes) => (int)Math.Clamp(totalBytes / 2 / (1 << 20), 4096, 12288);
}
```
- [ ] **Step 4:** Testler geçer. **Step 5:** Commit `feat(core): install layout and memory default`.

### Task 3: Başlatma ayarı yazıcı

**Files:** Create `src/Zfl.Core/LaunchConfig.cs`; Test `tests/Zfl.Core.Tests/LaunchConfigTests.cs`

**Interfaces — Produces:** `static string LaunchConfig.Apply(string originalJson, InstallLayout l, int memoryMb)` → yeni json metni.

- [ ] **Step 1: Failing tests**
```csharp
public class LaunchConfigTests {
  const string Orig = """{"mainClass":"zombie/gameStates/MainScreenState","classpath":[".","projectzomboid.jar"],"vmArgs":["-Djava.awt.headless=true","-Xmx3072m","-Dzomboid.steam=1","-Djava.library.path=win64/;."]}""";
  static string[] Args(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("vmArgs").EnumerateArray().Select(e => e.GetString()!).ToArray();
  [Fact] public void SetsIsolationArgs() {
    var a = Args(LaunchConfig.Apply(Orig, new InstallLayout(@"C:\Oyunlar\Zomboid FPS"), 8192));
    Assert.Contains("-Xmx8192m", a); Assert.DoesNotContain("-Xmx3072m", a);
    Assert.Contains("-Dzomboid.steam=0", a); Assert.DoesNotContain("-Dzomboid.steam=1", a);
    Assert.Contains("-Duser.home=C:/Oyunlar/Zomboid FPS/Kullanici", a);
    Assert.Contains("-agentlib:zbNative=config_dir=C:/Oyunlar/Zomboid FPS/zombie_buddy", a);
    Assert.Contains("-Djava.library.path=win64/;.", a);
  }
  [Fact] public void Idempotent() {
    var l = new InstallLayout(@"D:\Z");
    var once = LaunchConfig.Apply(Orig, l, 6144);
    Assert.Equal(Args(once), Args(LaunchConfig.Apply(once, l, 6144)));
  }
  [Fact] public void KeepsOtherKeys() =>
    Assert.Contains("MainScreenState", LaunchConfig.Apply(Orig, new InstallLayout(@"D:\Z"), 4096));
}
```
- [ ] **Step 2:** Fails (yok).
- [ ] **Step 3: Implementation**
```csharp
using System.Text.Json; using System.Text.Json.Nodes;
namespace Zfl.Core;
public static class LaunchConfig {
  static readonly string[] Ours = ["-Xmx", "-Xms", "-Dzomboid.steam=", "-Duser.home=", "-Ddeployment.user.cachedir=", "-agentlib:zbNative", "-javaagent:ZombieBuddy"];
  static string Fwd(string p) => p.Replace('\\', '/');
  public static string Apply(string originalJson, InstallLayout l, int memoryMb) {
    var root = JsonNode.Parse(originalJson)!.AsObject();
    var old = root["vmArgs"]?.AsArray().Select(n => n!.GetValue<string>()) ?? [];
    var args = new JsonArray();
    foreach (var a in old.Where(a => !Ours.Any(a.StartsWith))) args.Add(a);
    args.Add($"-Xmx{memoryMb}m");
    args.Add("-Dzomboid.steam=0");
    args.Add($"-Duser.home={Fwd(l.UserHome)}");
    args.Add($"-agentlib:zbNative=config_dir={Fwd(l.ZbConfigDir)}");
    root["vmArgs"] = args;
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
  }
}
```
- [ ] **Step 4:** Pass. **Step 5:** Commit `feat(core): launch config writer`.

### Task 4: mod.info okuyucu ve mod listesi

**Files:** Create `src/Zfl.Core/ModInfo.cs`, `src/Zfl.Core/ModList.cs`; Test `ModInfoTests.cs`, `ModListTests.cs`

**Interfaces — Produces:**
- `record ModInfo(string Id, string Name, string Dir, IReadOnlyList<string> Requires, string? VersionMin, string? VersionMax, bool IsJava)`.
- `static ModInfo? ModInfo.Read(string modFolder)` — `<modFolder>/42/mod.info`, yoksa `common/mod.info`, yoksa `<modFolder>/mod.info`.
- `static bool ModInfo.SupportsGame(ModInfo m, string gameVersion)` — sayısal karşılaştırma ("42.21").
- `static IReadOnlyList<ModInfo> ModInfo.FindInWorkshopItem(string itemDir)` — `<itemDir>/mods/*`.
- `static string ModList.DefaultTxt(IEnumerable<string> modIds)` — oyunun `default.txt` biçimi.
- `static IReadOnlyList<string> ModList.Missing(IEnumerable<ModInfo> mods)` — karşılanmamış `require` id'leri.

- [ ] **Step 1: Failing tests**
```csharp
public class ModInfoTests : IDisposable {
  readonly string dir = Directory.CreateTempSubdirectory("zfl").FullName;
  public void Dispose() => Directory.Delete(dir, true);
  string Write(string rel, string text) { var p = Path.Combine(dir, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); return p; }
  [Fact] public void ReadsCommonViewpoint() {
    Write("mods/Viewpoint/common/mod.info", "name=Viewpoint\nid=Viewpoint\nversionMin=42.21\nversionMax=42.21\nrequire=\\ZombieBuddy\njavaJarFile=media/java/client/Viewpoint.jar\n");
    var m = ModInfo.Read(Path.Combine(dir, "mods/Viewpoint"))!;
    Assert.Equal("Viewpoint", m.Id); Assert.True(m.IsJava);
    Assert.Equal(["ZombieBuddy"], m.Requires);
    Assert.True(ModInfo.SupportsGame(m, "42.21")); Assert.False(ModInfo.SupportsGame(m, "42.22"));
  }
  [Fact] public void Prefers42OverCommon() {
    Write("mods/A/common/mod.info", "id=Old\nname=Old\n");
    Write("mods/A/42/mod.info", "id=New\nname=New\n");
    Assert.Equal("New", ModInfo.Read(Path.Combine(dir, "mods/A"))!.Id);
  }
  [Fact] public void NoBoundsMeansSupported() {
    Write("mods/B/42/mod.info", "id=B\nname=B\n");
    Assert.True(ModInfo.SupportsGame(ModInfo.Read(Path.Combine(dir, "mods/B"))!, "42.30"));
  }
  [Fact] public void FindsAllModsInItem() {
    Write("mods/X/42/mod.info", "id=X\n"); Write("mods/Y/common/mod.info", "id=Y\n");
    Assert.Equal(["X", "Y"], ModInfo.FindInWorkshopItem(dir).Select(m => m.Id).Order());
  }
}
public class ModListTests {
  [Fact] public void DefaultTxt() {
    var t = ModList.DefaultTxt(["ZombieBuddy", "Viewpoint"]);
    Assert.Contains("mod = \\ZombieBuddy,", t); Assert.Contains("mod = \\Viewpoint,", t);
    Assert.True(t.IndexOf("ZombieBuddy") < t.IndexOf("Viewpoint"));
  }
  [Fact] public void Missing() {
    var a = new ModInfo("Viewpoint", "Viewpoint", "", ["ZombieBuddy"], null, null, true);
    Assert.Equal(["ZombieBuddy"], ModList.Missing([a]));
    var zb = new ModInfo("ZombieBuddy", "ZombieBuddy", "", [], null, null, true);
    Assert.Empty(ModList.Missing([a, zb]));
  }
}
```
- [ ] **Step 2:** Fails.
- [ ] **Step 3: Implementation**
```csharp
namespace Zfl.Core;
public sealed record ModInfo(string Id, string Name, string Dir, IReadOnlyList<string> Requires, string? VersionMin, string? VersionMax, bool IsJava) {
  public static ModInfo? Read(string modFolder) {
    var file = new[] { "42", "common", "" }.Select(s => Path.Combine(modFolder, s, "mod.info")).FirstOrDefault(File.Exists);
    if (file is null) return null;
    var kv = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadAllLines(file)) {
      var i = line.IndexOf('='); if (i <= 0) continue;
      var k = line[..i].Trim(); var v = line[(i + 1)..].Trim();
      if (!kv.TryGetValue(k, out var l)) kv[k] = l = []; l.Add(v);
    }
    string? One(string k) => kv.TryGetValue(k, out var l) ? l[0] : null;
    var id = One("id"); if (id is null) return null;
    var req = kv.TryGetValue("require", out var r)
      ? r.SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Select(x => x.TrimStart('\\')).Where(x => x.Length > 0).ToList()
      : new List<string>();
    return new ModInfo(id, One("name") ?? id, modFolder, req, One("versionMin"), One("versionMax"), One("javaJarFile") is not null);
  }
  static Version? V(string? s) => Version.TryParse(s, out var v) ? v : null;
  public static bool SupportsGame(ModInfo m, string gameVersion) {
    var g = V(gameVersion); if (g is null) return true;
    if (V(m.VersionMin) is { } lo && g < lo) return false;
    if (V(m.VersionMax) is { } hi && g > hi) return false;
    return true;
  }
  public static IReadOnlyList<ModInfo> FindInWorkshopItem(string itemDir) {
    var mods = Path.Combine(itemDir, "mods");
    if (!Directory.Exists(mods)) return [];
    return Directory.GetDirectories(mods).Select(Read).OfType<ModInfo>().ToList();
  }
}
public static class ModList {
  public static string DefaultTxt(IEnumerable<string> modIds) =>
    "VERSION = 1,\n\nmods\n{\n" + string.Concat(modIds.Select(id => $"    mod = \\{id},\n")) + "}\n";
  public static IReadOnlyList<string> Missing(IEnumerable<ModInfo> mods) {
    var list = mods.ToList(); var have = list.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    return list.SelectMany(m => m.Requires).Where(r => !have.Contains(r)).Distinct().ToList();
  }
}
```
- [ ] **Step 4:** Pass. Not: `default.txt` biçimi Task 9'daki uçtan uca denemede oyunun yazdığı dosyayla karşılaştırılarak doğrulanır.
- [ ] **Step 5:** Commit `feat(core): mod.info parsing and mod list`.

### Task 5: SteamCMD sarmalayıcı

**Files:** Create `src/Zfl.Core/SteamCmd.cs`, `src/Zfl.Core/IProcessRunner.cs`; Test `SteamCmdTests.cs`

**Interfaces — Produces:**
- `interface IProcessRunner { Task<(int Code, string Output)> RunAsync(string exe, string args, CancellationToken ct); }` + `ProcessRunner` (gizli pencere).
- `class SteamCmd(string toolsDir, IProcessRunner run, HttpClient http)`: `Task EnsureInstalledAsync(CancellationToken)`, `Task<string> DownloadAsync(long itemId, CancellationToken)` → item klasörü yolu; `string ItemDir(long id)`.

- [ ] **Step 1: Failing tests** (sahte runner)
```csharp
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
```
- [ ] **Step 2:** Fails.
- [ ] **Step 3: Implementation**
```csharp
using System.Diagnostics; using System.IO.Compression;
namespace Zfl.Core;
public interface IProcessRunner { Task<(int Code, string Output)> RunAsync(string exe, string args, CancellationToken ct); }
public sealed class ProcessRunner : IProcessRunner {
  public async Task<(int, string)> RunAsync(string exe, string args, CancellationToken ct) {
    var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
    using var p = Process.Start(psi)!;
    var o = p.StandardOutput.ReadToEndAsync(ct); var e = p.StandardError.ReadToEndAsync(ct);
    await p.WaitForExitAsync(ct);
    return (p.ExitCode, await o + await e);
  }
}
public sealed class SteamCmdException(string m) : Exception(m);
public sealed class SteamCmd(string toolsDir, IProcessRunner run, HttpClient http) {
  public const long AppId = 108600;
  string Home => Path.Combine(toolsDir, "steamcmd");
  string Exe => Path.Combine(Home, "steamcmd.exe");
  public string ItemDir(long id) => Path.Combine(Home, "steamapps", "workshop", "content", AppId.ToString(), id.ToString());
  public async Task EnsureInstalledAsync(CancellationToken ct) {
    if (File.Exists(Exe)) return;
    Directory.CreateDirectory(Home);
    var zip = Path.Combine(Home, "steamcmd.zip");
    await File.WriteAllBytesAsync(zip, await http.GetByteArrayAsync("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", ct), ct);
    ZipFile.ExtractToDirectory(zip, Home, true);
    await run.RunAsync(Exe, "+quit", ct); // ilk çalıştırma kendini günceller
  }
  public async Task<string> DownloadAsync(long itemId, CancellationToken ct) {
    var args = $"+login anonymous +workshop_download_item {AppId} {itemId} +quit";
    string output = "";
    for (int attempt = 0; attempt < 2; attempt++) {
      (_, output) = await run.RunAsync(Exe, args, ct);
      if (output.Contains($"Success. Downloaded item {itemId}")) return ItemDir(itemId);
      if (output.Contains("ERROR")) break;
    }
    throw new SteamCmdException($"Workshop modu {itemId} indirilemedi. İnternet bağlantını kontrol edip tekrar dene.\n{output.Split('\n').LastOrDefault(l => l.Trim().Length > 0)}");
  }
}
```
- [ ] **Step 4:** Pass. **Step 5:** Commit `feat(core): steamcmd wrapper`.

### Task 6: Steam ve oyun bulucu + kopyalayıcı

**Files:** Create `src/Zfl.Core/SteamLocator.cs`, `src/Zfl.Core/GameMirror.cs`; Test `SteamLocatorTests.cs`, `GameMirrorTests.cs`

**Interfaces — Produces:**
- `static IReadOnlyList<string> SteamLocator.LibraryFolders(string libraryFoldersVdf)` — vdf metninden `"path"` değerleri.
- `static string? SteamLocator.FindGame()` — kayıt defteri `HKCU\Software\Valve\Steam\SteamPath` → `steamapps/libraryfolders.vdf` → `steamapps/common/ProjectZomboid/ProjectZomboid64.exe` olan ilk klasör.
- `static string? SteamLocator.GameVersion(string gameDir)` — `<gameDir>/media/version.txt`? yoksa null. (Task 9'da gerçek kaynak doğrulanır; yoksa `projectzomboid.jar` içindeki sürüm yerine kopyalanan `Zomboid/version.txt` kullanılır.)
- `static Task<int> GameMirror.SyncAsync(string src, string dst, IProgress<string>? p, CancellationToken ct)` → kopyalanan dosya sayısı; boyut veya değişme zamanı farklıysa kopyalar; dst'de olup src'de olmayan dosyaları silmez (zbNative.dll, ZombieBuddy.jar, ProjectZomboid64.json korunur). `ProjectZomboid64.json` asla üzerine yazılmaz (Task 7 yönetir).

- [ ] **Step 1: Failing tests**
```csharp
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
```
- [ ] **Step 2:** Fails.
- [ ] **Step 3: Implementation**
```csharp
using System.Text.RegularExpressions; using Microsoft.Win32;
namespace Zfl.Core;
public static partial class SteamLocator {
  [GeneratedRegex("\"path\"\\s+\"(?<p>[^\"]+)\"")] private static partial Regex PathRx();
  public static IReadOnlyList<string> LibraryFolders(string vdf) =>
    PathRx().Matches(vdf).Select(m => m.Groups["p"].Value.Replace(@"\\", @"\")).ToList();
  public static string? FindGame() {
    var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
    if (steam is null) return null;
    var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
    var libs = File.Exists(vdf) ? LibraryFolders(File.ReadAllText(vdf)) : [steam];
    return libs.Select(l => Path.Combine(l, "steamapps", "common", "ProjectZomboid"))
               .FirstOrDefault(d => File.Exists(Path.Combine(d, "ProjectZomboid64.exe")));
  }
}
public static class GameMirror {
  static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase) { "ProjectZomboid64.json" };
  public static Task<int> SyncAsync(string src, string dst, IProgress<string>? p, CancellationToken ct) => Task.Run(() => {
    int n = 0;
    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
      ct.ThrowIfCancellationRequested();
      var rel = Path.GetRelativePath(src, f);
      var target = Path.Combine(dst, rel);
      if (Protected.Contains(rel) && File.Exists(target)) continue;
      var s = new FileInfo(f); var t = new FileInfo(target);
      if (t.Exists && t.Length == s.Length && t.LastWriteTimeUtc == s.LastWriteTimeUtc) continue;
      Directory.CreateDirectory(t.DirectoryName!);
      File.Copy(f, target, true); File.SetLastWriteTimeUtc(target, s.LastWriteTimeUtc);
      n++; p?.Report(rel);
    }
    return n;
  }, ct);
}
```
- [ ] **Step 4:** Pass. **Step 5:** Commit `feat(core): steam locator and game mirror`.

### Task 7: Feed, durum ve kurulum orkestrası

**Files:** Create `src/Zfl.Core/Feed.cs`, `src/Zfl.Core/LauncherState.cs`, `src/Zfl.Core/Installer.cs`; `feed.json` (repo kökü); Test `FeedTests.cs`, `InstallerTests.cs`

**Interfaces — Produces:**
- `record FeedMod(long WorkshopId, string Name)`; `record Feed(string GameVersion, IReadOnlyList<FeedMod> BaseMods, string LauncherVersion, string? LauncherUrl)`; `static Feed Feed.Parse(string json)`; `static Feed Feed.Builtin` (ağ yoksa).
- `class LauncherState { string? InstallRoot; int MemoryMb; List<long> UserMods; List<long> DisabledMods; string? LastGameVersion; }` + `Load(path)` / `Save(path)`.
- `class Installer(InstallLayout l, SteamCmd steam, IProgress<string> p)`: `Task<IReadOnlyList<ModInfo>> InstallModsAsync(IEnumerable<long> ids, CancellationToken)`; `void PlaceZombieBuddy(string zbItemDir)`; `void WriteConfig(int memoryMb)`; `void WriteModList(IEnumerable<ModInfo> ordered)`.
  - Mod kopyalama: item'daki her `mods/<Ad>` klasörü `ModsDir/<Ad>`'a (önce eskisi silinir).
  - `PlaceZombieBuddy`: `<zbItem>/mods/ZombieBuddy/libs/{zbNative.dll,ZombieBuddy.jar}` → `GameDir`.
  - `WriteConfig`: `GameDir/ProjectZomboid64.json` → ilk kez `.orijinal` yedeği; `LaunchConfig.Apply` yedekten uygulanır.
  - `WriteModList`: `ModsDir/default.txt` (ZombieBuddy listeye yazılmaz — ZB onu Java agent olarak yükler; diğerleri sırayla).

`feed.json` içeriği:
```json
{ "gameVersion": "42.21", "launcherVersion": "0.1.0", "launcherUrl": null,
  "baseMods": [
    { "workshopId": 3619862853, "name": "ZombieBuddy" },
    { "workshopId": 3809306528, "name": "Project Viewpoint" },
    { "workshopId": 3810302175, "name": "6244 3D models for Viewpoint" },
    { "workshopId": 3814178814, "name": "Complete Roofs" },
    { "workshopId": 3813172420, "name": "TREE in 3D" },
    { "workshopId": 3811256204, "name": "Viewpoint QOL" } ] }
```

- [ ] **Step 1: Failing tests**
```csharp
public class FeedTests {
  [Fact] public void Parses() {
    var f = Feed.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "feed.json")));
    Assert.Equal(3619862853, f.BaseMods[0].WorkshopId); Assert.Equal("42.21", f.GameVersion);
  }
  [Fact] public void BuiltinStartsWithZbAndViewpoint() =>
    Assert.Equal([3619862853L, 3809306528L], Feed.Builtin.BaseMods.Take(2).Select(m => m.WorkshopId));
}
public class InstallerTests {
  [Fact] public void PlacesZbAndWritesConfigFromBackup() {
    var root = Directory.CreateTempSubdirectory("zfl").FullName; var l = new InstallLayout(root);
    Directory.CreateDirectory(l.GameDir);
    File.WriteAllText(Path.Combine(l.GameDir, "ProjectZomboid64.json"), """{"vmArgs":["-Xmx3072m","-Dzomboid.steam=1"]}""");
    var zb = Path.Combine(root, "zbitem"); var libs = Path.Combine(zb, "mods", "ZombieBuddy", "libs"); Directory.CreateDirectory(libs);
    File.WriteAllText(Path.Combine(libs, "zbNative.dll"), "n"); File.WriteAllText(Path.Combine(libs, "ZombieBuddy.jar"), "j");
    var i = new Installer(l, null!, new Progress<string>());
    i.PlaceZombieBuddy(zb); i.WriteConfig(8192); i.WriteConfig(6144);
    Assert.True(File.Exists(Path.Combine(l.GameDir, "zbNative.dll")));
    Assert.True(File.Exists(Path.Combine(l.GameDir, "ProjectZomboid64.json.orijinal")));
    var json = File.ReadAllText(Path.Combine(l.GameDir, "ProjectZomboid64.json"));
    Assert.Contains("-Xmx6144m", json); Assert.DoesNotContain("-Xmx8192m", json);
  }
  [Fact] public void ModListSkipsZombieBuddy() {
    var root = Directory.CreateTempSubdirectory("zfl").FullName; var l = new InstallLayout(root);
    new Installer(l, null!, new Progress<string>()).WriteModList([
      new ModInfo("ZombieBuddy","ZB","",[],null,null,true), new ModInfo("Viewpoint","V","",["ZombieBuddy"],null,null,true)]);
    var t = File.ReadAllText(Path.Combine(l.ModsDir, "default.txt"));
    Assert.DoesNotContain("ZombieBuddy,", t); Assert.Contains("\\Viewpoint,", t);
  }
}
```
Test projesine `feed.json`'u kopyala: `<None Include="..\..\feed.json" CopyToOutputDirectory="PreserveNewest" />`.
- [ ] **Step 2:** Fails.
- [ ] **Step 3: Implementation**
```csharp
using System.Text.Json;
namespace Zfl.Core;
public sealed record FeedMod(long WorkshopId, string Name);
public sealed record Feed(string GameVersion, IReadOnlyList<FeedMod> BaseMods, string LauncherVersion, string? LauncherUrl) {
  static readonly JsonSerializerOptions O = new() { PropertyNameCaseInsensitive = true };
  public static Feed Parse(string json) => JsonSerializer.Deserialize<Feed>(json, O) ?? throw new InvalidDataException("feed boş");
  public const string Url = "https://raw.githubusercontent.com/fallenhak/zomboid-fps-launcher/main/feed.json";
  public static Feed Builtin { get; } = new("42.21", [
    new(3619862853, "ZombieBuddy"), new(3809306528, "Project Viewpoint"), new(3810302175, "6244 3D models for Viewpoint"),
    new(3814178814, "Complete Roofs"), new(3813172420, "TREE in 3D"), new(3811256204, "Viewpoint QOL")], "0.1.0", null);
}
public sealed class LauncherState {
  public string? InstallRoot { get; set; }
  public int MemoryMb { get; set; }
  public List<long> UserMods { get; set; } = [];
  public List<long> DisabledMods { get; set; } = [];
  public string? LastGameVersion { get; set; }
  static readonly JsonSerializerOptions O = new() { WriteIndented = true };
  public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZomboidFpsLauncher", "state.json");
  public static LauncherState Load(string path) {
    try { return JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(path)) ?? new(); } catch { return new(); }
  }
  public void Save(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(this, O)); }
}
public sealed class Installer(InstallLayout l, SteamCmd steam, IProgress<string> p) {
  public async Task<IReadOnlyList<ModInfo>> InstallModsAsync(IEnumerable<long> ids, CancellationToken ct) {
    var result = new List<ModInfo>();
    foreach (var id in ids) {
      p.Report($"İndiriliyor: {id}");
      var item = await steam.DownloadAsync(id, ct);
      if (id == 3619862853) PlaceZombieBuddy(item);
      foreach (var m in ModInfo.FindInWorkshopItem(item)) {
        var dst = Path.Combine(l.ModsDir, Path.GetFileName(m.Dir));
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        CopyDir(m.Dir, dst);
        result.Add(ModInfo.Read(dst)!);
      }
    }
    return result;
  }
  public void PlaceZombieBuddy(string zbItemDir) {
    var libs = Path.Combine(zbItemDir, "mods", "ZombieBuddy", "libs");
    Directory.CreateDirectory(l.GameDir);
    foreach (var f in new[] { "zbNative.dll", "ZombieBuddy.jar" }) File.Copy(Path.Combine(libs, f), Path.Combine(l.GameDir, f), true);
  }
  public void WriteConfig(int memoryMb) {
    var cfg = Path.Combine(l.GameDir, "ProjectZomboid64.json"); var bak = cfg + ".orijinal";
    if (!File.Exists(bak)) File.Copy(cfg, bak);
    Directory.CreateDirectory(l.ZbConfigDir);
    File.WriteAllText(cfg, LaunchConfig.Apply(File.ReadAllText(bak), l, memoryMb));
  }
  public void WriteModList(IEnumerable<ModInfo> ordered) {
    Directory.CreateDirectory(l.ModsDir);
    File.WriteAllText(Path.Combine(l.ModsDir, "default.txt"), ModList.DefaultTxt(ordered.Where(m => !m.Id.Equals("ZombieBuddy", StringComparison.OrdinalIgnoreCase)).Select(m => m.Id)));
  }
  static void CopyDir(string src, string dst) {
    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
      var t = Path.Combine(dst, Path.GetRelativePath(src, f)); Directory.CreateDirectory(Path.GetDirectoryName(t)!); File.Copy(f, t, true);
    }
  }
}
```
- [ ] **Step 4:** Pass. **Step 5:** Commit `feat(core): feed, state, installer`.

### Task 8: WPF uygulaması

**Files:** Create `src/Zfl.App/Zfl.App.csproj` (net10.0-windows, UseWPF, PublishSingleFile, SelfContained, win-x64), `App.xaml`, `MainWindow.xaml(.cs)`, `Theme.xaml`, `BugReport.cs`, `GameRunner.cs`

Ekranlar taslak v4'e göre: başlık "Zomboid FPS Launcher"; sekmeler Oyna / Modlar / Ayarlar / Hata bildir; alt satır atıf + durum mesajı.
- İlk açılış (state'te InstallRoot yok): Oyna sekmesinde kurulum kutusu — Steam'de bulunan oyun yolu, kurulum klasörü (varsayılan `C:\Zomboid FPS`), gereken alan (oyun klasörü boyutu + 1 GB), "Kur" düğmesi. `InstallLayout.Validate` hatası kutunun altında.
- Kurulum sırası: `GameMirror.SyncAsync(steamGame, GameDir)` → `SteamCmd.EnsureInstalledAsync` → `Installer.InstallModsAsync(feed.BaseMods + UserMods − Disabled)` → `WriteConfig` → `WriteModList` (sıra: feed sırası, sonra kullanıcı modları). İlerleme satırı ilerleme çubuğunun altında.
- Oyna: Steam oyunu bulunursa Sync; feed indirilemezse `Feed.Builtin` + "Güncellemeler kontrol edilemedi"; tüm modlar yeniden indirilir (SteamCMD değişmeyenleri hızla geçer); oyun sürümü feed'den farklıysa satır içi uyarı + "Yine de oyna"; `GameRunner.Start(GameDir)` → `ProjectZomboid64.exe`, çalışma klasörü GameDir; 20 sn içinde çıkarsa "Oyun hemen kapandı" + Hata bildir sekmesine geçiş.
- Modlar: liste (temel = kilitli), bağlantı/numara kutusu (`https://steamcommunity.com/sharedfiles/filedetails/?id=N` veya N) → "Ekle" → indir, `ModInfo` kontrolleri (Java → uyarı satırı; sürüm uyumsuz → uyarı; `ModList.Missing` → "Şunlar da gerekli: …"); onay kutusu = Disabled; "Seçiliyi kaldır".
- Ayarlar: bellek kaydırıcısı 4–12 GB (değişince `WriteConfig`), klasörler, "Kurulum klasörünü aç".
- Hata bildir: `BugReport.Collect` (Viewpoint/ZB sürümü mod.info `modversion`'dan, oyun sürümü, GPU `Win32_VideoController` Name+DriverVersion WMI yerine `dxdiag` yok — `System.Management` paketi gerekmesin diye kayıt defteri `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-...}\0000` DriverDesc/DriverVersion), "Kopyala", "console.txt klasörünü aç" (`ZomboidDir`).

- [ ] **Step 1:** Projeyi oluştur ve sln'e ekle; `dotnet build -m:1`.
- [ ] **Step 2:** Tema + pencere XAML'i, kod-arkası.
- [ ] **Step 3:** Derle, açılışta çökmediğini Burak oyun oynamıyorken dene.
- [ ] **Step 4:** Commit `feat(app): WPF launcher UI`.

### Task 9: Uçtan uca deneme ve sürüm

- [ ] Burak oyunda değilken: `dotnet publish src/Zfl.App -c Release` → exe; temiz klasöre kurulum; oyun açılır, O tuşu çalışır; `default.txt` biçimini oyunun ürettiğiyle karşılaştır; `GameVersion` kaynağını doğrula.
- [ ] GitHub repo `fallenhak/zomboid-fps-launcher` (özel), push, `v0.1.0` release + exe.
