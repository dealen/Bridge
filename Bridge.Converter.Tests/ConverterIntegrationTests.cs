using Bridge.Converter;
using Bridge.Converter.Models;
using HtmlAgilityPack;
using System.Text.Json;
using Xunit;

namespace Bridge.Converter.Tests;

/// <summary>
/// Integration tests: parse the actual source HTML files end-to-end
/// and verify the resulting JSON matches the plan's stated requirements.
/// These tests require the source HTML files to be present relative to the test binary.
/// </summary>
public sealed class ConverterIntegrationTests
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

    private static string ResolveSourceFile(string fileName)
    {
        // Walk up from the test binary directory to find the workspace root
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return candidate;
            directory = Path.GetDirectoryName(directory);
        }
        throw new FileNotFoundException($"Source HTML not found: {fileName}. Run tests from the repository root.");
    }

    private static List<BidNode> ParseFile(string fileName)
    {
        string filePath = ResolveSourceFile(fileName);
        var htmlDocument = new HtmlDocument();
        htmlDocument.Load(filePath);
        return CreateParser().Parse(htmlDocument);
    }

    private static int CountLeaves(IEnumerable<BidNode> nodes)
    {
        int count = 0;
        foreach (BidNode node in nodes)
        {
            if (node.IsLeaf)
                count++;
            else
                count += CountLeaves(node.Children);
        }
        return count;
    }

    private static BidNode? FindNodeByLabel(IEnumerable<BidNode> nodes, string labelSubstring)
    {
        foreach (BidNode node in nodes)
        {
            if (node.Label.Contains(labelSubstring, StringComparison.OrdinalIgnoreCase))
                return node;
            BidNode? found = FindNodeByLabel(node.Children, labelSubstring);
            if (found is not null)
                return found;
        }
        return null;
    }

    [Fact]
    public void SystemAnia_TopLevelCount_Is11()
    {
        List<BidNode> nodes = ParseFile("System_ania.html");
        Assert.Equal(11, nodes.Count);
    }

    [Fact]
    public void DwustronnyAnia_TopLevelCount_Is20()
    {
        List<BidNode> nodes = ParseFile("dwustronny_ania.html");
        Assert.Equal(20, nodes.Count);
    }

    [Fact]
    public void SystemAnia_LeafCount_MatchesHtmlSource()
    {
        // grep -c "tree-leaf-list-item" System_ania.html == 660
        List<BidNode> nodes = ParseFile("System_ania.html");
        Assert.Equal(660, CountLeaves(nodes));
    }

    [Fact]
    public void SystemAnia_FirstTopLevelNode_HasClubSymbolAndExpectedLabel()
    {
        List<BidNode> nodes = ParseFile("System_ania.html");
        BidNode firstNode = nodes[0];

        Assert.Equal("1", firstNode.Id);
        Assert.False(firstNode.IsLeaf);
        Assert.Contains("♣", firstNode.Label);
        Assert.Contains("12+", firstNode.Label);
    }

    [Fact]
    public void SystemAnia_AllFourSuitSymbols_PresentInOutput()
    {
        // Verifies that every suit SVG path was correctly resolved to a Unicode symbol.
        List<BidNode> nodes = ParseFile("System_ania.html");

        string[] expectedSuits = ["♥", "♠", "♦", "♣"];
        foreach (string suitSymbol in expectedSuits)
        {
            BidNode? nodeWithSuit = FindNodeByLabel(nodes, suitSymbol);
            Assert.True(nodeWithSuit is not null, $"No node found containing suit symbol '{suitSymbol}'");
        }
    }

    [Fact]
    public void DwustronnyAnia_ContainsKnownCompetitiveBiddingSequence()
    {
        List<BidNode> nodes = ParseFile("dwustronny_ania.html");

        BidNode? kontryNode = FindNodeByLabel(nodes, "kontry");
        Assert.NotNull(kontryNode);
    }

    [Fact]
    public void SystemAnia_AllBranchNodeIds_AreHierarchical()
    {
        List<BidNode> nodes = ParseFile("System_ania.html");

        // Top-level IDs must be "1"–"11" with no dots
        for (int i = 0; i < nodes.Count; i++)
            Assert.Equal((i + 1).ToString(), nodes[i].Id);

        // All children of node "1" must start with "1."
        foreach (BidNode child in nodes[0].Children)
            Assert.StartsWith("1.", child.Id);
    }
}
