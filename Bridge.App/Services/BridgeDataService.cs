using System.Net.Http.Json;
using Bridge.App.Models;

namespace Bridge.App.Services;

public class BridgeDataService : IBridgeDataService
{
    private readonly HttpClient _httpClient;
    private BridgeDocument? _systemDocument;
    private BridgeDocument? _dwustronnyDocument;

    public BridgeDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BridgeDocument> GetSystemAsync()
    {
        if (_systemDocument is null)
        {
            _systemDocument = await _httpClient.GetFromJsonAsync<BridgeDocument>("data/system.json")
                ?? throw new InvalidOperationException("Failed to load system.json");
            SetAllExpanded(_systemDocument.Nodes);
        }

        return _systemDocument;
    }

    public async Task<BridgeDocument> GetDwustronnyAsync()
    {
        if (_dwustronnyDocument is null)
        {
            _dwustronnyDocument = await _httpClient.GetFromJsonAsync<BridgeDocument>("data/dwustronny.json")
                ?? throw new InvalidOperationException("Failed to load dwustronny.json");
            SetAllExpanded(_dwustronnyDocument.Nodes);
        }

        return _dwustronnyDocument;
    }

    private static void SetAllExpanded(List<BidNode> nodes)
    {
        foreach (BidNode node in nodes)
        {
            node.IsExpanded = true;
            if (node.Children.Count > 0)
                SetAllExpanded(node.Children);
        }
    }
}
