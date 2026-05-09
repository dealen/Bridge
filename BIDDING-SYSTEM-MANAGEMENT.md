# Bidding System Management — Edit Mode Feature Plan

## Goal

Add a full edit mode to both pages (System otwarć, Licytacja dwustronna):

- Rename nodes (inline)
- Add a child node under any existing node
- Delete a node and its subtree
- Reorder nodes (move up / move down within siblings)
- Move a node to a different parent (drag & drop)
- Auto-save edits to `localStorage` (survives page refresh)
- Export the current document as a JSON file (download)
- Import a previously exported JSON file

All client-side — no server required. Blazor WASM only.

---

## Architecture

### New `TreeEditService` singleton

Owns all mutable state and mutation operations.

- On first access per document: checks `localStorage` first → if found, loads it; else delegates to `BridgeDataService` for the bundled JSON — **deep-cloned** so the `BridgeDataService` cache is never mutated
- Caches its own `_systemDoc` / `_dwustronnyDoc` private fields (separate from `BridgeDataService`)
- All mutation methods live here: `Rename`, `AddChild`, `Delete`, `MoveUp`, `MoveDown`, `MoveToParent`; each internally calls `FindParentList(nodeId)` when parent context is needed
- Uses `IJSRuntime` for localStorage reads/writes and file download
- Fires `OnChange` event so pages can call `StateHasChanged()`
- `string? PendingRenameNodeId` — set by `AddChild` to signal the new node should start in edit mode on next render; cleared by `TreeNode` after it consumes it
- `string? DragSourceId` — set directly on the service by `@ondragstart`, read directly by `@ondrop` (not a cascade — cascades only propagate on re-render, which does not happen between dragstart and drop)
- Debounced localStorage save uses **separate `CancellationTokenSource` per storage key** (`_systemCts` and `_dwostronnyCts`) so rapid edits to one document never cancel a pending save for the other
- Pages must implement `IDisposable` and unsubscribe from `OnChange` in `Dispose()` to prevent memory leaks from the singleton holding references to disposed page components; pending saves must be flushed synchronously in `Dispose()` before the CTS is cancelled

### Edit mode cascade

`IsEditMode` bool cascading value passed from `TreeView` into every `TreeNode` — same pattern as the existing `SearchQuery` cascade.

### Drag-to-reparent strategy

Dragged node becomes the **last child** of the drop target. Combined with ↑/↓ reorder buttons this covers all positioning. Reparenting a node onto its own descendant is blocked.

### New node IDs

8-char hex from `Guid.NewGuid().ToString("N")[..8]` — stable, unique, safe as Blazor `@key`.

---

## Files

| File | Change |
|---|---|
| `Bridge.App/Services/TreeEditService.cs` | **New** — all edit logic |
| `Bridge.App/wwwroot/index.html` | Add `window.bridgeEdit` JS helper block |
| `Bridge.App/Program.cs` | Register `TreeEditService` singleton |
| `Bridge.App/Components/TreeNode.razor` | Add edit UI: rename / add / delete / reorder / drag; add `ParentChildren` parameter |
| `Bridge.App/Components/TreeView.razor` | Add `IsEditMode` cascade; pass `ParentChildren` to top-level nodes |
| `Bridge.App/Pages/SystemPage.razor` | Switch to `TreeEditService`; toolbar: toggle, dirty indicator, export, import |
| `Bridge.App/Pages/DwustronnyPage.razor` | Same as SystemPage |
| `Bridge.App/wwwroot/css/app.css` | Edit mode styles |

---

## Implementation Phases

### Phase 1 — JS helpers + TreeEditService skeleton *(unblocks everything)*

1. Add `window.bridgeEdit` script block to `wwwroot/index.html` (before `blazor.webassembly.js`):
   - `downloadFile(filename, content)` — creates a temporary `<a download>` and clicks it
   - `saveToStorage(key, val)` — `localStorage.setItem`
   - `loadFromStorage(key)` — `localStorage.getItem` (returns `null` if absent)
   - `clearStorage(key)` — `localStorage.removeItem`
   - `confirmDelete(message)` — wraps `window.confirm(message)` and returns the bool; all confirm dialogs go through this helper (avoids raw `eval` / inline JS interop)

2. Create `Services/TreeEditService.cs`:
   - Constructor takes `BridgeDataService` + `IJSRuntime`
   - Private fields: `BridgeDocument? _systemDoc`, `BridgeDocument? _dwustronnyDoc`, `CancellationTokenSource? _systemCts`, `CancellationTokenSource? _dwostronnyCts`
   - `GetSystemAsync()` / `GetDwustronnyAsync()` — localStorage-first load; if falling back to HTTP, deep-clone the `BridgeDataService` result via re-serialization so the two caches remain independent
   - Private `FindParentList(string nodeId, List<BidNode> nodes)` — recursive helper that returns the `List<BidNode>` containing the given node ID; required by `Delete`, `MoveUp`, `MoveDown`, and `MoveToParent`
   - `string? DragSourceId` field — set directly by drag event handlers, read directly by drop handlers
   - `string? PendingRenameNodeId` field — set by `AddChild`, consumed and cleared by the new `TreeNode` on `OnAfterRender`
   - All mutation methods (each calls the appropriate `SaveToLocalStorageDebounced(key)` internally)
   - Private `SaveToLocalStorageDebounced(string key)` — cancels and replaces only the CTS for that specific key; rapid edits to System never cancel a pending Dwustronny save
   - `ExportAsync(doc, filename)` and `ImportAsync(stream, key)`
   - `event Action? OnChange`

3. Register in `Program.cs`:
   ```csharp
   builder.Services.AddSingleton(sp =>
       new TreeEditService(
           sp.GetRequiredService<BridgeDataService>(),
           sp.GetRequiredService<IJSRuntime>()));
   ```

#### ✅ Phase 1 — Implemented

| File | Change |
|---|---|
| `Bridge.App/wwwroot/index.html` | Added `window.bridgeEdit` script block (`downloadFile`, `saveToStorage`, `loadFromStorage`, `clearStorage`, `confirmDelete`) before `blazor.webassembly.js` |
| `Bridge.App/Services/TreeMutator.cs` | **New** — pure static helpers with no I/O: `FindParentList`, `FindNode`, `ContainsNode`, `IsDescendant`, `Rename`, `AddChild`, `Delete`, `MoveUp`, `MoveDown`, `MoveToParent` |
| `Bridge.App/Services/TreeEditService.cs` | **New** — singleton service: localStorage-first load with deep-clone fallback, all mutation methods delegating to `TreeMutator`, per-key 200 ms debounced save, `ExportAsync` / `ImportAsync` / `FlushPendingSaveAsync`, `OnChange` event |
| `Bridge.App/Program.cs` | Registered `TreeEditService` singleton |
| `Bridge.App/_Imports.razor` | Added `@using Bridge.App.Services` |
| `Bridge.App.Tests/TreeMutatorTests.cs` | **New** — 25 tests covering `FindParentList` (incl. nested-tree fixture), `IsDescendant`, `Rename`, `AddChild`, `Delete`, `MoveUp`, `MoveDown`, `MoveToParent` (incl. descendant guard) |

**Build:** 0 errors · 0 warnings &nbsp;·&nbsp; **Tests:** 42 passed

---

### Phase 2 — Edit mode toggle *(depends on Phase 1)*

4. `TreeView.razor`:
   - Add `bool IsEditMode` cascading value (alongside existing `SearchQuery`); **do not cascade `DragSourceId`** — drag event handlers read it directly from the injected `TreeEditService`
   - Add `ToggleEditMode()` public method; when switching into edit mode, call `ApplyFilter("")` first to reset search state (`IsVisible` flags and `_searchQuery`) before the cascade flips
   - Add `bool IsDirty` property (set via `TreeEditService.OnChange`)

5. `SystemPage.razor` and `DwustronnyPage.razor`:
   - Switch data source from `BridgeDataService` → `TreeEditService`
   - Implement `IDisposable`; in `OnInitialized` subscribe `TreeEditService.OnChange += HandleChange`; in `Dispose()` unsubscribe `TreeEditService.OnChange -= HandleChange` and call a synchronous flush to write any pending debounced save before the CTS is cancelled
   - Add toolbar:
     - **Tryb edycji** toggle button (active state styled)
     - `● Niezapisane zmiany` dirty indicator (hidden when clean)
     - **Eksportuj JSON** button
     - `<InputFile>` import (hidden file input, styled as button)
   - When entering edit mode: call `_treeView.ApplyFilter("")` to reset `IsVisible` on all nodes and clear the search query, then disable the search bar

#### ✅ Phase 2 — Implemented

| File | Change |
|---|---|
| `Bridge.App/Components/SearchBar.razor` | Added `Disabled` parameter; input gets `disabled="@Disabled"` |
| `Bridge.App/Components/TreeView.razor` | Added `IsEditMode` cascading value; `ToggleEditMode()` method (resets filter first); `ParentChildren` passed to top-level `TreeNode`s |
| `Bridge.App/Components/TreeNode.razor` | Added `ParentChildren` `[Parameter]`; receives `IsEditMode` cascade; passes `ParentChildren="Node.Children"` to child nodes |
| `Bridge.App/Pages/SystemPage.razor` | Switched to `TreeEditService`; implements `IDisposable`; toolbar with edit toggle, dirty indicator (`● Niezapisane zmiany`), Export + Import buttons; search bar disabled in edit mode |
| `Bridge.App/Pages/DwustronnyPage.razor` | Same as SystemPage |
| `Bridge.App/wwwroot/css/app.css` | Added styles: `.btn-tree-action--active`, `.btn-tree-action--import`, `.dirty-indicator`, `.search-input:disabled` |

**Build:** 0 errors · 0 warnings

---

### Phase 3 — Inline rename *(depends on Phase 2)*

6. `TreeNode.razor` — edit mode only:
   - Clicking the label switches it to `<input type="text" @bind="_editBuffer" @onkeydown="HandleRenameKey" @onblur="ConfirmRename">`
   - **Enter** or **blur** → `TreeEditService.Rename(Node.Id, _editBuffer)`
   - **Escape** → cancel, restore original label

#### ✅ Phase 3 — Implemented

| File | Change |
|---|---|
| `Bridge.App/Components/TreeNode.razor` | Added `_isRenaming` / `_editBuffer` / `_renameInputRef` / `_shouldFocusRenameInput` fields; `EnterRenameMode()`, `HandleLabelClick()`, `HandleRenameKey()`, `ConfirmRename()` (trims whitespace; guards against empty result), `CancelRename()`; `OnAfterRenderAsync` handles `PendingRenameNodeId` auto-focus and `_shouldFocusRenameInput` deferred focus; row click checks `_isRenaming` before toggling expand |
| `Bridge.App/wwwroot/css/app.css` | Added `.tree-rename-input` styles |

**Build:** 0 errors · 0 warnings &nbsp;·&nbsp; **Tests:** 46 passed

---

### Phase 4 — Add child + Delete *(parallel with Phase 3)*

7. `TreeNode.razor` — edit mode only:
   - **+** button → `TreeEditService.AddChild(Node.Id)`:
     - Appends new `BidNode` with `Label = "Nowy węzeł"`, `IsLeaf = true`, unique ID
     - If node was a leaf, set `IsLeaf = false` and `IsExpanded = true`
     - Sets `TreeEditService.PendingRenameNodeId` to the new node's ID
     - The new `TreeNode` checks `TreeEditService.PendingRenameNodeId == Node.Id` in `OnAfterRender`; if matched, clears `PendingRenameNodeId` and enters inline rename mode
   - **✕** button:
     - If node has no children → delete immediately via `TreeEditService.Delete(Node.Id)`
     - If node has children → call `JS.InvokeAsync<bool>("bridgeEdit.confirmDelete", message)`; delete on `true`
     - When last child removed from a parent → parent reverts to `IsLeaf = true`

#### ✅ Phase 4 — Implemented

| File | Change |
|---|---|
| `Bridge.App/Components/TreeNode.razor` | Added `HandleAddChild()` and `HandleDelete()` (with subtree confirm guard); edit-mode action buttons (`+` / `✕`) shown with `stopPropagation`; `@inject IJSRuntime JS` and `@inject TreeEditService EditService` added |
| `Bridge.App/wwwroot/css/app.css` | Added `.tree-edit-actions`, `.btn-node-action`, `.btn-node-delete` styles (buttons fade in on row hover) |
| `Bridge.App.Tests/TreeMutatorTests.cs` | Added 4 new tests: `Delete_BranchWithDescendants_RemovesEntireSubtree`, `AddChild_MultipleCallsOnSameParent_ProduceUniqueIds`, `Rename_SameLabel_StillReturnsTrueAndLeaveValueUnchanged`, `AddChild_NewNode_HasDefaultLabelAndIsLeaf` |

**Build:** 0 errors · 0 warnings &nbsp;·&nbsp; **Tests:** 46 passed

---

### Phase 5 — Move up / Move down *(parallel with Phase 4)*

8. `TreeNode.razor` — edit mode only:
   - **↑** / **↓** buttons
   - `ParentChildren` (`List<BidNode>?`) as `[Parameter]` — nullable; `null` for top-level nodes (where the page passes `Document.Nodes` directly and move buttons are still enabled using that reference). Only populated in edit mode; no overhead in read-only renders.
   - `TreeEditService.MoveUp(Node.Id, ParentChildren)` / `MoveDown` — swaps adjacent elements in the provided list
   - ↑ disabled when node is first sibling; ↓ disabled when last

#### ✅ Phase 5 — Implemented

| File | Change |
|---|---|
| `Bridge.App/Components/TreeNode.razor` | Added `IsFirstSibling` / `IsLastSibling` computed properties; `HandleMoveUp()` / `HandleMoveDown()` methods; ↑ / ↓ buttons prepended to the edit-action bar, disabled when node is first/last sibling |

**Build:** 0 errors · 0 warnings &nbsp;·&nbsp; **Tests:** 46 passed (MoveUp/MoveDown already covered)

---

### Phase 6 — Drag-to-reparent *(depends on Phase 5)*

9. `TreeNode.razor` — edit mode only:
   - `draggable="true"` on the node row
   - `@ondragstart` → set `TreeEditService.DragSourceId = Node.Id` directly on the injected service
   - `@ondragover:preventDefault` (enables drop target)
   - `@ondrop` → read `TreeEditService.DragSourceId` directly from the service (not from a cascade — cascades only propagate on Blazor re-renders, which do not occur between dragstart and drop), then call `TreeEditService.MoveToParent(sourceId, Node.Id)`:
     - Guard: skip if source == target
     - Guard: skip if target is a descendant of source — `MoveToParent` uses the `FindParentList` helper traversal to locate source before modifying the tree
     - Remove source from its current parent's `Children` (found via `FindParentList`)
     - Append source as last child of target; set `target.IsLeaf = false`, `target.IsExpanded = true`
   - `@ondragend` → clear `TreeEditService.DragSourceId`
   - Drop target highlighted via CSS `dragover` class

#### ✅ Phase 6 — Implemented

| File | Change |
|---|---|
| `Bridge.App/Components/TreeNode.razor` | `draggable="@(IsEditMode ? "true" : "false")"` on row; `@ondragstart` / `@ondragend` / `@ondragenter` / `@ondragleave` / `@ondrop` / `@ondragover:preventDefault` handlers; `_isDragOver` field for visual feedback; `HandleDragStart` sets `EditService.DragSourceId`; `HandleDrop` reads it, clears it, and calls `EditService.MoveToParent` |
| `Bridge.App/wwwroot/css/app.css` | Added `.tree-node-row[draggable="true"]` grab cursor and `.tree-node-row.drag-over` dashed-outline highlight |

**Build:** 0 errors · 0 warnings &nbsp;·&nbsp; **Tests:** 46 passed (MoveToParent + descendant guard already covered)

---

### Phase 7 — localStorage auto-save *(wires into all mutations)*

10. Every mutation method in `TreeEditService` triggers `SaveToLocalStorageDebounced(key)` — a **per-key** 200 ms debounce using separate `CancellationTokenSource` fields (`_systemCts` / `_dwostronnyCts`). Cancelling a CTS for one document never affects the pending save of the other:
    ```csharp
    private async Task SaveToLocalStorageDebounced(string key)
    {
        ref CancellationTokenSource? cts = ref (key == SystemKey ? ref _systemCts : ref _dwostronnyCts);
        cts?.Cancel();
        cts = new CancellationTokenSource();
        try
        {
            await Task.Delay(200, cts.Token);
            var doc = key == SystemKey ? _systemDoc : _dwustronnyDoc;
            var json = JsonSerializer.Serialize(doc);
            await JS.InvokeVoidAsync("bridgeEdit.saveToStorage", key, json);
        }
        catch (OperationCanceledException) { /* superseded by newer edit */ }
    }
    ```
    Pages flush any pending save synchronously on `Dispose()` by cancelling the CTS and immediately calling `saveToStorage` without delay.

11. `GetSystemAsync()` / `GetDwustronnyAsync()`:
    ```
    var stored = await JS.InvokeAsync<string?>("bridgeEdit.loadFromStorage", key);
    if (stored is not null)
        // deserialize + set IsExpanded = true
    else
        // fall back to HTTP load from bundled JSON
    ```

    localStorage keys:
    - `"bridge-edit-system"` for System otwarć
    - `"bridge-edit-dwustronny"` for Licytacja dwustronna

---

### Phase 8 — Export + Import *(depends on Phase 1)*

12. **Export** (`TreeEditService.ExportAsync`):
    - Recalculates `TopLevelCount = Nodes.Count`
    - Serializes with `WriteIndented = true`, `[JsonIgnore]` properties excluded automatically
    - Calls `JS.InvokeVoidAsync("bridgeEdit.downloadFile", filename, json)`
    - Filenames: `system-edited.json` / `dwustronny-edited.json`
    - Sets `IsDirty = false` and fires `OnChange` after successful download (export = external backup, so the current state is considered saved)

13. **Import** (`<InputFile OnChange="HandleImport">` on each page):
    - Reads `IBrowserFile` stream via `OpenReadStream(maxAllowedSize: 10 * 1024 * 1024)` — the default 500 KB cap must be overridden explicitly or the call throws for typical files
    - `TreeEditService.ImportAsync(stream, key)` deserializes with `JsonSerializer.DeserializeAsync<BridgeDocument>`; **validation**: result must not be `null`, `Nodes` must not be `null`, `TopLevelCount` must equal `Nodes.Count`; on failure throw `InvalidDataException` with a descriptive message
    - Page catches the exception and displays an inline error message (do not use `alert()` interop for errors — render an `<div class="error-text">` element instead)
    - On success: replaces in-memory doc, saves to localStorage, fires `OnChange`, sets `IsDirty = false`

    The imported JSON format is identical to the converter output, so a file exported from the app can be re-imported without modification, or deployed as a new `wwwroot/data/*.json`.

---

## Verification Checklist

- [ ] `dotnet build Bridge.App` → 0 errors after each phase
- [ ] Edit mode toggle: UI switches between read-only and edit mode
- [ ] Entering edit mode resets search: all nodes visible, search bar disabled
- [ ] Rename: edit a node label → refresh page → localStorage restores correctly
- [ ] Add child to leaf → becomes branch; new node auto-enters rename mode; delete last child → reverts to leaf
- [ ] Move up/down → order changes in tree and persists in localStorage
- [ ] Drag node X onto node Y → X disappears from old location, appears as last child of Y
- [ ] Drag parent onto its own descendant → nothing happens (guard works)
- [ ] Rapid rename keystrokes: only one localStorage write fires after typing stops
- [ ] Edit System, switch to Dwustronny, edit that → both saves complete independently
- [ ] Navigate away mid-edit → pending save flushes before page disposes (no lost edits)
- [ ] Export → JSON file downloads; `IsDirty` clears; open file and verify edits are present
- [ ] Import the exported file → tree matches file contents; `IsDirty` clears; search still works
- [ ] Import a malformed file → inline error message shown; existing tree unchanged
- [ ] Search works correctly in read-only mode; disabled/reset in edit mode
- [ ] Unit tests: `TreeEditService` mutation methods (`Rename`, `AddChild`, `Delete`, `MoveUp`, `MoveDown`, `MoveToParent`) each have at least one test in `Bridge.App.Tests`; `FindParentList` covered with a nested-tree fixture; `MoveToParent` descendant guard tested

---

## Decisions

| Decision | Rationale |
|---|---|
| No server | Blazor WASM only; localStorage + file download is sufficient |
| Drag = append as last child | Simpler implementation; ↑/↓ covers fine positioning |
| New node IDs = 8-char Guid hex | Stable, unique, no collision risk, safe as Blazor `@key` |
| Per-key debounced save (200 ms) | Separate CTS per document key; rapid edits to one file never cancel a pending save for the other |
| Confirm dialog for non-empty delete | Prevents accidental subtree loss; routed through `bridgeEdit.confirmDelete` helper (not raw interop) |
| Export format = converter format | Exported JSON can be re-deployed as bundled data without changes |
| Export clears `IsDirty` | Export produces an external backup; the current state is considered saved |
| Search disabled in edit mode | Avoids confusing `IsVisible` state mixing with edit operations; `ApplyFilter("")` called explicitly on mode toggle |
| `TreeEditService` deep-clones from `BridgeDataService` | Prevents the `BridgeDataService` cache from being silently mutated by edits |
| `DragSourceId` read from service, not cascade | Cascades only propagate on Blazor re-renders; drop events fire without an intervening render |
| Pages implement `IDisposable` + flush-on-dispose | Singleton `OnChange` event would leak disposed page references; pending saves must complete before the CTS is cancelled |
| `PendingRenameNodeId` on service | No way to signal a freshly rendered child to enter edit mode without a shared rendezvous field checked in `OnAfterRender` |
| `OpenReadStream(maxAllowedSize: 10 MB)` | Default 500 KB cap throws for typical export files; must be overridden explicitly |
| Import errors shown inline | `alert()` interop is disruptive; an `<div class="error-text">` element integrates with the existing error style |

## Out of Scope

- Undo / redo (can be added later via snapshot stack in `TreeEditService`)
- Server-side persistence
- Multi-user collaboration
- Node reordering via drag within same parent (↑/↓ covers this)
