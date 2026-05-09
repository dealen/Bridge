using System.Text.Json.Serialization;

namespace Bridge.App.Models;

public class BidNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsLeaf { get; set; }
    public List<BidNode> Children { get; set; } = [];

    [JsonIgnore]
    public bool IsExpanded { get; set; } = true;

    [JsonIgnore]
    public bool IsVisible { get; set; } = true;
}
