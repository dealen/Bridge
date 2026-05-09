namespace Bridge.Converter.Models;

public class BidNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsLeaf { get; set; }
    public List<BidNode> Children { get; set; } = [];
}
