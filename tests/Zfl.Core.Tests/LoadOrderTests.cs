using Zfl.Core;

namespace Zfl.Core.Tests;

public class LoadOrderTests {
  static ModInfo M(string id, string[]? req = null, string[]? after = null, string[]? before = null, string[]? inc = null) =>
    new(id, id, "", req ?? [], null, null, false) { LoadAfter = after ?? [], LoadBefore = before ?? [], Incompatible = inc ?? [] };
  static string[] Ids(IEnumerable<ModInfo> ms) => ms.Select(m => m.Id).ToArray();

  [Fact] public void KeepsGivenOrderWhenRulesAllow() =>
    Assert.Equal(["ZB", "VP", "A", "B"], Ids(LoadOrder.Sort([M("ZB"), M("VP", ["ZB"]), M("A", ["VP"]), M("B", ["VP"])])));
  [Fact] public void MovesModBelowItsRequirement() =>
    Assert.Equal(["ZB", "VP", "A"], Ids(LoadOrder.Sort([M("A", ["VP"]), M("ZB"), M("VP", ["ZB"])])));
  [Fact] public void HonorsLoadModAfterAndBefore() {
    Assert.Equal(["VP", "Hills"], Ids(LoadOrder.Sort([M("Hills", after: ["VP"]), M("VP")])));
    Assert.Equal(["X", "Y"], Ids(LoadOrder.Sort([M("Y"), M("X", before: ["Y"])])));
  }
  [Fact] public void IgnoresRulesAboutMissingMods() =>
    Assert.Equal(["B", "A"], Ids(LoadOrder.Sort([M("B", after: ["Weather"]), M("A")])));
  [Fact] public void SurvivesCycles() => Assert.Equal(2, LoadOrder.Sort([M("A", ["B"]), M("B", ["A"])]).Count);
  [Fact] public void FindsIncompatiblePairs() =>
    Assert.Single(LoadOrder.Conflicts([M("Move", inc: ["MoveDev"]), M("MoveDev")]));

  [Fact] public void MergeKeepsSavedOrderAndPlacesNewOnesAfterTheirDefaultNeighbour() =>
    Assert.Equal(["ZB", "VP", "C", "New", "A"], LoadOrder.Merge(["ZB", "VP", "A", "C", "New"], ["ZB", "VP", "C", "A"]));
  [Fact] public void MergeDropsRemovedMods() =>
    Assert.Equal(["ZB", "VP"], LoadOrder.Merge(["ZB", "VP"], ["ZB", "Gone", "VP"]));
  [Fact] public void MergeWithNothingSavedIsDefault() =>
    Assert.Equal(["ZB", "VP", "A"], LoadOrder.Merge(["ZB", "VP", "A"], []));

  [Fact] public void BuiltinFeedLocksZbAndViewpoint() =>
    Assert.All(Feed.Builtin.BaseMods.Take(2), m => Assert.True(m.Locked));
}

public class MoveBlockTests {
  static readonly string[] K = ["a", "b", "c", "d", "e"];
  [Fact] public void ShiftUpKeepsGap() => Assert.Equal(["a", "c", "b", "e", "d"], Zfl.Core.LoadOrder.Shift(K, ["c", "e"], -1));
  [Fact] public void ShiftStopsAtTop() => Assert.Equal(K, Zfl.Core.LoadOrder.Shift(K, ["a", "c"], -1));
  [Fact] public void MoveToDropsBlockBeforeTarget() => Assert.Equal(["a", "d", "e", "b", "c"], Zfl.Core.LoadOrder.MoveTo(K, ["d", "e"], 1));
  [Fact] public void MoveToEnd() => Assert.Equal(["b", "c", "d", "e", "a"], Zfl.Core.LoadOrder.MoveTo(K, ["a"], 5));
}
