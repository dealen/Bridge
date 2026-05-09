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

    private readonly BridgeDataService _dataService;
    private readonly IJSRuntime _js;

    private BridgeDocument? _systemDoc;
    private BridgeDocument? _dwustronnyDoc;

    private CancellationTokenSource? _systemCts;
    private CancellationTokenSource? _dwostronnyCts;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Set by drag-start; read by drop handler. Not cascaded — cascades don't propagate between events.</summary>
    public string? DragSourceId { get; set; }

    /// <summary>Set by AddChild so the newly created TreeNode can auto-enter rename mode in OnAfterRender.</summary>
    public string? PendingRenameNodeId { get; set; }

    /// <summary>True when there are unsaved mutations (cleared by export or import).</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Direct accessor for the currently loaded system document (may be null before first load).</summary>
    public BridgeDocument? SystemDocument => _systemDoc;

    /// <summary>Direct accessor for the currently loaded dwustronny document (may be null before first load).</summary>
    public BridgeDocument? DwustronnyDocument => _dwustronnyDoc;

    /// <summary>Fired after every mutation, export, or import so subscribers can call StateHasChanged.</summary>
    public event Action? OnChange;

    public TreeEditService(BridgeDataService dataService, IJSRuntime js)
    {
        _dataService = dataService;
        _js = js;
    }

    // ── Load ──────────────────────────────────────────────────────────────────

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
        IsDirty = false;
        OnChange?.Invoke();
    }

    public async Task ImportAsync(Stream stream, string key)
    {
        BridgeDocument? doc = await JsonSerializer.DeserializeAsync<BridgeDocument>(stream);
        TreeMutator.ValidateImportedDocument(doc);

        SetAllExpanded(doc!.Nodes);

        if (key == SystemKey) _systemDoc = doc;
        else _dwustronnyDoc = doc;

        string json = JsonSerializer.Serialize(doc);
        await _js.InvokeVoidAsync("bridgeEdit.saveToStorage", key, json);
        IsDirty = false;
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
        else
        {
            _dwostronnyCts?.Cancel();
            _dwostronnyCts = null;
            if (_dwustronnyDoc is null) return;
            string json = JsonSerializer.Serialize(_dwustronnyDoc);
            await _js.InvokeVoidAsync("bridgeEdit.saveToStorage", key, json);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string ResolveKey(string nodeId) =>
        TreeMutator.ContainsNode(nodeId, _systemDoc?.Nodes ?? []) ? SystemKey : DwustronnyKey;

    private List<BidNode> GetNodes(string key) =>
        key == SystemKey ? (_systemDoc?.Nodes ?? []) : (_dwustronnyDoc?.Nodes ?? []);

    private void CommitChange(string key)
    {
        IsDirty = true;
        OnChange?.Invoke();
        _ = SaveToLocalStorageDebounced(key);
    }

    private async Task SaveToLocalStorageDebounced(string key)
    {
        // Separate CTS per key so rapid edits to System never cancel a pending Dwustronny save
        CancellationTokenSource newCts = new();
        if (key == SystemKey)
        {
            _systemCts?.Cancel();
            _systemCts = newCts;
        }
        else
        {
            _dwostronnyCts?.Cancel();
            _dwostronnyCts = newCts;
        }

        try
        {
            await Task.Delay(200, newCts.Token);
            BridgeDocument? doc = key == SystemKey ? _systemDoc : _dwustronnyDoc;
            if (doc is null) return;
            string json = JsonSerializer.Serialize(doc);
            await _js.InvokeVoidAsync("bridgeEdit.saveToStorage", key, json);
        }
        catch (OperationCanceledException) { /* superseded by a newer edit */ }
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
