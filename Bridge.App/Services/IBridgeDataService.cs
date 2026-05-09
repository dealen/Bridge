using Bridge.App.Models;

namespace Bridge.App.Services;

public interface IBridgeDataService
{
    Task<BridgeDocument> GetSystemAsync();
    Task<BridgeDocument> GetDwustronnyAsync();
}
