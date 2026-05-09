# Plan: Create Custom Bidding Systems ("Moje systemy")

## Goal

Allow users to create, manage, view, and edit any number of their own named bidding system trees ("Moje systemy"). Each system is a generic named tree stored in localStorage. Users can start from an empty tree or clone an existing system (built-in or custom). Systems appear as individual nav links under a "Moje systemy" nav section.

---

## Decisions

- **Multiple** named user systems (not a single slot)
- **Generic** (no fixed type — just name + tree)
- Navigation: "Moje systemy" section in `NavMenu.razor` with dynamic per-system links
- Starting content: user chooses — **empty** or **clone** from System otwarć, Dwustronny, or any existing user system
- Mutations reuse existing `TreeMutator` + extend `TreeEditService` (avoids refactoring `TreeNode.razor`)
- Cloned trees must have all `BidNode.Id` values regenerated — otherwise `ResolveKey` would match against the original doc and corrupt it
- `IsDirty` becomes a per-key `Dictionary<string, bool>` — current single-bool flag bleeds across all open docs
- Excluded: server sync, sharing, versioning, undo/redo

---

## Data Model

**New model**: `Bridge.App/Models/UserDocInfo.cs`
- `string Id` — `Guid.NewGuid().ToString("N")[..12]`
- `string Name` — user-given name
- `DateTime CreatedAt`

**localStorage layout:**
- Index key: `"bridge-user-index"` → JSON array of `UserDocInfo[]`
- Doc content key: `"bridge-user-{id}"` → full `BridgeDocument` JSON (reuse existing model; `SourceFile` = system name, `ConvertedAt` = created date)

---

## Architecture

### Extend `TreeEditService`

Add to existing singleton:
- `Dictionary<string, BridgeDocument> _userDocs` — in-memory cache keyed by `"bridge-user-{id}"`
- `Dictionary<string, CancellationTokenSource> _userCts` — debounce CTSs per user doc
- `List<UserDocInfo>? _userIndex` — in-memory index cache
- `event Action? OnUserIndexChange` — fired when the list of systems changes (nav updates)

New public methods:
- `GetUserIndexAsync()` → `List<UserDocInfo>` — loads from localStorage, caches
- `GetUserDocumentAsync(string id)` → `BridgeDocument` — loads from localStorage with `SetAllExpanded`
- `CreateUserSystemAsync(string name, string? cloneFromBuiltIn, string? cloneFromUserId)` → `string newId`
  - `cloneFromBuiltIn` accepts `"system"` or `"dwustronny"` (logical names, not localStorage keys); maps to `SystemKey`/`DwustronnyKey` internally
  - Clones from `_systemDoc` / `_dwustronnyDoc` / user doc via re-serialization, or creates empty `BridgeDocument`
  - **After cloning, runs `RegenerateIds` (see below) over all nodes before saving** — prevents `ResolveKey` from matching against the source doc
  - Adds `UserDocInfo` to index, saves index + doc to localStorage
  - Fires `OnUserIndexChange` then `OnChange`
- `DeleteUserSystemAsync(string id)` — removes from index + localStorage, fires `OnUserIndexChange`
- `RenameUserSystemAsync(string id, string newName)` — updates index, saves index, fires `OnUserIndexChange`

**Add to `TreeMutator`:**
- `RegenerateIds(List<BidNode> nodes)` — static; recursively replaces every `BidNode.Id` with a fresh `Guid.NewGuid().ToString("N")[..8]`; called after cloning any source tree

**Extend existing helpers:**
- `ResolveKey(nodeId)` — searches `_userDocs` values **first** (before `_systemDoc`), then `_systemDoc`, then `_dwustronnyDoc`; throws `InvalidOperationException` if not found in any document (never silently returns wrong key)
- `GetNodes(key)` — returns `_userDocs[key].Nodes` for user doc keys (keyed by the full `"bridge-user-{id}"` string)
- `IsDirty` — replace `bool IsDirty` with `Dictionary<string, bool> _dirtyFlags`; expose `bool IsDirtyFor(string key)` for page components to query; `CommitChange(key)` sets `_dirtyFlags[key] = true`; export/import clears only the affected key's flag
- `SaveToLocalStorageDebounced(key)` — for user doc keys, uses `_userCts[key]`; cancels previous CTS for that key before creating a new one
- `FlushPendingSaveAsync(key)` — for user doc keys: cancels and removes `_userCts[key]`, then saves `_userDocs[key]` if present; existing system/dwustronny branches unchanged
- `ImportAsync(Stream, string key)` — extend the hard-coded two-branch check to handle user doc keys: deserialize, call `SetAllExpanded`, store in `_userDocs[key]`, save to localStorage, clear `_dirtyFlags[key]`, fire `OnChange`
- `SaveIndexAsync()` — private; serializes and saves the index

---

## Implementation Phases

### Phase 1 — Data model + `TreeEditService` extension

1. Create `Bridge.App/Models/UserDocInfo.cs` with `Id`, `Name`, `CreatedAt` properties
2. Extend `Bridge.App/Services/TreeMutator.cs`:
   - Add `RegenerateIds(List<BidNode> nodes)` static method
3. Extend `Bridge.App/Services/TreeEditService.cs`:
   - Replace `bool IsDirty` with `Dictionary<string, bool> _dirtyFlags`; add `bool IsDirtyFor(string key)`
   - Update `CommitChange`, `ExportAsync`, `ImportAsync` to use the per-key flag
   - Add `_userDocs`, `_userCts`, `_userIndex` fields
   - Add `OnUserIndexChange` event
   - Implement `GetUserIndexAsync`, `GetUserDocumentAsync`, `CreateUserSystemAsync`, `DeleteUserSystemAsync`, `RenameUserSystemAsync`
   - Extend `ResolveKey` (user docs first, throw on not-found), `GetNodes`, `SaveToLocalStorageDebounced`, `FlushPendingSaveAsync`, `ImportAsync`
   - Private `SaveIndexAsync()`
4. No new JS helpers needed — existing `saveToStorage` / `loadFromStorage` / `clearStorage` cover it.

> **✅ Implemented (2026-05-09)**
> - `Bridge.App/Models/UserDocInfo.cs` created with `Id`, `Name`, `CreatedAt`
> - `Bridge.App/Services/IBridgeDataService.cs` extracted as interface; `BridgeDataService` implements it
> - `RegenerateIds(List<BidNode>)` added to `TreeMutator`
> - `TreeEditService` fully extended: `_userDocs`, `_userCts`, `_userIndex`, `_dirtyFlags`; `IsDirtyFor(key)` computed per-key; `OnUserIndexChange` event; all CRUD methods; `ResolveKey` throws on not-found; `FlushPendingSaveAsync`, `ImportAsync`, `ExportAsync` updated
> - `Program.cs` updated to register `IBridgeDataService` and pass it to `TreeEditService`
> - Build: 0 errors, 0 warnings

---

### Phase 2 — Management page (`MojeSystemyPage`)

5. Create `Bridge.App/Pages/MojeSystemyPage.razor` (`@page "/moje-systemy"`)
   - Loads user index on `OnInitializedAsync` via `EditService.GetUserIndexAsync()`
   - Subscribes to `EditService.OnUserIndexChange`; unsubscribes in `Dispose()`
   - Renders a list of existing systems (name, created date) with **Otwórz** and **Usuń** buttons
   - **Utwórz nowy system** button reveals an inline creation form:
     - Text input: system name (required, max 60 chars)
     - Select: "Od nowa" / "Skopiuj: System otwarć" / "Skopiuj: Licytacja dwustronna" / one entry per existing user system
     - Built-in options pass `cloneFromBuiltIn: "system"` or `"dwustronny"` (logical names)
     - **Utwórz** button → calls `EditService.CreateUserSystemAsync(...)` then navigates to `/moje-systemy/{newId}`
   - **Usuń** button: `bridgeEdit.confirmDelete(...)` → `EditService.DeleteUserSystemAsync(id)`
   - Inline rename: clicking system name enters rename mode (input + confirm)
   - Inject `NavigationManager` for navigation after create

> **✅ Implemented (2026-05-09)**
> - `Bridge.App/Pages/MojeSystemyPage.razor` created at route `/moje-systemy`
> - Renders list with inline rename (click name → input + Save/Cancel), `<a href>` Otwórz link, Usuń button
> - Create form: name input (max 60 chars) + clone-source select (Od nowa / built-ins / existing user systems)
> - Subscribes to `OnUserIndexChange`; navigates to `/moje-systemy/{newId}` on create
> - Fixed: Razor string interpolation `$"..."` is not valid inside double-quoted HTML attributes — replaced with `<a href="moje-systemy/@info.Id">`

---

### Phase 3 — System view/edit page (`UserSystemPage`)

6. Create `Bridge.App/Pages/UserSystemPage.razor` (`@page "/moje-systemy/{Id}"`)
   - Route parameter: `[Parameter] public string Id { get; set; }`
   - Pattern mirrors `SystemPage.razor`:
     - Inject `TreeEditService`, implements `IDisposable`
     - `OnInitializedAsync`: subscribe `OnChange`, call `EditService.GetUserDocumentAsync(Id)`
     - `Dispose()`: unsubscribe, `FlushPendingSaveAsync("bridge-user-{Id}")`
     - Toolbar: edit mode toggle, `SearchBar` (same as `SystemPage`), dirty indicator, Eksportuj JSON, Importuj JSON
     - `dirty indicator` reads `EditService.IsDirtyFor("bridge-user-{Id}")` (not the removed global `IsDirty`)
     - `HandleChange()`: refreshes `_document` from `EditService.GetUserDocumentAsync(Id)` (covers in-place mutations and post-import replacement)
     - Show `<TreeView>` over `_document!.Nodes`
   - Page title = `_document.SourceFile` (the system name)
   - On import: passes `"bridge-user-{Id}"` as the key to `EditService.ImportAsync` (handled by the extended branch)
   - On export: filename = `{system-name}-{id}.json`

> **✅ Implemented (2026-05-09)**
> - `Bridge.App/Pages/UserSystemPage.razor` created at route `/moje-systemy/{Id}`
> - `DocKey` computed as `TreeEditService.UserDocKey(Id)`; dirty indicator reads `EditService.IsDirtyFor(DocKey)`
> - `HandleChange()` refreshes doc via `GetUserDocumentAsync(Id)` to cover mutations and post-import replacement
> - `Dispose()` unsubscribes and calls `FlushPendingSaveAsync(DocKey)`
> - Export filename: invalid path chars stripped from system name

---

### Phase 4 — Dynamic navigation

7. Modify `Bridge.App/Layout/NavMenu.razor`:
   - Inject `TreeEditService`; implement `IDisposable`
   - `OnInitializedAsync`: load user index; subscribe `EditService.OnUserIndexChange += Refresh`
   - `Dispose()`: unsubscribe
   - `Refresh()`: reload index, call `StateHasChanged()`
   - Render a "Moje systemy" section after existing nav links:
     - `NavLink href="moje-systemy" Match="NavLinkMatch.All"` — management page link; `NavLinkMatch.All` prevents it staying active on sub-routes like `/moje-systemy/{id}`
     - Foreach user system: `NavLink href="moje-systemy/{info.Id}"` showing `info.Name`

> **✅ Implemented (2026-05-09)**
> - `NavMenu.razor` extended with `@implements IDisposable`, `TreeEditService` injection, `_userIndex` field
> - "Moje systemy" `NavLink` with `Match="NavLinkMatch.All"` added; per-system sub-links generated in `@foreach`
> - `OnUserIndexChange` subscribed in `OnInitializedAsync`, unsubscribed in `Dispose()`

---

### Phase 5 — CSS + tests

8. Add CSS to `Bridge.App/wwwroot/css/app.css`:
   - `.user-systems-list` — card-style list
   - `.user-system-item` — row with name + actions
   - `.user-system-create-form` — inline form panel
   - `.nav-subsection` — indented sub-items under "Moje systemy"

9. Add unit tests in `Bridge.App.Tests/`:
   - `TreeMutatorTests.cs` — add `RegenerateIds` tests: all IDs replaced, no duplicates, original list unchanged
   - `UserSystemServiceTests.cs` — requires mocking `IJSRuntime` and `BridgeDataService`; use **NSubstitute** (already a test dependency if present, otherwise add it):
     - `CreateUserSystemAsync` — new id appears in index, doc stored in `_userDocs`
     - `CreateUserSystemAsync` (clone) — cloned node IDs differ from source; source doc not mutated
     - `DeleteUserSystemAsync` — removed from index and `_userDocs`
     - `RenameUserSystemAsync` — name updated in index
     - `IsDirtyFor` — false before edit, true after mutation, false after export
     - `ResolveKey` — throws when node not found in any doc

> **✅ Implemented (2026-05-09)**
> - CSS section appended to `app.css`: `.user-systems-list`, `.user-system-item`, `.user-system-name`, `.user-system-date`, `.user-system-rename-input`, `.user-system-name-input`, `.user-system-create-section`, `.user-system-create-form`, `.user-system-create-actions`, `.btn-tree-action--danger`, `.nav-subsection`, `.nav-subsection .nav-link`
> - 5 new `RegenerateIds` tests added to `TreeMutatorTests.cs`
> - `Bridge.App.Tests/UserSystemServiceTests.cs` created: 24 tests with `FakeJSRuntime` (in-memory `IJSRuntime`) and NSubstitute mock for `IBridgeDataService`; covers create, clone, delete, rename, dirty tracking, `ResolveKey` error path, localStorage round-trip
> - NSubstitute 5.3.0 and Microsoft.JSInterop 9.0.0 added to `Bridge.App.Tests.csproj`
> - All 75 tests passing (51 pre-existing + 5 RegenerateIds + 24 UserSystemServiceTests), 0 build errors

---

## Relevant Files

| File | Change |
|---|---|
| `Bridge.App/Models/UserDocInfo.cs` | **New** |
| `Bridge.App/Services/TreeEditService.cs` | **Extended** (no breaking changes to existing API) |
| `Bridge.App/Services/TreeMutator.cs` | **Extended** — add `RegenerateIds` |
| `Bridge.App/Pages/MojeSystemyPage.razor` | **New** |
| `Bridge.App/Pages/UserSystemPage.razor` | **New** (mirrors `SystemPage.razor`) |
| `Bridge.App/Layout/NavMenu.razor` | **Modified** — dynamic "Moje systemy" section |
| `Bridge.App/wwwroot/css/app.css` | **Modified** — new styles |
| `Bridge.App.Tests/UserSystemServiceTests.cs` | **New** |

---

## Verification

1. `dotnet build Bridge.slnx` → 0 errors, 0 warnings
2. `dotnet test Bridge.App.Tests` → all passing
3. Navigate to Moje systemy → create "Test pusty" (empty) → empty tree appears on page
4. Create "Kopia systemu" (clone System otwarć) → full tree appears
5. Rename system from management page → name updates in nav immediately
6. Delete system → nav link disappears immediately
7. Edit tree on UserSystemPage (add/rename/delete nodes) → dirty indicator shows
8. Export JSON → valid file downloaded
9. Refresh page → changes persisted via localStorage

---

## Further Considerations

1. **Nav overflow:** If many systems are created, the sidebar can get long. A cap of 5–6 entries + "więcej…" link is an easy addition.
2. **Import as new system:** Currently "Importuj JSON" on `UserSystemPage` replaces the current document. If import should also be available on the management page to create a new system from file, that is a small addition.
3. **Name uniqueness:** The plan does not enforce unique names. Duplicate names are allowed (distinguishable by `Id`). Can be restricted if desired.
4. **NSubstitute dependency:** Before writing `UserSystemServiceTests`, verify `Bridge.App.Tests.csproj` already references NSubstitute. If not, add `<PackageReference Include="NSubstitute" Version="5.*" />`.
