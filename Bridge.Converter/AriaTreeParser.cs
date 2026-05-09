using Bridge.Converter.Models;
using HtmlAgilityPack;
using System.Text;

namespace Bridge.Converter;

/// <summary>
/// Parses an ARIA tree HTML document into a list of BidNode objects.
/// </summary>
public sealed class AriaTreeParser
{
    private readonly SvgResolver _svgResolver;

    public AriaTreeParser(SvgResolver svgResolver)
    {
        _svgResolver = svgResolver;
    }

    public List<BidNode> Parse(HtmlDocument htmlDocument)
    {
        HtmlNode? treeRoot = htmlDocument.DocumentNode.SelectSingleNode("//ul[@class='tree']");
        if (treeRoot is null)
            throw new InvalidOperationException("Could not find <ul class='tree'> root element.");

        List<BidNode> topLevelNodes = [];
        int topLevelIndex = 0;

        foreach (HtmlNode childLi in treeRoot.ChildNodes)
        {
            if (childLi.Name != "li")
                continue;

            topLevelIndex++;
            BidNode node = ParseLiNode(childLi, topLevelIndex.ToString());
            topLevelNodes.Add(node);
        }

        return topLevelNodes;
    }

    private BidNode ParseLiNode(HtmlNode liNode, string nodeId)
    {
        string liClass = liNode.GetAttributeValue("class", string.Empty);

        if (liClass.Contains("tree-branch-wrapper", StringComparison.Ordinal))
            return ParseBranchNode(liNode, nodeId);

        if (liClass.Contains("tree-leaf-list-item", StringComparison.Ordinal))
            return ParseLeafNode(liNode, nodeId);

        // Fallback: try to treat as leaf
        return new BidNode { Id = nodeId, Label = liNode.InnerText.Trim(), IsLeaf = true };
    }

    private BidNode ParseBranchNode(HtmlNode liNode, string nodeId)
    {
        // Branch label: <span> that comes after the arrow <svg> inside <div class="tree-node ...">
        HtmlNode? treeNodeDiv = liNode.ChildNodes
            .FirstOrDefault(n => n.Name == "div" && n.GetAttributeValue("class", string.Empty).Contains("tree-node"));

        string label = string.Empty;
        if (treeNodeDiv is not null)
        {
            // The label span is the first <span> child of the div (after the arrow svg)
            HtmlNode? labelSpan = treeNodeDiv.ChildNodes
                .FirstOrDefault(n => n.Name == "span");
            if (labelSpan is not null)
                label = ExtractLabelFromSpan(labelSpan);
        }

        // Recurse into <ul role="group">
        List<BidNode> children = [];
        HtmlNode? groupUl = liNode.ChildNodes
            .FirstOrDefault(n => n.Name == "ul" && n.GetAttributeValue("role", string.Empty) == "group");

        if (groupUl is not null)
        {
            int childIndex = 0;
            foreach (HtmlNode childLi in groupUl.ChildNodes)
            {
                if (childLi.Name != "li")
                    continue;

                childIndex++;
                string childId = $"{nodeId}.{childIndex}";
                children.Add(ParseLiNode(childLi, childId));
            }
        }

        return new BidNode
        {
            Id = nodeId,
            Label = label,
            IsLeaf = false,
            Children = children
        };
    }

    private BidNode ParseLeafNode(HtmlNode liNode, string nodeId)
    {
        // Leaf label: <span> inside the inner <div role="treeitem">
        HtmlNode? treeItemDiv = liNode.ChildNodes
            .FirstOrDefault(n => n.Name == "div" && n.GetAttributeValue("role", string.Empty) == "treeitem");

        string label = string.Empty;
        if (treeItemDiv is not null)
        {
            HtmlNode? labelSpan = treeItemDiv.ChildNodes
                .FirstOrDefault(n => n.Name == "span");
            if (labelSpan is not null)
                label = ExtractLabelFromSpan(labelSpan);
        }

        return new BidNode
        {
            Id = nodeId,
            Label = label,
            IsLeaf = true,
            Children = []
        };
    }

    private string ExtractLabelFromSpan(HtmlNode spanNode)
    {
        var labelBuilder = new StringBuilder();

        foreach (HtmlNode child in spanNode.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                labelBuilder.Append(HtmlEntity.DeEntitize(child.InnerText));
            }
            else if (child.Name == "svg")
            {
                string? symbol = _svgResolver.Resolve(child);
                if (symbol is not null)
                    labelBuilder.Append(symbol);
            }
        }

        return labelBuilder.ToString().Trim();
    }
}
