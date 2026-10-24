using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unit;

namespace Unciv.Core.Tests.Logic.Map;

// T126: PromotionTree (core/.../pickerscreens/PromotionTree.kt, no Kotlin tests). The tree is built from AvailablePromotion lists as the
// MapUnit constructor would; the MapUnit constructor itself is covered by CityConstructionsTests.CompleteConstruction_AppliesSavedPromotions.
public class PromotionTreeTests
{
    private static Promotion P(string name, int row = -1, int column = 0, string[]? prerequisites = null, string[]? uniques = null) =>
        new() { Name = name, Row = row, Column = column, Prerequisites = [.. prerequisites ?? []], Uniques = [.. uniques ?? []] };

    private static AvailablePromotion A(Promotion promotion, bool adopted = false, bool blocked = false) => new(promotion, adopted, blocked);

    private static PromotionTree Tree(IEnumerable<AvailablePromotion> list, Func<int, bool>? afford = null) =>
        new([.. list], afford ?? (_ => true), StringComparer.Ordinal);

    [Fact]
    public void PossiblePromotions_AreSortedByColumnRowThenName_WithRowZeroFirstAndUnpositionedLast()
    {
        var tree = Tree([
            A(P("Zed")), A(P("Alpha")),
            A(P("Col2", row: 1, column: 2)), A(P("Col1", row: 3, column: 1)), A(P("Col1b", row: 2, column: 1)),
            A(P("Top", row: 0, column: 5)), A(P("Top0", row: 0, column: 0)),
        ]);
        Assert.Equal(["Top0", "Top", "Col1b", "Col1", "Col2", "Alpha", "Zed"], tree.PossiblePromotions.Select(it => it.Name));
        Assert.Equal(tree.PossiblePromotions.Select(it => it.Name), tree.AllNodes().Select(it => it.ToString()));
    }

    [Fact]
    public void Nodes_LinkParentsAndChildren_IgnoringUnknownPrerequisites_AndComputeLevels()
    {
        var drill1 = P("Drill I", 1, 1);
        var drill2 = P("Drill II", 1, 2, ["Drill I"]);
        var drill3 = P("Drill III", 1, 3, ["Drill II", "Missing"]);
        var tree = Tree([A(drill1), A(drill2), A(drill3)]);
        var n1 = tree.GetNode(drill1)!;
        var n2 = tree.GetNode(drill2)!;
        var n3 = tree.GetNode(drill3)!;
        Assert.True(n1.IsRoot);
        Assert.Equal([n2], n1.Children);
        Assert.Equal([n2], n3.Parents);
        Assert.Equal([1, 2, 3], new[] { n1.Level, n2.Level, n3.Level });
        Assert.Equal("Drill", n3.BaseName);
        Assert.Equal([2, 2, 1], new[] { n1.Levels, n2.Levels, n3.Levels }); // counts the child links of the same base name
        Assert.Equal([0, 1, 2], new[] { n1.Depth, n2.Depth, n3.Depth });
        Assert.Equal([1, 2, 3], new[] { n1.DistanceToAdopted, n2.DistanceToAdopted, n3.DistanceToAdopted });
        Assert.Same(n2, n3.PreferredParent);
        Assert.Null(tree.GetNode(P("Other")));
        Assert.Equal("Drill II", n2.ToString());
    }

    [Fact]
    public void AdoptedNodes_HaveDistanceZero_AndEndThePath()
    {
        var a = P("A"); var b = P("B", prerequisites: ["A"]); var c = P("C", prerequisites: ["B"]);
        var tree = Tree([A(a, adopted: true), A(b), A(c)]);
        Assert.Equal([0, 1, 2], tree.AllNodes().Select(it => it.DistanceToAdopted));
        Assert.Equal([b, c], tree.GetPathTo(c));
        Assert.Equal([a], tree.GetPathTo(a)); // the node itself is always in its path
    }

    [Fact]
    public void BlockedPromotions_AreUnreachable_AndTheirDescendantsToo()
    {
        var a = P("A"); var b = P("B", prerequisites: ["A"]); var c = P("C", prerequisites: ["B"]);
        var tree = Tree([A(a), A(b, blocked: true), A(c)]);
        Assert.True(tree.GetNode(b)!.Unreachable);
        Assert.Equal(int.MaxValue, tree.GetNode(b)!.DistanceToAdopted);
        Assert.Equal(int.MaxValue, tree.GetNode(c)!.DistanceToAdopted);
        Assert.Empty(tree.GetPathTo(c));
        Assert.False(tree.CanBuyUpTo(c));
        Assert.True(tree.CanBuyUpTo(a));
    }

    [Fact]
    public void BlockedRoot_GetsDistanceMax()
    {
        var tree = Tree([A(P("A"), blocked: true)]);
        Assert.Equal(int.MaxValue, tree.AllNodes().Single().DistanceToAdopted);
    }

    [Fact]
    public void ADisallowedPrerequisite_MakesTheNodeUnreachableAndARoot()
    {
        // The only prerequisite is not in the tree: defensive branch marks it unreachable, but it has no parents so it is a root.
        var tree = Tree([A(P("Child", prerequisites: ["NotOffered"]))]);
        var node = tree.AllNodes().Single();
        Assert.True(node.Unreachable);
        Assert.True(node.IsRoot);
        Assert.Equal(int.MaxValue, node.DistanceToAdopted);
    }

    [Fact]
    public void EqualCostPaths_AreAmbiguous_AndAdoptedChildrenResetDistance()
    {
        var root = P("Root", 1, 1);
        var left = P("Left", 1, 2, ["Root"]);
        var right = P("Right", 2, 2, ["Root"]);
        var join = P("Join", 1, 3, ["Left", "Right"]);
        var tree = Tree([A(root), A(left), A(right), A(join)]);
        var node = tree.GetNode(join)!;
        Assert.True(node.PathIsAmbiguous);
        Assert.Null(node.PreferredParent);
        Assert.Equal(3, node.DistanceToAdopted);
        Assert.Equal([join], tree.GetPathTo(join));
    }

    [Fact]
    public void ShorterPathThroughAdoptedNode_OverwritesALongerOne()
    {
        // Root -> Mid -> Target (distance 3), and AdoptedRoot -> Target (distance 1 via the adopted root's child step)
        var root = P("Root", 1, 1);
        var mid = P("Mid", 1, 2, ["Root"]);
        var adoptedRoot = P("AdoptedRoot", 2, 1);
        var target = P("Target", 1, 3, ["Mid", "AdoptedRoot"]);
        var tree = Tree([A(root), A(mid), A(adoptedRoot, adopted: true), A(target)]);
        var node = tree.GetNode(target)!;
        Assert.Equal(1, node.DistanceToAdopted);
        Assert.False(node.PathIsAmbiguous);
        Assert.Equal([target], tree.GetPathTo(target));
    }

    [Fact]
    public void PrerequisiteLoops_LeaveNoRoot_SoNoNodeIsReached_AndTerminate()
    {
        // detectLoop only sees the parents linked so far, so a two-node loop is linked completely; without a root nothing gets a depth.
        var tree = Tree([A(P("A", prerequisites: ["B"])), A(P("B", prerequisites: ["A"]))]);
        Assert.Empty(tree.AllRoots());
        Assert.All(tree.AllNodes(), it => Assert.Equal(int.MinValue, it.Depth));
    }

    [Fact]
    public void LongPrerequisiteChains_AreLinkedCompletely_BecauseDetectLoopSeesNoParentsYet()
    {
        // Kotlin quirk kept: detectLoop runs before the node's own parents are linked, so it never fires (the 99-level guard is dead code).
        var list = new List<AvailablePromotion> { A(P("N0", row: 1, column: 0)) };
        for (var i = 1; i <= 101; i++) list.Add(A(P($"N{i}", row: 1, column: i, prerequisites: [$"N{i - 1}"])));
        var tree = Tree(list);
        Assert.Single(tree.GetNode(P("N101"))!.Parents);
        Assert.Equal(99, tree.GetNode(P("N99"))!.Depth);
        Assert.Equal(int.MinValue, tree.GetNode(P("N101"))!.Depth); // the depth loop stops after 100 rounds
    }

    [Fact]
    public void CanBuyUpTo_CountsOnlyNonFreePromotionsInThePath()
    {
        var a = P("A"); var free = P("Free", prerequisites: ["A"], uniques: ["This Promotion is free"]); var c = P("C", prerequisites: ["Free"]);
        var asked = new List<int>();
        var tree = Tree([A(a), A(free), A(c)], count => { asked.Add(count); return count < 3; });
        Assert.True(tree.CanBuyUpTo(c));
        Assert.Equal([2], asked); // A and C count, Free does not
        Assert.False(tree.CanBuyUpTo(P("Unknown")));
    }

    [Fact]
    public void CanBuyUpTo_IsFalseForAdoptedPromotions()
    {
        var a = P("A");
        Assert.False(Tree([A(a, adopted: true)]).CanBuyUpTo(a));
    }

    [Fact]
    public void MaxRowsAndColumns_AreDerivedFromTheNodes_AndEmptyIsZero()
    {
        Assert.Equal(0, Tree([]).GetMaxColumns());
        Assert.Equal(0, Tree([]).GetMaxRows());
        var tree = Tree([A(P("Drill I", 4, 1)), A(P("Drill II", 1, 2, ["Drill I"]))]);
        Assert.Equal(2, tree.GetMaxRows());
        Assert.Equal(6, tree.GetMaxColumns()); // max(row 4, depth 0) + 2 levels
    }

    [Fact]
    public void Update_RebuildsTheTree()
    {
        var tree = Tree([A(P("A"))]);
        var before = tree.AllNodes().Single();
        tree.Update();
        Assert.NotSame(before, tree.AllNodes().Single());
    }
}
