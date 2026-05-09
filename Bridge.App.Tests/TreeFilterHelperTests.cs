using Bridge.App.Models;
using Bridge.App.Services;

namespace Bridge.App.Tests;

public sealed class TreeFilterHelperTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Creates a branch node (non-leaf) with optional children.</summary>
    private static BidNode Branch(string label, params BidNode[] children) =>
        new() { Id = label, Label = label, IsLeaf = false, Children = [.. children] };

    /// <summary>Creates a leaf node.</summary>
    private static BidNode Leaf(string label) =>
        new() { Id = label, Label = label, IsLeaf = true };

    // ── ApplyFilter: empty / whitespace query ──────────────────────────────────

    [Fact]
    public void ApplyFilter_EmptyQuery_AllNodesVisibleAndExpanded()
    {
        var nodes = new List<BidNode>
        {
            Branch("1♣", Leaf("1♣ opener"), Leaf("pass")),
            Branch("1♦", Leaf("response"))
        };

        // First hide everything so we can prove reset works
        foreach (var node in nodes)
            node.IsVisible = false;

        int matchCount = TreeFilterHelper.ApplyFilter(nodes, "");

        Assert.Equal(0, matchCount);
        Assert.All(nodes, n => Assert.True(n.IsVisible));
        Assert.All(nodes, n => Assert.True(n.IsExpanded));
    }

    [Fact]
    public void ApplyFilter_WhitespaceQuery_TreatedAsEmpty()
    {
        var nodes = new List<BidNode> { Branch("1♣", Leaf("opener")) };

        int matchCount = TreeFilterHelper.ApplyFilter(nodes, "   ");

        Assert.Equal(0, matchCount);
        Assert.True(nodes[0].IsVisible);
    }

    // ── ApplyFilter: match count ───────────────────────────────────────────────

    [Fact]
    public void ApplyFilter_MatchesTopLevelNode_ReturnsCorrectCount()
    {
        var nodes = new List<BidNode>
        {
            Branch("1♣ - opening"),
            Branch("1♦ - opening"),
            Branch("2♣ - strong")
        };

        int matchCount = TreeFilterHelper.ApplyFilter(nodes, "opening");

        Assert.Equal(2, matchCount);
    }

    [Fact]
    public void ApplyFilter_MatchesLeafOnly_CountsLeafNotParent()
    {
        var leaf = Leaf("kontra");
        var parent = Branch("1♣", leaf);
        var nodes = new List<BidNode> { parent };

        int matchCount = TreeFilterHelper.ApplyFilter(nodes, "kontra");

        Assert.Equal(1, matchCount); // only the leaf matches
    }

    [Fact]
    public void ApplyFilter_BothParentAndChildMatch_CountsBoth()
    {
        var child = Leaf("kontra pas");
        var parent = Branch("kontra", child);
        var nodes = new List<BidNode> { parent };

        int matchCount = TreeFilterHelper.ApplyFilter(nodes, "kontra");

        Assert.Equal(2, matchCount);
    }

    [Fact]
    public void ApplyFilter_NoMatch_ReturnsZero()
    {
        var nodes = new List<BidNode>
        {
            Branch("1♣", Leaf("opener")),
            Leaf("pass")
        };

        int matchCount = TreeFilterHelper.ApplyFilter(nodes, "zzz_no_such_text");

        Assert.Equal(0, matchCount);
    }

    // ── ApplyFilter: visibility ────────────────────────────────────────────────

    [Fact]
    public void ApplyFilter_NonMatchingNode_IsHidden()
    {
        var matchingLeaf = Leaf("kontra");
        var hiddenLeaf = Leaf("pas");
        var nodes = new List<BidNode> { matchingLeaf, hiddenLeaf };

        TreeFilterHelper.ApplyFilter(nodes, "kontra");

        Assert.True(matchingLeaf.IsVisible);
        Assert.False(hiddenLeaf.IsVisible);
    }

    [Fact]
    public void ApplyFilter_ParentOfMatchingLeaf_IsVisible()
    {
        var matchingLeaf = Leaf("kontra");
        var hiddenLeaf = Leaf("pas");
        var parent = Branch("responses", matchingLeaf, hiddenLeaf);
        var nodes = new List<BidNode> { parent };

        TreeFilterHelper.ApplyFilter(nodes, "kontra");

        Assert.True(parent.IsVisible);    // ancestor must be visible
        Assert.True(matchingLeaf.IsVisible);
        Assert.False(hiddenLeaf.IsVisible);
    }

    [Fact]
    public void ApplyFilter_TopLevelWithNoMatchingDescendants_IsHidden()
    {
        var matchingSection = Branch("1♣", Leaf("kontra"));
        var hiddenSection = Branch("1♦", Leaf("pas"), Leaf("2♦"));
        var nodes = new List<BidNode> { matchingSection, hiddenSection };

        TreeFilterHelper.ApplyFilter(nodes, "kontra");

        Assert.True(matchingSection.IsVisible);
        Assert.False(hiddenSection.IsVisible);
    }

    // ── ApplyFilter: auto-expand ancestors ────────────────────────────────────

    [Fact]
    public void ApplyFilter_AncestorOfMatchingLeaf_IsExpanded()
    {
        var deepLeaf = Leaf("BA");
        var middleBranch = Branch("responses", deepLeaf);
        var root = Branch("1♣", middleBranch);
        root.IsExpanded = false;
        middleBranch.IsExpanded = false;

        var nodes = new List<BidNode> { root };
        TreeFilterHelper.ApplyFilter(nodes, "BA");

        Assert.True(root.IsExpanded,        "root should be expanded because descendant matches");
        Assert.True(middleBranch.IsExpanded, "middle branch should be expanded because child matches");
    }

    [Fact]
    public void ApplyFilter_BranchWithNoMatch_IsNotForceExpanded()
    {
        var hiddenLeaf = Leaf("pas");
        var hiddenBranch = Branch("1♦", hiddenLeaf);
        hiddenBranch.IsExpanded = false;

        var nodes = new List<BidNode> { hiddenBranch };
        TreeFilterHelper.ApplyFilter(nodes, "kontra");

        Assert.False(hiddenBranch.IsExpanded, "non-matching branch should remain collapsed");
    }

    // ── ApplyFilter: case-insensitivity ───────────────────────────────────────

    [Theory]
    [InlineData("KONTRA")]
    [InlineData("Kontra")]
    [InlineData("kontra")]
    [InlineData("KoNtRa")]
    public void ApplyFilter_CaseInsensitive(string query)
    {
        var nodes = new List<BidNode> { Leaf("kontra") };

        int matchCount = TreeFilterHelper.ApplyFilter(nodes, query);

        Assert.Equal(1, matchCount);
        Assert.True(nodes[0].IsVisible);
    }

    // ── ResetVisibility ────────────────────────────────────────────────────────

    [Fact]
    public void ResetVisibility_RestoresAllNodesToVisibleAndExpanded()
    {
        var leaf = Leaf("pas");
        leaf.IsVisible = false;
        leaf.IsExpanded = false;

        var branch = Branch("1♣", leaf);
        branch.IsVisible = false;
        branch.IsExpanded = false;

        TreeFilterHelper.ResetVisibility([branch]);

        Assert.True(branch.IsVisible);
        Assert.True(branch.IsExpanded);
        Assert.True(leaf.IsVisible);
        Assert.True(leaf.IsExpanded);
    }

    // ── ApplyFilter followed by reset ─────────────────────────────────────────

    [Fact]
    public void ApplyFilter_AfterFilter_EmptyQueryResetsAll()
    {
        var matchingLeaf = Leaf("kontra");
        var hiddenLeaf = Leaf("pas");
        var parent = Branch("responses", matchingLeaf, hiddenLeaf);
        var nodes = new List<BidNode> { parent };

        // Apply a filter that hides hiddenLeaf
        TreeFilterHelper.ApplyFilter(nodes, "kontra");
        Assert.False(hiddenLeaf.IsVisible);

        // Clear the filter
        TreeFilterHelper.ApplyFilter(nodes, "");

        Assert.True(parent.IsVisible);
        Assert.True(matchingLeaf.IsVisible);
        Assert.True(hiddenLeaf.IsVisible);
    }
}
