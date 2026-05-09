using Bridge.App.Models;

namespace Bridge.App.Services;

/// <summary>
/// Pure static helpers for in-memory tree mutations.
/// No I/O or DI dependencies — safe to unit-test directly.
/// </summary>
public static class TreeMutator
{
    // ── Finders ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="List{BidNode}"/> that directly contains the node
    /// with <paramref name="nodeId"/>, or <c>null</c> if not found.
    /// </summary>
    public static List<BidNode>? FindParentList(string nodeId, List<BidNode> nodes)
    {
        foreach (BidNode node in nodes)
        {
            if (node.Children.Any(c => c.Id == nodeId)) return node.Children;
            List<BidNode>? found = FindParentList(nodeId, node.Children);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Finds a node by ID anywhere in the tree. Returns <c>null</c> if not found.</summary>
    public static BidNode? FindNode(string nodeId, List<BidNode> nodes)
    {
        foreach (BidNode node in nodes)
        {
            if (node.Id == nodeId) return node;
            BidNode? found = FindNode(nodeId, node.Children);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Returns <c>true</c> if a node with <paramref name="nodeId"/> exists anywhere in the tree.</summary>
    public static bool ContainsNode(string nodeId, List<BidNode> nodes) =>
        FindNode(nodeId, nodes) is not null;

    /// <summary>Returns <c>true</c> if <paramref name="candidateId"/> is a descendant (at any depth) of <paramref name="ancestor"/>.</summary>
    public static bool IsDescendant(string candidateId, BidNode ancestor)
    {
        foreach (BidNode child in ancestor.Children)
        {
            if (child.Id == candidateId) return true;
            if (IsDescendant(candidateId, child)) return true;
        }
        return false;
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    /// <summary>Renames the node with <paramref name="nodeId"/>. Returns <c>true</c> on success.</summary>
    public static bool Rename(string nodeId, string newLabel, List<BidNode> nodes)
    {
        BidNode? node = FindNode(nodeId, nodes);
        if (node is null) return false;
        node.Label = newLabel;
        return true;
    }

    /// <summary>
    /// Appends a new top-level node to <paramref name="nodes"/>.
    /// Sets <paramref name="newNodeId"/> to the new node's ID.
    /// </summary>
    public static string AddRootNode(List<BidNode> nodes)
    {
        string newNodeId = Guid.NewGuid().ToString("N")[..8];
        nodes.Add(new BidNode { Id = newNodeId, Label = "Nowa sekcja", IsLeaf = true });
        return newNodeId;
    }

    /// <summary>
    /// Appends a new leaf child under <paramref name="parentId"/>.
    /// On success sets <paramref name="newNodeId"/> to the new node's ID and returns <c>true</c>.
    /// </summary>
    public static bool AddChild(string parentId, List<BidNode> nodes, out string newNodeId)
    {
        newNodeId = string.Empty;
        BidNode? parent = FindNode(parentId, nodes);
        if (parent is null) return false;

        newNodeId = Guid.NewGuid().ToString("N")[..8];
        BidNode newNode = new() { Id = newNodeId, Label = "Nowy węzeł", IsLeaf = true };

        if (parent.IsLeaf) parent.IsLeaf = false;
        parent.IsExpanded = true;
        parent.Children.Add(newNode);
        return true;
    }

    /// <summary>
    /// Deletes the node with <paramref name="nodeId"/>.
    /// Reverts the parent to <c>IsLeaf = true</c> when the last child is removed.
    /// Returns <c>true</c> on success.
    /// </summary>
    public static bool Delete(string nodeId, List<BidNode> nodes)
    {
        // Top-level node?
        int topIndex = nodes.FindIndex(n => n.Id == nodeId);
        if (topIndex >= 0)
        {
            nodes.RemoveAt(topIndex);
            return true;
        }

        BidNode? parentNode = FindParentNode(nodeId, nodes);
        if (parentNode is null) return false;

        int childIndex = parentNode.Children.FindIndex(n => n.Id == nodeId);
        if (childIndex < 0) return false;

        parentNode.Children.RemoveAt(childIndex);
        if (parentNode.Children.Count == 0)
            parentNode.IsLeaf = true;

        return true;
    }

    /// <summary>Moves a node one position up among its siblings. Returns <c>true</c> on success.</summary>
    public static bool MoveUp(string nodeId, List<BidNode> siblings)
    {
        int index = siblings.FindIndex(n => n.Id == nodeId);
        if (index <= 0) return false;
        (siblings[index], siblings[index - 1]) = (siblings[index - 1], siblings[index]);
        return true;
    }

    /// <summary>Moves a node one position down among its siblings. Returns <c>true</c> on success.</summary>
    public static bool MoveDown(string nodeId, List<BidNode> siblings)
    {
        int index = siblings.FindIndex(n => n.Id == nodeId);
        if (index < 0 || index >= siblings.Count - 1) return false;
        (siblings[index], siblings[index + 1]) = (siblings[index + 1], siblings[index]);
        return true;
    }

    /// <summary>
    /// Moves <paramref name="sourceId"/> to become the last child of <paramref name="targetId"/>.
    /// Guards: source == target, or target is a descendant of source.
    /// Returns <c>true</c> on success.
    /// </summary>
    public static bool MoveToParent(string sourceId, string targetId, List<BidNode> nodes)
    {
        if (sourceId == targetId) return false;

        BidNode? sourceNode = FindNode(sourceId, nodes);
        if (sourceNode is null) return false;

        // Descendant guard
        if (IsDescendant(targetId, sourceNode)) return false;

        BidNode? targetNode = FindNode(targetId, nodes);
        if (targetNode is null) return false;

        // Remove source from its current location
        int topIndex = nodes.FindIndex(n => n.Id == sourceId);
        if (topIndex >= 0)
        {
            nodes.RemoveAt(topIndex);
        }
        else
        {
            BidNode? sourceParentNode = FindParentNode(sourceId, nodes);
            if (sourceParentNode is null) return false;
            int sourceIndex = sourceParentNode.Children.FindIndex(n => n.Id == sourceId);
            if (sourceIndex < 0) return false;
            sourceParentNode.Children.RemoveAt(sourceIndex);
            if (sourceParentNode.Children.Count == 0)
                sourceParentNode.IsLeaf = true;
        }

        // Re-find target (tree indices may have shifted after removal)
        targetNode = FindNode(targetId, nodes);
        if (targetNode is null) return false;

        targetNode.IsLeaf = false;
        targetNode.IsExpanded = true;
        targetNode.Children.Add(sourceNode);
        return true;
    }

    // ── Id management ─────────────────────────────────────────────────────────

    /// <summary>
    /// Recursively replaces every <see cref="BidNode.Id"/> with a fresh unique identifier.
    /// Call this after cloning any source tree to prevent <c>ResolveKey</c> from matching
    /// nodes against the original document.
    /// </summary>
    public static void RegenerateIds(List<BidNode> nodes)
    {
        foreach (BidNode node in nodes)
        {
            node.Id = Guid.NewGuid().ToString("N")[..8];
            if (node.Children.Count > 0)
                RegenerateIds(node.Children);
        }
    }

    // ── Import validation ─────────────────────────────────────────────────────

    /// <summary>
    /// Validates a <see cref="BridgeDocument"/> deserialized from an imported file.
    /// Throws <see cref="InvalidDataException"/> with a descriptive message on failure.
    /// </summary>
    public static void ValidateImportedDocument(BridgeDocument? doc)
    {
        if (doc is null || doc.Nodes is null)
            throw new InvalidDataException("Plik JSON jest nieprawidłowy lub pusty.");
        if (doc.TopLevelCount != doc.Nodes.Count)
            throw new InvalidDataException(
                $"Niezgodność: TopLevelCount={doc.TopLevelCount}, liczba węzłów najwyższego poziomu={doc.Nodes.Count}.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Returns the <see cref="BidNode"/> that directly contains <paramref name="nodeId"/> as a child.</summary>
    private static BidNode? FindParentNode(string nodeId, List<BidNode> nodes)
    {
        foreach (BidNode node in nodes)
        {
            if (node.Children.Any(c => c.Id == nodeId)) return node;
            BidNode? found = FindParentNode(nodeId, node.Children);
            if (found is not null) return found;
        }
        return null;
    }
}
