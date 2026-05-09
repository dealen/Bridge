namespace Bridge.Converter.Models;

public class BridgeDocument
{
    public string SourceFile { get; set; } = string.Empty;
    public DateTime ConvertedAt { get; set; }
    public int TopLevelCount { get; set; }
    public List<BidNode> Nodes { get; set; } = [];
}
