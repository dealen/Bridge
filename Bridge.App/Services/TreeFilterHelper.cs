using Bridge.App.Models;

namespace Bridge.App.Services;

/// <summary>Pure static helpers for filtering and resetting tree visibility. Extracted for testability.</summary>
public static class TreeFilterHelper
{
    /// <summary>
    /// Applies a case-insensitive filter to the tree. Nodes that neither match nor have matching
    /// descendants are hidden. Ancestor branches of matching nodes are auto-expanded.
    /// Returns the total count of directly matching nodes.
    /// </summary>
    public static int ApplyFilter(List<BidNode> nodes, string query)
    {
        int matchCount = 0;

        if (string.IsNullOrWhiteSpace(query))
        {
            ResetVisibility(nodes);
            return 0;
        }

        foreach (BidNode topLevelNode in nodes)
            FilterNode(topLevelNode, query, ref matchCount);

        return matchCount;
    }

    /// <summary>Restores all nodes to visible and expanded.</summary>
    public static void ResetVisibility(List<BidNode> nodes)
    {
        foreach (BidNode node in nodes)
        {
            node.IsVisible = true;
            node.IsExpanded = true;
            if (node.Children.Count > 0)
                ResetVisibility(node.Children);
        }
    }

    /// <summary>Marks each node visible if it or any descendant matches. Returns true if this subtree has any match.</summary>
    private static bool FilterNode(BidNode node, string query, ref int matchCount)
    {
        bool selfMatches = node.Label.Contains(query, StringComparison.OrdinalIgnoreCase);

        bool descendantMatches = false;
        foreach (BidNode child in node.Children)
        {
            if (FilterNode(child, query, ref matchCount))
                descendantMatches = true;
        }

        if (selfMatches)
        {
            matchCount++;
            node.IsVisible = true;
            node.IsExpanded = true;
            ShowAllDescendants(node.Children);
        }
        else
        {
            node.IsVisible = descendantMatches;
            if (descendantMatches)
                node.IsExpanded = true;
        }

        return node.IsVisible;
    }

    /// <summary>Makes all descendants visible and expanded.</summary>
    private static void ShowAllDescendants(List<BidNode> nodes)
    {
        foreach (BidNode node in nodes)
        {
            node.IsVisible = true;
            node.IsExpanded = true;
            if (node.Children.Count > 0)
                ShowAllDescendants(node.Children);
        }
    }
}
