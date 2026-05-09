using System.Text.Json;
using Bridge.App.Models;
using Microsoft.JSInterop;

namespace Bridge.App.Services;

/// <summary>
/// Singleton service that owns all mutable document state and edit operations.
/// Persists changes to localStorage via JS interop (debounced, per-document key).
/// </summary>
public class TreeEditService
{
    public const string SystemKey = "bridge-edit-system";
    public const string DwustronnyKey = "bridge-edit-dwustronny";
    private const string UserDocPrefix = "bridge-user-";
    private const string UserIndexKey = "bridge-user-index";

    private readonly IBridgeDataService _dataService;
    private readonly IJSRuntime _js;

    private BridgeDocument? _systemDoc;
    private BridgeDocument? _dwustronnyDoc;
    private CancellationTokenSource? _systemCts;
    private CancellationTokenSource? _dwostronnyCts;

    private readonly Dictionary<string, BridgeDocument> _userDocs = new();
    private readonly Dictionary<string, CancellationTokenSource> _userCts = new();
    private List<UserDocInfo>? _userIndex;

    private readonly Dictionary<string, bool> _dirtyFlags = new();

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Set by drag-start; read by drop handler. Not cascaded — cascades don't propagate between events.</summary>
    public string? DragSourceId { get; set; }

    /// <summary>Set by AddChild so the newly created TreeNode can auto-enter rename mode in OnAfterRender.</summary>
    public string? PendingRenameNodeId { get; set; }

    /// <summary>True when there are unsaved mutations in any document (backward-compatible aggregate).</summary>
    public bool IsDirty => _dirtyFlags.Values.Any(v => v);

    /// <summary>True when there are unsaved mutations for the specified document key.</summary>
    public bool IsDirtyFor(string key) => _dirtyFlags.TryGetValue(key, out bool dirty) && dirty;

    /// <summary>Direct accessor for the currently loaded system document (may be null before first load).</summary>
    public BridgeDocument? SystemDocument => _systemDoc;

    /// <summary>Direct accessor for the currently loaded dwustronny document (may be null before first load).</summary>
    public BridgeDocument? DwustronnyDocument => _dwustronnyDoc;

    /// <summary>Fired after every mutation, export, or import so subscribers can call StateHasChanged.</summary>
    public event Action? OnChange;

    /// <summary>Fired when the user-system index changes (system created, deleted, or renamed).</summary>
    public event Action? OnUserIndexChange;

    public TreeEditService(IBridgeDataService dataService, IJSRuntime js)
    {
        _dataService = dataService;
        _js = js;
    }

    // ── Load built-in documents ───────────────────────────────────────────────

    public async Task<BridgeDocument> GetSystemAsync()
    {
        if (_systemDoc is not null) return _systemDoc;

        string? stored = await _js.InvokeAsync<string?>("bridgeEdit.loadFromStorage", SystemKey);
        if (stored is not null)
        {
            _systemDoc = JsonSerializer.Deserialize<BridgeDocument>(stored)
                ?? throw new InvalidOperationException("Failed to deserialize system doc from localStorage.");
            SetAllExpanded(_systemDoc.Nodes);
        }
        else
        {
            // Deep-clone via re-serialization so BridgeDataService cache is never mutated
            BridgeDocument original = await _dataService.GetSystemAsync();
            string json = JsonSerializer.Serialize(original);
            _systemDoc = JsonSerializer.Deserialize<BridgeDocument>(json)
                ?? throw new InvalidOperationException("Failed to clone system document.");
            SetAllExpanded(_systemDoc.Nodes);
        }

        return _systemDoc;
    }

    public async Task<BridgeDocument> GetDwustronnyAsync()
    {
        if (_dwustronnyDoc is not null) return _dwustronnyDoc;

        string? stored = await _js.InvokeAsync<string?>("bridgeEdit.loadFromStorage", DwustronnyKey);
        if (stored is not null)
        {
            _dwustronnyDoc = JsonSerializer.Deserialize<BridgeDocument>(stored)
                ?? throw new InvalidOperationException("Failed to deserialize dwustronny doc from localStorage.");
            SetAllExpanded(_dwustronnyDoc.Nodes);
        }
        else
        {
            BridgeDocument original = await _dataService.GetDwustronnyAsync();
            string json = JsonSerializer.Serialize(original);
            _dwustronnyDoc = JsonSerializer.Deserialize<BridgeDocument>(json)
                ?? throw new InvalidOperationException("Failed to clone dwustronny document.");
            SetAllExpanded(_dwustronnyDoc.Nodes);
        }

        return _dwustronnyDoc;
    }

    // ── User document management ──────────────────────────────────────────────

    /// <summary>Returns the list of user-created systems, loading from localStorage on first call.</summary>
    public async Task<List<UserDocInfo>> GetUserIndexAsync()
    {
        if (_userIndex is not null) return _userIndex;

        string? stored = await _js.InvokeAsync<string?>("bridgeEdit.loadFromStorage", UserIndexKey);
        _userIndex = stored is not null
            ? JsonSerializer.Deserialize<List<UserDocInfo>>(stored) ?? []
            : [];

        return _userIndex;
    }

    /// <summary>Returns the document for a user system by id, loading from localStorage if needed.</summary>
    public async Task<BridgeDocument> GetUserDocumentAsync(string id)
    {
        string key = UserDocKey(id);
        if (_userDocs.TryGetValue(key, out BridgeDocument? cached)) return cached;

        string? stored = await _js.InvokeAsync<string?>("bridgeEdit.loadFromStorage", key);
        BridgeDocument doc;
        if (stored is not null)
        {
            doc = JsonSerializer.Deserialize<BridgeDocument>(stored)
                ?? throw new InvalidOperationException($"Failed to deserialize user document '{id}' from localStorage.");
        }
        else
        {
            doc = new BridgeDocument { SourceFile = id, ConvertedAt = DateTime.UtcNow };
        }

        SetAllExpanded(doc.Nodes);
        _userDocs[key] = doc;
        return doc;
    }

    /// <summary>
    /// Creates a new user system with the given name, optionally cloning from a built-in or user document.
    /// <paramref name="cloneFromBuiltIn"/> accepts <c>"system"</c> or <c>"dwustronny"</c>.
    /// Returns the new system id.
    /// </summary>
    public async Task<string> CreateUserSystemAsync(
        string name,
        string? cloneFromBuiltIn = null,
        string? cloneFromUserId = null)
    {
        string newId = Guid.NewGuid().ToString("N")[..12];
        string newKey = UserDocKey(newId);

        BridgeDocument newDoc;
        if (cloneFromBuiltIn == "system")
        {
            newDoc = DeepClone(await GetSystemAsync());
        }
        else if (cloneFromBuiltIn == "dwustronny")
        {
            newDoc = DeepClone(await GetDwustronnyAsync());
        }
        else if (cloneFromUserId is not null)
        {
            newDoc = DeepClone(await GetUserDocumentAsync(cloneFromUserId));
        }
        else
        {
            newDoc = new BridgeDocument();
        }

        newDoc.SourceFile = name;
        newDoc.ConvertedAt = DateTime.UtcNow;
        // Regenerate all node IDs to prevent ResolveKey collisions with the source document
        TreeMutator.RegenerateIds(newDoc.Nodes);
        newDoc.TopLevelCount = newDoc.Nodes.Count;
        SetAllExpanded(newDoc.Nodes);

        _userDocs[newKey] = newDoc;

        List<UserDocInfo> index = await GetUserIndexAsync();
        index.Add(new UserDocInfo { Id = newId, Name = name, CreatedAt = DateTime.UtcNow });

        string docJson = JsonSerializer.Serialize(newDoc);
        await _js.InvokeVoidAsync("bridgeEdit.saveToStorage", newKey, docJson);
        await SaveIndexAsync();

        OnUserIndexChange?.Invoke();
        OnChange?.Invoke();

        return newId;
    }

    /// <summary>Permanently deletes a user system by id.</summary>
    public async Task DeleteUserSystemAsync(string id)
    {
        string key = UserDocKey(id);

        List<UserDocInfo> index = await GetUserIndexAsync();
        index.RemoveAll(info => info.Id == id);

        _userDocs.Remove(key);
        _dirtyFlags.Remove(key);

        if (_userCts.TryGetValue(key, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            _userCts.Remove(key);
        }

        await _js.InvokeVoidAsync("bridgeEdit.clearStorage", key);
        await SaveIndexAsync();

        OnUserIndexChange?.Invoke();
    }

    /// <summary>Renames a user system by id.</summary>
    public async Task RenameUserSystemAsync(string id, string newName)
    {
        List<UserDocInfo> index = await GetUserIndexAsync();
        UserDocInfo? info = index.FirstOrDefault(i => i.Id == id);
        if (info is null) return;

        info.Name = newName;

        string key = UserDocKey(id);
        if (_userDocs.TryGetValue(key, out BridgeDocument? doc))
            doc.SourceFile = newName;

        await SaveIndexAsync();
        OnUserIndexChange?.Invoke();
    }

    /// <summary>Shows a browser confirm dialog and returns the user's choice.</summary>
    public async Task<bool> ConfirmDeleteAsync(string message) =>
        await _js.InvokeAsync<bool>("bridgeEdit.confirmDelete", message);

    // ── Mutations ─────────────────────────────────────────────────────────────

    public void Rename(string nodeId, string newLabel)
    {
        string key = ResolveKey(nodeId);
        if (!TreeMutator.Rename(nodeId, newLabel, GetNodes(key))) return;
        CommitChange(key);
    }

    public void AddChild(string parentId)
    {
        string key = ResolveKey(parentId);
        if (!TreeMutator.AddChild(parentId, GetNodes(key), out string newNodeId)) return;
        PendingRenameNodeId = newNodeId;
        CommitChange(key);
    }

    public void AddRootNode(string key)
    {
        string newNodeId = TreeMutator.AddRootNode(GetNodes(key));
        PendingRenameNodeId = newNodeId;
        CommitChange(key);
    }

    public void Delete(string nodeId)
    {
        string key = ResolveKey(nodeId);
        if (!TreeMutator.Delete(nodeId, GetNodes(key))) return;
        CommitChange(key);
    }

    public void MoveUp(string nodeId, List<BidNode> siblings)
    {
        if (!TreeMutator.MoveUp(nodeId, siblings)) return;
        CommitChange(ResolveKey(nodeId));
    }

    public void MoveDown(string nodeId, List<BidNode> siblings)
    {
        if (!TreeMutator.MoveDown(nodeId, siblings)) return;
        CommitChange(ResolveKey(nodeId));
    }

    public void MoveToParent(string sourceId, string targetId)
    {
        string key = ResolveKey(sourceId);
        if (!TreeMutator.MoveToParent(sourceId, targetId, GetNodes(key))) return;
        CommitChange(key);
    }

    // ── Export / Import ───────────────────────────────────────────────────────

    public async Task ExportAsync(BridgeDocument doc, string filename)
    {
        doc.TopLevelCount = doc.Nodes.Count;
        string json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        await _js.InvokeVoidAsync("bridgeEdit.downloadFile", filename, json);
        string? key = FindKeyForDocument(doc);
        if (key is not null) _dirtyFlags[key] = false;
        OnChange?.Invoke();
    }

    public async Task ImportAsync(Stream stream, string key)
    {
        BridgeDocument? doc = await JsonSerializer.DeserializeAsync<BridgeDocument>(stream);
        TreeMutator.ValidateImportedDocument(doc);

        SetAllExpanded(doc!.Nodes);

        if (key == SystemKey) _systemDoc = doc;
        else if (key == DwustronnyKey) _dwustronnyDoc = doc;
        else _userDocs[key] = doc;

        string json = JsonSerializer.Serialize(doc);
        await _js.InvokeVoidAsync("bridgeEdit.saveToStorage", key, json);
        _dirtyFlags[key] = false;
        OnChange?.Invoke();
    }

    /// <summary>
    /// Cancels any pending debounced save and immediately writes the current document to localStorage.
    /// Should be called from IDisposable.Dispose() on the page component.
    /// </summary>
    public async Task FlushPendingSaveAsync(string key)
    {
        if (key == SystemKey)
        {
            _systemCts?.Cancel();
            _systemCts = null;
            if (_systemDoc is null) return;
            string json = JsonSerializer.Serialize(_systemDoc);
            await _js.InvokeVoidAsync("bridgeEdit.saveToStorage", key, json);
        }
        else if (key == DwustronnyKey)
        {
            _dwostronnyCts?.Cancel();
            _dwostronnyCts = null;
            if (_dwustronnyDoc is null) return;
            string json = JsonSerializer.Serialize(_dwustronnyDoc);
            await _js.InvokeVoidAsync("bridgeEdit.saveToStorage", key, json);
        }
        else
        {
            if (_userCts.TryGetValue(key, out CancellationTokenSource? cts))
            {
                cts.Cancel();
                _userCts.Remove(key);
            }
            if (_userDocs.TryGetValue(key, out BridgeDocument? doc))
            {
                string json = JsonSerializer.Serialize(doc);
                await _js.InvokeVoidAsync("bridgeEdit.saveToStorage", key, json);
            }
        }
    }

    // ── Key helpers ───────────────────────────────────────────────────────────

    /// <summary>Returns the localStorage key for a user document by its id.</summary>
    public static string UserDocKey(string id) => $"{UserDocPrefix}{id}";

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the document key that contains <paramref name="nodeId"/>.
    /// Searches user documents first, then built-ins.
    /// Throws <see cref="InvalidOperationException"/> if not found in any loaded document.
    /// </summary>
    private string ResolveKey(string nodeId)
    {
        foreach (KeyValuePair<string, BridgeDocument> kvp in _userDocs)
        {
            if (TreeMutator.ContainsNode(nodeId, kvp.Value.Nodes))
                return kvp.Key;
        }

        if (TreeMutator.ContainsNode(nodeId, _systemDoc?.Nodes ?? []))
            return SystemKey;

        if (TreeMutator.ContainsNode(nodeId, _dwustronnyDoc?.Nodes ?? []))
            return DwustronnyKey;

        throw new InvalidOperationException($"Node '{nodeId}' not found in any loaded document.");
    }

    private List<BidNode> GetNodes(string key)
    {
        if (key == SystemKey) return _systemDoc?.Nodes ?? [];
        if (key == DwustronnyKey) return _dwustronnyDoc?.Nodes ?? [];
        return _userDocs.TryGetValue(key, out BridgeDocument? doc) ? doc.Nodes : [];
    }

    private void CommitChange(string key)
    {
        _dirtyFlags[key] = true;
        OnChange?.Invoke();
        _ = SaveToLocalStorageDebounced(key);
    }

    private async Task SaveToLocalStorageDebounced(string key)
    {
        // Separate CTS per key so rapid edits to one document never cancel a pending save for another
        CancellationTokenSource newCts = new();
        if (key == SystemKey)
        {
            _systemCts?.Cancel();
            _systemCts = newCts;
        }
        else if (key == DwustronnyKey)
        {
            _dwostronnyCts?.Cancel();
            _dwostronnyCts = newCts;
        }
        else
        {
            if (_userCts.TryGetValue(key, out CancellationTokenSource? oldCts))
                oldCts.Cancel();
            _userCts[key] = newCts;
        }

        try
        {
            await Task.Delay(200, newCts.Token);
            BridgeDocument? doc = GetDocumentForKey(key);
            if (doc is null) return;
            string json = JsonSerializer.Serialize(doc);
            await _js.InvokeVoidAsync("bridgeEdit.saveToStorage", key, json);
        }
        catch (OperationCanceledException) { /* superseded by a newer edit */ }
    }

    private BridgeDocument? GetDocumentForKey(string key)
    {
        if (key == SystemKey) return _systemDoc;
        if (key == DwustronnyKey) return _dwustronnyDoc;
        _userDocs.TryGetValue(key, out BridgeDocument? doc);
        return doc;
    }

    private string? FindKeyForDocument(BridgeDocument doc)
    {
        if (ReferenceEquals(doc, _systemDoc)) return SystemKey;
        if (ReferenceEquals(doc, _dwustronnyDoc)) return DwustronnyKey;
        foreach (KeyValuePair<string, BridgeDocument> kvp in _userDocs)
        {
            if (ReferenceEquals(kvp.Value, doc)) return kvp.Key;
        }
        return null;
    }

    private async Task SaveIndexAsync()
    {
        string json = JsonSerializer.Serialize(_userIndex ?? []);
        await _js.InvokeVoidAsync("bridgeEdit.saveToStorage", UserIndexKey, json);
    }

    private static BridgeDocument DeepClone(BridgeDocument source)
    {
        string json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<BridgeDocument>(json)
            ?? throw new InvalidOperationException("Failed to deep-clone document.");
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
