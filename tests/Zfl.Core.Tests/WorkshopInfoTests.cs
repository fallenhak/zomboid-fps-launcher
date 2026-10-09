using Zfl.Core;

namespace Zfl.Core.Tests;

public class WorkshopInfoTests
{
    [Theory]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=3809306528", 3809306528L)]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=3809306528&searchtext=x", 3809306528L)]
    [InlineData(" 3809306528 ", 3809306528L)]
    [InlineData("Viewpoint", null)]
    public void ParseId(string input, long? id) => Assert.Equal(id, WorkshopInfo.ParseId(input));

    [Fact]
    public void ParsesDetailsAndSkipsOtherGames()
    {
        var json = """
        {"response":{"result":1,"resultcount":2,"publishedfiledetails":[
          {"publishedfileid":"3809306528","result":1,"consumer_app_id":108600,"title":"Project Viewpoint","file_size":"2484959","time_updated":1790000000},
          {"publishedfileid":"123456789","result":1,"consumer_app_id":440,"title":"TF2 hat","file_size":"10","time_updated":1},
          {"publishedfileid":"999999999","result":9}]}}
        """;
        var items = WorkshopInfo.Parse(json);
        Assert.Single(items);
        Assert.Equal("Project Viewpoint", items[0].Title);
        Assert.Equal(2484959, items[0].SizeBytes);
    }
}
