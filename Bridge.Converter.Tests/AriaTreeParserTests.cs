using Bridge.Converter;
using Bridge.Converter.Models;
using HtmlAgilityPack;
using Xunit;

namespace Bridge.Converter.Tests;

public sealed class AriaTreeParserTests
{
    private static readonly Dictionary<string, string> SuitMappings = new()
    {
        ["M480.25 156.355"] = "♥",
        ["M458.915 307.705"] = "♠",
        ["M431.76 256"] = "♦",
        ["M477.443 295.143"] = "♣"
    };

    private static AriaTreeParser CreateParser() =>
        new(new SvgResolver("arrow", SuitMappings));

    private static HtmlDocument LoadHtml(string html)
    {
        var htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(html);
        return htmlDocument;
    }

    // Minimal but realistic HTML snippets mirroring the actual source file structure.

    private const string ArrowSvgHtml =
        """<svg class="arrow arrow--open" viewBox="0 0 512 512"><path d="M192 128l128 128-128 128z"></path></svg>""";

    private const string ClubSvgHtml =
        """<svg viewBox="0 0 512 512"><path d="M477.443 295.143a104.45 104.45 0 0 1-202.26 36.67c-.08 68.73 4.33 114.46 69.55 149h-177.57c65.22-34.53 69.63-80.25 69.55-149a104.41 104.41 0 1 1-66.34-136.28 104.45 104.45 0 1 1 171.14 0 104.5 104.5 0 0 1 135.93 99.61z"></path></svg>""";

    private const string HeartSvgHtml =
        """<svg viewBox="0 0 512 512"><path d="M480.25 156.355c0 161.24-224.25 324.43-224.25 324.43S31.75 317.595 31.75 156.355c0-91.41 70.63-125.13 107.77-125.13 77.65 0 116.48 65.72 116.48 65.72s38.83-65.73 116.48-65.73c37.14.01 107.77 33.72 107.77 125.14z"></path></svg>""";

    private static string BranchHtml(string labelInnerHtml, string childrenHtml = "") =>
        $"""
        <ul class="tree" role="tree">
          <li role="treeitem" class="tree-branch-wrapper">
            <div class="tree-node tree-node__branch">
              {ArrowSvgHtml}
              <span style="display: flex;">{labelInnerHtml}</span>
            </div>
            <ul role="group">{childrenHtml}</ul>
          </li>
        </ul>
        """;

    private static string LeafHtml(string labelInnerHtml) =>
        $"""
        <ul class="tree" role="tree">
          <li role="none" class="tree-leaf-list-item">
            <div role="treeitem" class="tree-node tree-node__leaf">
              <span style="display: flex;">{labelInnerHtml}</span>
            </div>
          </li>
        </ul>
        """;

    [Fact]
    public void Parse_MissingTreeRoot_ThrowsInvalidOperationException()
    {
        AriaTreeParser parser = CreateParser();
        HtmlDocument htmlDocument = LoadHtml("<div>no tree here</div>");

        Assert.Throws<InvalidOperationException>(() => parser.Parse(htmlDocument));
    }

    [Fact]
    public void Parse_SingleLeafNode_IsLeafTrueAndLabelCorrect()
    {
        AriaTreeParser parser = CreateParser();
        HtmlDocument htmlDocument = LoadHtml(LeafHtml("2NT - balanced, 20-21 HCP"));

        List<BidNode> nodes = parser.Parse(htmlDocument);

        BidNode leaf = Assert.Single(nodes);
        Assert.True(leaf.IsLeaf);
        Assert.Equal("2NT - balanced, 20-21 HCP", leaf.Label);
        Assert.Empty(leaf.Children);
        Assert.Equal("1", leaf.Id);
    }

    [Fact]
    public void Parse_SingleBranchWithTwoLeaves_CorrectStructure()
    {
        string childrenHtml =
            $"""
            <li role="none" class="tree-leaf-list-item">
              <div role="treeitem" class="tree-node tree-node__leaf">
                <span>pas</span>
              </div>
            </li>
            <li role="none" class="tree-leaf-list-item">
              <div role="treeitem" class="tree-node tree-node__leaf">
                <span>x</span>
              </div>
            </li>
            """;

        AriaTreeParser parser = CreateParser();
        HtmlDocument htmlDocument = LoadHtml(BranchHtml("1NT - 15-17 HCP", childrenHtml));

        List<BidNode> nodes = parser.Parse(htmlDocument);

        BidNode branch = Assert.Single(nodes);
        Assert.False(branch.IsLeaf);
        Assert.Equal("1NT - 15-17 HCP", branch.Label);
        Assert.Equal(2, branch.Children.Count);
        Assert.Equal("pas", branch.Children[0].Label);
        Assert.Equal("x", branch.Children[1].Label);
    }

    [Fact]
    public void Parse_HierarchicalIds_AssignedCorrectly()
    {
        // Build: branch "A" → [branch "B" → [leaf "C"]]
        string leafHtml =
            """
            <li role="none" class="tree-leaf-list-item">
              <div role="treeitem" class="tree-node tree-node__leaf">
                <span>C - leaf</span>
              </div>
            </li>
            """;
        string innerBranchHtml =
            $"""
            <li role="treeitem" class="tree-branch-wrapper">
              <div class="tree-node tree-node__branch">
                {ArrowSvgHtml}
                <span>B - branch</span>
              </div>
              <ul role="group">{leafHtml}</ul>
            </li>
            """;
        string html = BranchHtml("A - branch", innerBranchHtml);

        AriaTreeParser parser = CreateParser();
        List<BidNode> nodes = parser.Parse(LoadHtml(html));

        BidNode nodeA = Assert.Single(nodes);
        Assert.Equal("1", nodeA.Id);

        BidNode nodeB = Assert.Single(nodeA.Children);
        Assert.Equal("1.1", nodeB.Id);

        BidNode nodeC = Assert.Single(nodeB.Children);
        Assert.Equal("1.1.1", nodeC.Id);
    }

    [Fact]
    public void Parse_SuitSymbolInBranchLabel_ExtractedCorrectly()
    {
        // Label contains: "1" + club SVG + " - 12+ bal"
        string labelHtml = $"1{ClubSvgHtml} - 12+ bal, 15+ clubs or 18 any";
        AriaTreeParser parser = CreateParser();
        HtmlDocument htmlDocument = LoadHtml(BranchHtml(labelHtml));

        List<BidNode> nodes = parser.Parse(htmlDocument);

        BidNode branch = Assert.Single(nodes);
        Assert.Equal("1♣ - 12+ bal, 15+ clubs or 18 any", branch.Label);
    }

    [Fact]
    public void Parse_ArrowSvgNotIncludedInLabel()
    {
        // The arrow SVG inside the branch div must not appear in the label.
        // Label span itself has no arrow — arrow is a sibling of the span.
        string labelHtml = $"1{HeartSvgHtml} - 5+ hearts";
        AriaTreeParser parser = CreateParser();
        HtmlDocument htmlDocument = LoadHtml(BranchHtml(labelHtml));

        List<BidNode> nodes = parser.Parse(htmlDocument);

        BidNode branch = Assert.Single(nodes);
        Assert.DoesNotContain("?", branch.Label);  // no unresolved SVGs
        Assert.Equal("1♥ - 5+ hearts", branch.Label);
    }

    [Fact]
    public void Parse_MultipleTopLevelNodes_CountAndIdsCorrect()
    {
        string html =
            $"""
            <ul class="tree" role="tree">
              <li role="none" class="tree-leaf-list-item">
                <div role="treeitem" class="tree-node tree-node__leaf"><span>Alpha</span></div>
              </li>
              <li role="none" class="tree-leaf-list-item">
                <div role="treeitem" class="tree-node tree-node__leaf"><span>Beta</span></div>
              </li>
              <li role="none" class="tree-leaf-list-item">
                <div role="treeitem" class="tree-node tree-node__leaf"><span>Gamma</span></div>
              </li>
            </ul>
            """;

        AriaTreeParser parser = CreateParser();
        List<BidNode> nodes = parser.Parse(LoadHtml(html));

        Assert.Equal(3, nodes.Count);
        Assert.Equal(["1", "2", "3"], nodes.Select(n => n.Id));
        Assert.Equal(["Alpha", "Beta", "Gamma"], nodes.Select(n => n.Label));
    }
}
