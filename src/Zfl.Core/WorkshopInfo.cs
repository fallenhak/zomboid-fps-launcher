using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zfl.Core;

public sealed record WorkshopItem(long Id, string Title, long SizeBytes, long UpdatedUnix);

/// Steam'in anahtarsız GetPublishedFileDetails ucu: bağlantıdan adı/boyutu gösterir.
public static partial class WorkshopInfo
{
    [GeneratedRegex(@"(?:[?&]id=)?(?<id>\d{6,12})")] private static partial Regex IdRx();

    public static long? ParseId(string input)
    {
        var m = IdRx().Match(input.Trim());
        return m.Success ? long.Parse(m.Groups["id"].Value) : null;
    }

    public static async Task<IReadOnlyList<WorkshopItem>> GetAsync(HttpClient http, IEnumerable<long> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        var form = new List<KeyValuePair<string, string>> { new("itemcount", list.Count.ToString()) };
        for (int i = 0; i < list.Count; i++) form.Add(new($"publishedfileids[{i}]", list[i].ToString()));
        using var resp = await http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", new FormUrlEncodedContent(form), ct);
        resp.EnsureSuccessStatusCode();
        return Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    public static IReadOnlyList<WorkshopItem> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<WorkshopItem>();
        foreach (var d in doc.RootElement.GetProperty("response").GetProperty("publishedfiledetails").EnumerateArray())
        {
            if (d.GetProperty("result").GetInt32() != 1) continue;
            if (d.TryGetProperty("consumer_app_id", out var app) && app.GetInt64() != SteamCmd.AppId) continue;
            result.Add(new WorkshopItem(
                long.Parse(d.GetProperty("publishedfileid").GetString()!),
                d.GetProperty("title").GetString() ?? "",
                long.TryParse(d.GetProperty("file_size").ToString(), out var s) ? s : 0,
                d.TryGetProperty("time_updated", out var t) ? t.GetInt64() : 0));
        }
        return result;
    }
}
