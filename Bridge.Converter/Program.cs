using Bridge.Converter;
using Bridge.Converter.Models;
using HtmlAgilityPack;
using System.Text.Json;

string? inputPath = null;
string? outputPath = null;
string? suitMapPath = null;
bool pretty = false;

for (int argIndex = 0; argIndex < args.Length; argIndex++)
{
    switch (args[argIndex])
    {
        case "--input" when argIndex + 1 < args.Length:
            inputPath = args[++argIndex];
            break;
        case "--output" when argIndex + 1 < args.Length:
            outputPath = args[++argIndex];
            break;
        case "--suit-map" when argIndex + 1 < args.Length:
            suitMapPath = args[++argIndex];
            break;
        case "--pretty":
            pretty = true;
            break;
    }
}

if (inputPath is null || outputPath is null)
{
    Console.Error.WriteLine("Usage: Bridge.Converter --input <file> --output <file> [--suit-map suits.json] [--pretty]");
    return 1;
}

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input file not found: {inputPath}");
    return 1;
}

// Resolve suit map path — default to suits.json next to the executable
string resolvedSuitMapPath = suitMapPath
    ?? Path.Combine(AppContext.BaseDirectory, "suits.json");

if (!File.Exists(resolvedSuitMapPath))
{
    Console.Error.WriteLine($"Suit map file not found: {resolvedSuitMapPath}");
    return 1;
}

// Load suit map
using JsonDocument suitMapDocument = JsonDocument.Parse(await File.ReadAllTextAsync(resolvedSuitMapPath));
JsonElement suitMapRoot = suitMapDocument.RootElement;
string svgClassToSkip = suitMapRoot.GetProperty("svgClassToSkip").GetString() ?? "arrow";
Dictionary<string, string> pathMappings = [];
foreach (JsonProperty pathEntry in suitMapRoot.GetProperty("pathMappings").EnumerateObject())
    pathMappings[pathEntry.Name] = pathEntry.Value.GetString() ?? "?";

// Parse HTML
var htmlDocument = new HtmlDocument();
htmlDocument.Load(inputPath);

var svgResolver = new SvgResolver(svgClassToSkip, pathMappings);
var ariaTreeParser = new AriaTreeParser(svgResolver);

List<BidNode> topLevelNodes = ariaTreeParser.Parse(htmlDocument);

var bridgeDocument = new BridgeDocument
{
    SourceFile = Path.GetFileName(inputPath),
    ConvertedAt = DateTime.UtcNow,
    TopLevelCount = topLevelNodes.Count,
    Nodes = topLevelNodes
};

// Serialize to JSON
JsonSerializerOptions jsonOptions = new()
{
    WriteIndented = pretty,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

string outputDirectory = Path.GetDirectoryName(outputPath)!;
if (!string.IsNullOrEmpty(outputDirectory))
    Directory.CreateDirectory(outputDirectory);

await using FileStream outputStream = File.Create(outputPath);
await JsonSerializer.SerializeAsync(outputStream, bridgeDocument, jsonOptions);
await outputStream.FlushAsync();

// Print summary
Console.WriteLine($"Source:        {bridgeDocument.SourceFile}");
Console.WriteLine($"Top-level:     {bridgeDocument.TopLevelCount}");
Console.WriteLine($"Output:        {outputPath}");
PrintNodeSummary(topLevelNodes, depthLevel: 1);

return 0;

static void PrintNodeSummary(List<BidNode> nodes, int depthLevel)
{
    int branchCount = nodes.Count(n => !n.IsLeaf);
    int leafCount = nodes.Count(n => n.IsLeaf);
    Console.WriteLine($"  Level {depthLevel}: {nodes.Count} nodes ({branchCount} branches, {leafCount} leaves)");
    List<BidNode> allChildren = nodes.SelectMany(n => n.Children).ToList();
    if (allChildren.Count > 0)
        PrintNodeSummary(allChildren, depthLevel + 1);
}

