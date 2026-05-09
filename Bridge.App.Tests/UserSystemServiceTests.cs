using System.Text.Json;
using Bridge.App.Models;
using Bridge.App.Services;
using Microsoft.JSInterop;
using NSubstitute;

namespace Bridge.App.Tests;

/// <summary>
/// Tests for user-system management in <see cref="TreeEditService"/>.
/// Uses a <see cref="FakeJSRuntime"/> instead of a real browser runtime
/// and a substituted <see cref="IBridgeDataService"/> for the built-in documents.
/// </summary>
public sealed class UserSystemServiceTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static (TreeEditService service, FakeJSRuntime js, IBridgeDataService data) CreateService()
    {
        var js = new FakeJSRuntime();
        var data = Substitute.For<IBridgeDataService>();
        var service = new TreeEditService(data, js);
        return (service, js, data);
    }

    private static BridgeDocument DocWithNodes(params string[] nodeIds)
    {
        var nodes = nodeIds.Select(id => new BidNode { Id = id, Label = id, IsLeaf = true }).ToList();
        return new BridgeDocument { SourceFile = "source", TopLevelCount = nodes.Count, Nodes = nodes };
    }

    // ── CreateUserSystemAsync — empty ─────────────────────────────────────────

    [Fact]
    public async Task CreateUserSystem_Empty_AddsEntryToIndex()
    {
        var (service, _, _) = CreateService();

        string newId = await service.CreateUserSystemAsync("Test pusty");

        List<UserDocInfo> index = await service.GetUserIndexAsync();
        Assert.Single(index);
        Assert.Equal("Test pusty", index[0].Name);
        Assert.Equal(newId, index[0].Id);
    }

    [Fact]
    public async Task CreateUserSystem_Empty_DocumentCachedInMemory()
    {
        var (service, _, _) = CreateService();

        string newId = await service.CreateUserSystemAsync("Test pusty");
        BridgeDocument doc = await service.GetUserDocumentAsync(newId);

        Assert.Equal("Test pusty", doc.SourceFile);
        Assert.Empty(doc.Nodes);
    }

    [Fact]
    public async Task CreateUserSystem_Empty_DocumentSavedToLocalStorage()
    {
        var (service, js, _) = CreateService();

        string newId = await service.CreateUserSystemAsync("Test pusty");
        string docKey = TreeEditService.UserDocKey(newId);

        string? savedJson = js.GetStoredValue(docKey);
        Assert.NotNull(savedJson);
        BridgeDocument? saved = JsonSerializer.Deserialize<BridgeDocument>(savedJson);
        Assert.NotNull(saved);
        Assert.Equal("Test pusty", saved.SourceFile);
    }

    [Fact]
    public async Task CreateUserSystem_Empty_IndexSavedToLocalStorage()
    {
        var (service, js, _) = CreateService();

        await service.CreateUserSystemAsync("Test pusty");

        string? indexJson = js.GetStoredValue("bridge-user-index");
        Assert.NotNull(indexJson);
        List<UserDocInfo>? index = JsonSerializer.Deserialize<List<UserDocInfo>>(indexJson);
        Assert.NotNull(index);
        Assert.Single(index);
    }

    [Fact]
    public async Task CreateUserSystem_FiresOnUserIndexChange()
    {
        var (service, _, _) = CreateService();
        bool fired = false;
        service.OnUserIndexChange += () => fired = true;

        await service.CreateUserSystemAsync("Test");

        Assert.True(fired);
    }

    // ── CreateUserSystemAsync — clone from user doc ───────────────────────────

    [Fact]
    public async Task CreateUserSystem_CloneFromUser_NodeIdsDifferFromSource()
    {
        var (service, _, _) = CreateService();

        string sourceId = await service.CreateUserSystemAsync("Źródło");
        // Manually add a node to the source doc so there's something to verify
        BridgeDocument sourceDoc = await service.GetUserDocumentAsync(sourceId);
        sourceDoc.Nodes.Add(new BidNode { Id = "fixed-id", Label = "Węzeł", IsLeaf = true });
        sourceDoc.TopLevelCount = sourceDoc.Nodes.Count;

        string cloneId = await service.CreateUserSystemAsync("Kopia", cloneFromUserId: sourceId);
        BridgeDocument cloneDoc = await service.GetUserDocumentAsync(cloneId);

        // Clone must have the same number of nodes
        Assert.Equal(sourceDoc.Nodes.Count, cloneDoc.Nodes.Count);
        // But different IDs (RegenerateIds was called)
        Assert.NotEqual(sourceDoc.Nodes[0].Id, cloneDoc.Nodes[0].Id);
    }

    [Fact]
    public async Task CreateUserSystem_CloneFromUser_SourceDocNotMutated()
    {
        var (service, _, _) = CreateService();

        string sourceId = await service.CreateUserSystemAsync("Źródło");
        BridgeDocument sourceDoc = await service.GetUserDocumentAsync(sourceId);
        sourceDoc.Nodes.Add(new BidNode { Id = "original-id", Label = "Węzeł", IsLeaf = true });
        string originalId = sourceDoc.Nodes[0].Id;

        await service.CreateUserSystemAsync("Kopia", cloneFromUserId: sourceId);

        // The source document's node ID must be unchanged
        Assert.Equal(originalId, sourceDoc.Nodes[0].Id);
    }

    [Fact]
    public async Task CreateUserSystem_CloneFromBuiltIn_CallsBuiltInLoad()
    {
        var (service, js, data) = CreateService();
        BridgeDocument builtIn = DocWithNodes("n1", "n2");
        data.GetSystemAsync().Returns(Task.FromResult(builtIn));
        // Simulate localStorage having no built-in stored
        js.SetStoredValue(TreeEditService.SystemKey, null);

        string newId = await service.CreateUserSystemAsync("Kopia systemu", cloneFromBuiltIn: "system");
        BridgeDocument cloned = await service.GetUserDocumentAsync(newId);

        // Same number of top-level nodes as the source
        Assert.Equal(builtIn.Nodes.Count, cloned.Nodes.Count);
        // Different IDs after RegenerateIds
        Assert.DoesNotContain(cloned.Nodes, n => n.Id == "n1" || n.Id == "n2");
    }

    // ── DeleteUserSystemAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteUserSystem_RemovesFromIndex()
    {
        var (service, _, _) = CreateService();
        string id = await service.CreateUserSystemAsync("Do usunięcia");

        await service.DeleteUserSystemAsync(id);

        List<UserDocInfo> index = await service.GetUserIndexAsync();
        Assert.Empty(index);
    }

    [Fact]
    public async Task DeleteUserSystem_RemovesFromLocalStorage()
    {
        var (service, js, _) = CreateService();
        string id = await service.CreateUserSystemAsync("Do usunięcia");
        string key = TreeEditService.UserDocKey(id);

        await service.DeleteUserSystemAsync(id);

        // clearStorage should have been called for the doc key
        Assert.Contains(key, js.ClearedKeys);
    }

    [Fact]
    public async Task DeleteUserSystem_FiresOnUserIndexChange()
    {
        var (service, _, _) = CreateService();
        string id = await service.CreateUserSystemAsync("Do usunięcia");
        int fireCount = 0;
        service.OnUserIndexChange += () => fireCount++;

        await service.DeleteUserSystemAsync(id);

        Assert.Equal(1, fireCount);
    }

    // ── RenameUserSystemAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RenameUserSystem_UpdatesNameInIndex()
    {
        var (service, _, _) = CreateService();
        string id = await service.CreateUserSystemAsync("Stara nazwa");

        await service.RenameUserSystemAsync(id, "Nowa nazwa");

        List<UserDocInfo> index = await service.GetUserIndexAsync();
        Assert.Equal("Nowa nazwa", index[0].Name);
    }

    [Fact]
    public async Task RenameUserSystem_UpdatesDocSourceFile()
    {
        var (service, _, _) = CreateService();
        string id = await service.CreateUserSystemAsync("Stara nazwa");

        await service.RenameUserSystemAsync(id, "Nowa nazwa");

        BridgeDocument doc = await service.GetUserDocumentAsync(id);
        Assert.Equal("Nowa nazwa", doc.SourceFile);
    }

    [Fact]
    public async Task RenameUserSystem_FiresOnUserIndexChange()
    {
        var (service, _, _) = CreateService();
        string id = await service.CreateUserSystemAsync("Stara nazwa");
        bool fired = false;
        service.OnUserIndexChange += () => fired = true;

        await service.RenameUserSystemAsync(id, "Nowa nazwa");

        Assert.True(fired);
    }

    [Fact]
    public async Task RenameUserSystem_UnknownId_DoesNotThrow()
    {
        var (service, _, _) = CreateService();

        // Should be a no-op, not throw
        await service.RenameUserSystemAsync("nonexistent-id", "Whatever");
    }

    // ── IsDirtyFor ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsDirtyFor_FalseBeforeAnyEdit()
    {
        var (service, _, _) = CreateService();
        string id = await service.CreateUserSystemAsync("System");
        string key = TreeEditService.UserDocKey(id);

        Assert.False(service.IsDirtyFor(key));
    }

    [Fact]
    public async Task IsDirtyFor_TrueAfterRename()
    {
        var (service, _, _) = CreateService();
        string id = await service.CreateUserSystemAsync("System");
        BridgeDocument doc = await service.GetUserDocumentAsync(id);
        doc.Nodes.Add(new BidNode { Id = "nodeA", Label = "Węzeł A", IsLeaf = true });

        // Trigger a mutation via the service
        service.Rename("nodeA", "Nowa etykieta");

        string key = TreeEditService.UserDocKey(id);
        Assert.True(service.IsDirtyFor(key));
    }

    [Fact]
    public async Task IsDirtyFor_FalseAfterExport()
    {
        var (service, _, _) = CreateService();
        string id = await service.CreateUserSystemAsync("System");
        BridgeDocument doc = await service.GetUserDocumentAsync(id);
        doc.Nodes.Add(new BidNode { Id = "nodeX", Label = "X", IsLeaf = true });
        service.Rename("nodeX", "Renamed");

        await service.ExportAsync(doc, "out.json");

        string key = TreeEditService.UserDocKey(id);
        Assert.False(service.IsDirtyFor(key));
    }

    // ── ResolveKey (via mutations) — error path ────────────────────────────────

    [Fact]
    public async Task Rename_UnknownNodeId_ThrowsInvalidOperationException()
    {
        var (service, _, _) = CreateService();
        await service.CreateUserSystemAsync("System"); // load a doc (won't contain this id)

        Assert.Throws<InvalidOperationException>(() => service.Rename("no-such-node", "label"));
    }

    // ── GetUserDocumentAsync — loads from localStorage ─────────────────────────

    [Fact]
    public async Task GetUserDocument_LoadsFromLocalStorageWhenNotCached()
    {
        var (service, js, _) = CreateService();
        string id = "testid123";
        string key = TreeEditService.UserDocKey(id);
        var stored = new BridgeDocument { SourceFile = "Stored", TopLevelCount = 0, Nodes = [] };
        js.SetStoredValue(key, JsonSerializer.Serialize(stored));

        BridgeDocument doc = await service.GetUserDocumentAsync(id);

        Assert.Equal("Stored", doc.SourceFile);
    }
}

// ── FakeJSRuntime ──────────────────────────────────────────────────────────────

/// <summary>
/// In-memory implementation of <see cref="IJSRuntime"/> for unit tests.
/// Simulates localStorage operations used by <see cref="TreeEditService"/>.
/// </summary>
internal sealed class FakeJSRuntime : IJSRuntime
{
    private readonly Dictionary<string, string?> _storage = new();

    /// <summary>All (key, value) pairs passed to <c>bridgeEdit.saveToStorage</c>.</summary>
    public List<(string Key, string Value)> SavedValues { get; } = new();

    /// <summary>All keys passed to <c>bridgeEdit.clearStorage</c>.</summary>
    public List<string> ClearedKeys { get; } = new();

    /// <summary>All (filename, content) pairs passed to <c>bridgeEdit.downloadFile</c>.</summary>
    public List<(string Filename, string Content)> Downloads { get; } = new();

    /// <summary>Pre-populates a storage value (use <c>null</c> to simulate missing key).</summary>
    public void SetStoredValue(string key, string? value)
    {
        if (value is null)
            _storage.Remove(key);
        else
            _storage[key] = value;
    }

    /// <summary>Returns the current stored value for a key (null if absent).</summary>
    public string? GetStoredValue(string key) =>
        _storage.TryGetValue(key, out string? value) ? value : null;

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        object? result = identifier switch
        {
            "bridgeEdit.loadFromStorage" when args is [string loadKey] =>
                _storage.TryGetValue(loadKey, out string? storedVal) ? storedVal : null,

            "bridgeEdit.saveToStorage" when args is [string saveKey, string saveVal] =>
                SaveToStorage<TValue>(saveKey, saveVal),

            "bridgeEdit.clearStorage" when args is [string clearKey] =>
                ClearStorage(clearKey),

            "bridgeEdit.downloadFile" when args is [string filename, string content] =>
                RecordDownload(filename, content),

            "bridgeEdit.confirmDelete" =>
                (object)true,

            _ => null
        };

        return new ValueTask<TValue>((TValue?)(object?)result ?? default!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);

    private object? SaveToStorage<TValue>(string key, string value)
    {
        _storage[key] = value;
        SavedValues.Add((key, value));
        return default(TValue);
    }

    private object? ClearStorage(string key)
    {
        _storage.Remove(key);
        ClearedKeys.Add(key);
        return null;
    }

    private object? RecordDownload(string filename, string content)
    {
        Downloads.Add((filename, content));
        return null;
    }
}
