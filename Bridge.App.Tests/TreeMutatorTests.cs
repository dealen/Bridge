using Bridge.App.Models;
using Bridge.App.Services;

namespace Bridge.App.Tests;

public sealed class TreeMutatorTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static BidNode Branch(string id, params BidNode[] children) =>
        new() { Id = id, Label = id, IsLeaf = false, Children = [.. children] };

    private static BidNode Leaf(string id) =>
        new() { Id = id, Label = id, IsLeaf = true };

    // ── FindParentList ─────────────────────────────────────────────────────────

    [Fact]
    public void FindParentList_TopLevelNode_ReturnsNull()
    {
        var nodes = new List<BidNode> { Branch("A", Leaf("A1")) };

        List<BidNode>? result = TreeMutator.FindParentList("A", nodes);

        Assert.Null(result);
    }

    [Fact]
    public void FindParentList_DirectChild_ReturnsParentChildren()
    {
        BidNode leaf = Leaf("A1");
        BidNode root = Branch("A", leaf);
        var nodes = new List<BidNode> { root };

        List<BidNode>? result = TreeMutator.FindParentList("A1", nodes);

        Assert.Same(root.Children, result);
    }

    [Fact]
    public void FindParentList_DeepNestedNode_ReturnsCorrectList()
    {
        // A → B → C
        BidNode c = Leaf("C");
        BidNode b = Branch("B", c);
        BidNode a = Branch("A", b);
        var nodes = new List<BidNode> { a };

        List<BidNode>? result = TreeMutator.FindParentList("C", nodes);

        Assert.Same(b.Children, result);
    }

    [Fact]
    public void FindParentList_MissingNode_ReturnsNull()
    {
        var nodes = new List<BidNode> { Leaf("X") };

        List<BidNode>? result = TreeMutator.FindParentList("NONE", nodes);

        Assert.Null(result);
    }

    // ── IsDescendant ───────────────────────────────────────────────────────────

    [Fact]
    public void IsDescendant_DirectChild_ReturnsTrue()
    {
        BidNode parent = Branch("P", Leaf("C"));

        Assert.True(TreeMutator.IsDescendant("C", parent));
    }

    [Fact]
    public void IsDescendant_GrandChild_ReturnsTrue()
    {
        BidNode grandchild = Leaf("GC");
        BidNode parent = Branch("P", Branch("C", grandchild));

        Assert.True(TreeMutator.IsDescendant("GC", parent));
    }

    [Fact]
    public void IsDescendant_UnrelatedNode_ReturnsFalse()
    {
        BidNode ancestor = Branch("A", Leaf("A1"));

        Assert.False(TreeMutator.IsDescendant("X", ancestor));
    }

    // ── Rename ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_ExistingNode_UpdatesLabel()
    {
        var nodes = new List<BidNode> { Leaf("N1") };

        bool result = TreeMutator.Rename("N1", "Renamed", nodes);

        Assert.True(result);
        Assert.Equal("Renamed", nodes[0].Label);
    }

    [Fact]
    public void Rename_NestedNode_UpdatesLabel()
    {
        BidNode child = Leaf("child");
        var nodes = new List<BidNode> { Branch("root", child) };

        bool result = TreeMutator.Rename("child", "Updated child", nodes);

        Assert.True(result);
        Assert.Equal("Updated child", child.Label);
    }

    [Fact]
    public void Rename_MissingNode_ReturnsFalse()
    {
        var nodes = new List<BidNode> { Leaf("N1") };

        bool result = TreeMutator.Rename("NONE", "X", nodes);

        Assert.False(result);
    }

    // ── AddChild ───────────────────────────────────────────────────────────────

    [Fact]
    public void AddChild_LeafNode_BecomesBreanchWithOneChild()
    {
        BidNode parent = Leaf("P");
        var nodes = new List<BidNode> { parent };

        bool result = TreeMutator.AddChild("P", nodes, out string newId);

        Assert.True(result);
        Assert.False(string.IsNullOrEmpty(newId));
        Assert.False(parent.IsLeaf);
        Assert.True(parent.IsExpanded);
        Assert.Single(parent.Children);
        Assert.Equal(newId, parent.Children[0].Id);
        Assert.Equal("Nowy węzeł", parent.Children[0].Label);
        Assert.True(parent.Children[0].IsLeaf);
    }

    [Fact]
    public void AddChild_BranchNode_AppendsChild()
    {
        BidNode parent = Branch("P", Leaf("existing"));
        var nodes = new List<BidNode> { parent };

        TreeMutator.AddChild("P", nodes, out string newId);

        Assert.Equal(2, parent.Children.Count);
        Assert.Equal(newId, parent.Children[1].Id);
    }

    [Fact]
    public void AddChild_MissingParent_ReturnsFalse()
    {
        var nodes = new List<BidNode> { Leaf("X") };

        bool result = TreeMutator.AddChild("NONE", nodes, out string newId);

        Assert.False(result);
        Assert.Equal(string.Empty, newId);
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_TopLevelNode_Removed()
    {
        var nodes = new List<BidNode> { Leaf("A"), Leaf("B") };

        bool result = TreeMutator.Delete("A", nodes);

        Assert.True(result);
        Assert.Single(nodes);
        Assert.Equal("B", nodes[0].Id);
    }

    [Fact]
    public void Delete_ChildNode_Removed()
    {
        BidNode parent = Branch("P", Leaf("C1"), Leaf("C2"));
        var nodes = new List<BidNode> { parent };

        bool result = TreeMutator.Delete("C1", nodes);

        Assert.True(result);
        Assert.Single(parent.Children);
        Assert.Equal("C2", parent.Children[0].Id);
    }

    [Fact]
    public void Delete_LastChild_RevertsParentToLeaf()
    {
        BidNode parent = Branch("P", Leaf("only-child"));
        var nodes = new List<BidNode> { parent };

        TreeMutator.Delete("only-child", nodes);

        Assert.True(parent.IsLeaf);
        Assert.Empty(parent.Children);
    }

    [Fact]
    public void Delete_MissingNode_ReturnsFalse()
    {
        var nodes = new List<BidNode> { Leaf("X") };

        bool result = TreeMutator.Delete("NONE", nodes);

        Assert.False(result);
    }

    // ── MoveUp ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MoveUp_MiddleNode_SwapsWithPrevious()
    {
        var siblings = new List<BidNode> { Leaf("A"), Leaf("B"), Leaf("C") };

        bool result = TreeMutator.MoveUp("B", siblings);

        Assert.True(result);
        Assert.Equal("B", siblings[0].Id);
        Assert.Equal("A", siblings[1].Id);
        Assert.Equal("C", siblings[2].Id);
    }

    [Fact]
    public void MoveUp_FirstNode_ReturnsFalse()
    {
        var siblings = new List<BidNode> { Leaf("A"), Leaf("B") };

        bool result = TreeMutator.MoveUp("A", siblings);

        Assert.False(result);
        Assert.Equal("A", siblings[0].Id);
    }

    // ── MoveDown ───────────────────────────────────────────────────────────────

    [Fact]
    public void MoveDown_MiddleNode_SwapsWithNext()
    {
        var siblings = new List<BidNode> { Leaf("A"), Leaf("B"), Leaf("C") };

        bool result = TreeMutator.MoveDown("B", siblings);

        Assert.True(result);
        Assert.Equal("A", siblings[0].Id);
        Assert.Equal("C", siblings[1].Id);
        Assert.Equal("B", siblings[2].Id);
    }

    [Fact]
    public void MoveDown_LastNode_ReturnsFalse()
    {
        var siblings = new List<BidNode> { Leaf("A"), Leaf("B") };

        bool result = TreeMutator.MoveDown("B", siblings);

        Assert.False(result);
        Assert.Equal("B", siblings[1].Id);
    }

    // ── MoveToParent ───────────────────────────────────────────────────────────

    [Fact]
    public void MoveToParent_TopLevelToChild_RelocatesNode()
    {
        // Tree: [A, B]  →  after MoveToParent("A", "B"):  [B(children:[A])]
        var nodes = new List<BidNode> { Leaf("A"), Leaf("B") };

        bool result = TreeMutator.MoveToParent("A", "B", nodes);

        Assert.True(result);
        Assert.Single(nodes);
        Assert.Equal("B", nodes[0].Id);
        Assert.False(nodes[0].IsLeaf);
        Assert.Single(nodes[0].Children);
        Assert.Equal("A", nodes[0].Children[0].Id);
    }

    [Fact]
    public void MoveToParent_ChildToAnotherBranch_RelocatesNode()
    {
        // Tree: A(→C), B   →  MoveToParent("C", "B")  →  A(empty→leaf), B(→C)
        BidNode c = Leaf("C");
        BidNode a = Branch("A", c);
        BidNode b = Leaf("B");
        var nodes = new List<BidNode> { a, b };

        bool result = TreeMutator.MoveToParent("C", "B", nodes);

        Assert.True(result);
        Assert.True(a.IsLeaf);           // A reverted to leaf
        Assert.False(b.IsLeaf);          // B promoted to branch
        Assert.Single(b.Children);
        Assert.Equal("C", b.Children[0].Id);
    }

    [Fact]
    public void MoveToParent_SameSourceTarget_ReturnsFalse()
    {
        var nodes = new List<BidNode> { Leaf("A") };

        bool result = TreeMutator.MoveToParent("A", "A", nodes);

        Assert.False(result);
    }

    [Fact]
    public void MoveToParent_TargetIsDescendantOfSource_ReturnsFalse()
    {
        // A → B → C;  attempt MoveToParent("A", "C") must be blocked
        BidNode c = Leaf("C");
        BidNode b = Branch("B", c);
        BidNode a = Branch("A", b);
        var nodes = new List<BidNode> { a };

        bool result = TreeMutator.MoveToParent("A", "C", nodes);

        Assert.False(result);
        // Tree must be unchanged
        Assert.Same(a, nodes[0]);
        Assert.Same(b, a.Children[0]);
        Assert.Same(c, b.Children[0]);
    }

    // ── Phase 3 & 4 edge cases ─────────────────────────────────────────────────

    /// <summary>
    /// Deleting a branch node removes it together with all its descendants.
    /// The component shows a confirmation dialog before deleting a non-empty branch.
    /// </summary>
    [Fact]
    public void Delete_BranchWithDescendants_RemovesEntireSubtree()
    {
        // root → branch(→ leaf1, leaf2)
        BidNode leaf1 = Leaf("L1");
        BidNode leaf2 = Leaf("L2");
        BidNode branch = Branch("B", leaf1, leaf2);
        var nodes = new List<BidNode> { branch };

        bool result = TreeMutator.Delete("B", nodes);

        Assert.True(result);
        Assert.Empty(nodes);
    }

    /// <summary>
    /// Successive AddChild calls on the same parent must produce distinct IDs.
    /// The component relies on ID uniqueness as Blazor @key values.
    /// </summary>
    [Fact]
    public void AddChild_MultipleCallsOnSameParent_ProduceUniqueIds()
    {
        BidNode parent = Leaf("P");
        var nodes = new List<BidNode> { parent };

        TreeMutator.AddChild("P", nodes, out string firstId);
        TreeMutator.AddChild("P", nodes, out string secondId);
        TreeMutator.AddChild("P", nodes, out string thirdId);

        Assert.NotEqual(firstId, secondId);
        Assert.NotEqual(secondId, thirdId);
        Assert.NotEqual(firstId, thirdId);
    }

    /// <summary>
    /// TreeMutator.Rename accepts any non-null string (including empty).
    /// The component guards against empty/whitespace before calling Rename,
    /// so the mutator itself must not reject valid label replacements.
    /// </summary>
    [Fact]
    public void Rename_SameLabel_StillReturnsTrueAndLeaveValueUnchanged()
    {
        var nodes = new List<BidNode> { Leaf("N1") };
        nodes[0].Label = "original";

        bool result = TreeMutator.Rename("N1", "original", nodes);

        Assert.True(result);
        Assert.Equal("original", nodes[0].Label);
    }

    /// <summary>
    /// After AddChild, the newly created node is a leaf with the default label,
    /// matching the label the component will display in the rename input.
    /// </summary>
    [Fact]
    public void AddChild_NewNode_HasDefaultLabelAndIsLeaf()
    {
        BidNode parent = Leaf("P");
        var nodes = new List<BidNode> { parent };

        TreeMutator.AddChild("P", nodes, out string newId);
        BidNode? newNode = TreeMutator.FindNode(newId, nodes);

        Assert.NotNull(newNode);
        Assert.Equal("Nowy węzeł", newNode.Label);
        Assert.True(newNode.IsLeaf);
        Assert.Empty(newNode.Children);
    }

    // ── ValidateImportedDocument (Phase 8) ────────────────────────────────────

    [Fact]
    public void ValidateImportedDocument_NullDocument_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => TreeMutator.ValidateImportedDocument(null));
    }

    [Fact]
    public void ValidateImportedDocument_TopLevelCountMismatch_ThrowsInvalidDataException()
    {
        var doc = new BridgeDocument
        {
            TopLevelCount = 3,
            Nodes = [Leaf("A"), Leaf("B")]   // count=2, but TopLevelCount=3
        };

        var ex = Assert.Throws<InvalidDataException>(() => TreeMutator.ValidateImportedDocument(doc));
        Assert.Contains("TopLevelCount", ex.Message);
    }

    [Fact]
    public void ValidateImportedDocument_ValidDocument_DoesNotThrow()
    {
        var doc = new BridgeDocument
        {
            TopLevelCount = 2,
            Nodes = [Leaf("A"), Leaf("B")]
        };

        // Must not throw
        TreeMutator.ValidateImportedDocument(doc);
    }

    [Fact]
    public void ValidateImportedDocument_EmptyNodeList_MatchingTopLevelCount_DoesNotThrow()
    {
        var doc = new BridgeDocument { TopLevelCount = 0, Nodes = [] };

        // Must not throw — an empty document is valid
        TreeMutator.ValidateImportedDocument(doc);
    }

    // ── RegenerateIds ──────────────────────────────────────────────────────────

    [Fact]
    public void RegenerateIds_EmptyList_DoesNotThrow()
    {
        var nodes = new List<BidNode>();
        TreeMutator.RegenerateIds(nodes); // should not throw
    }

    [Fact]
    public void RegenerateIds_AllIdsReplaced()
    {
        var nodes = new List<BidNode>
        {
            Branch("A", Leaf("A1"), Leaf("A2")),
            Branch("B", Branch("B1", Leaf("B1a"))),
        };

        var originalIds = CollectAllIds(nodes).ToHashSet();
        TreeMutator.RegenerateIds(nodes);
        var newIds = CollectAllIds(nodes).ToHashSet();

        // None of the new IDs should match any original ID
        Assert.Empty(newIds.Intersect(originalIds));
    }

    [Fact]
    public void RegenerateIds_NoDuplicateIds()
    {
        var nodes = new List<BidNode>();
        for (int i = 0; i < 20; i++)
            nodes.Add(Branch($"node{i}", Leaf($"leaf{i}")));

        TreeMutator.RegenerateIds(nodes);

        var allIds = CollectAllIds(nodes).ToList();
        Assert.Equal(allIds.Count, allIds.Distinct().Count());
    }

    [Fact]
    public void RegenerateIds_StructurePreserved()
    {
        BidNode leaf = Leaf("L");
        BidNode branch = Branch("B", leaf);
        var nodes = new List<BidNode> { branch };

        TreeMutator.RegenerateIds(nodes);

        // Tree structure: one top-level branch with one child
        Assert.Single(nodes);
        Assert.Single(nodes[0].Children);
    }

    [Fact]
    public void RegenerateIds_IdsAreEightCharsHex()
    {
        var nodes = new List<BidNode> { Leaf("old") };
        TreeMutator.RegenerateIds(nodes);
        string newId = nodes[0].Id;
        Assert.Equal(8, newId.Length);
        Assert.All(newId, c => Assert.True(char.IsAsciiHexDigit(c)));
    }

    // ── Helpers (extra) ────────────────────────────────────────────────────────

    private static IEnumerable<string> CollectAllIds(List<BidNode> nodes)
    {
        foreach (BidNode node in nodes)
        {
            yield return node.Id;
            foreach (string childId in CollectAllIds(node.Children))
                yield return childId;
        }
    }
}
