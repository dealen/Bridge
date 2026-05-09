# Bridge Bidding System Viewer

A .NET 9 tool suite for browsing a bridge bidding system from ARIA-tree HTML source files.

Two components:

- **Bridge.Converter** — CLI tool that converts ARIA-tree HTML files into structured JSON
- **Bridge.App** — Blazor WebAssembly app that renders the JSON as a collapsible, searchable tree

---

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download)

---

## Quick Start (app already set up)

The JSON data files are already generated and committed. To run the viewer:

```bash
cd Bridge.App
dotnet run
```

Then open http://localhost:5136 in your browser.

Two tabs are available:
- **System otwarć** — the opening system (`System_ania.html`, 11 top-level sections)
- **Licytacja dwustronna** — competitive bidding (`dwustronny_ania.html`, 20 top-level sections)

---

## Using the App

| Feature | How to use |
|---|---|
| Expand / collapse a node | Click anywhere on the node row |
| Expand all nodes | Click **Rozwiń wszystko** |
| Collapse all nodes | Click **Zwiń wszystko** |
| Search | Type in the search box — results update after 300 ms |
| Clear search | Click **×** next to the search box, or clear the text |
| Match count | Shown as **Znaleziono: N** while a search is active |

Search is case-insensitive and matches any substring of a node label. All ancestors of matching nodes are automatically expanded. Matching text is highlighted in yellow.

Suit symbols are coloured: ♥ ♦ in red, ♠ ♣ in black.

---

## Regenerating the JSON (Bridge.Converter)

Run this when the source HTML files change.

```bash
cd Bridge.Converter

# System otwarć
dotnet run -- \
  --input ../System_ania.html \
  --output ../Bridge.App/wwwroot/data/system.json \
  --pretty

# Licytacja dwustronna
dotnet run -- \
  --input ../dwustronny_ania.html \
  --output ../Bridge.App/wwwroot/data/dwustronny.json \
  --pretty
```

The converter prints a node-count summary per depth level when it finishes. Verify that `system.json` shows 11 top-level nodes and `dwustronny.json` shows 20.

### Converter options

| Option | Required | Description |
|---|---|---|
| `--input <file>` | Yes | Path to the source HTML file |
| `--output <file>` | Yes | Path for the generated JSON file |
| `--suit-map <file>` | No | Custom SVG-path-to-symbol mapping (default: `suits.json` next to the executable) |
| `--pretty` | No | Pretty-print the JSON output |

### Using a custom suit map

If you have a different HTML source that uses different SVG paths for suit symbols, create a custom `suits.json`:

```json
{
  "svgClassToSkip": "arrow",
  "pathMappings": {
    "M480.25 156.355": "♥",
    "M458.915 307.705": "♠",
    "M431.76 256":      "♦",
    "M477.443 295.143": "♣"
  }
}
```

`svgClassToSkip` is the CSS class of the expand-arrow SVG — nodes with this class are skipped and not treated as suit symbols. Pass the file with `--suit-map path/to/custom-suits.json`.

---

## Running Tests

```bash
# Converter tests (unit + integration, 21 tests)
dotnet test Bridge.Converter.Tests/

# App tests (TreeFilterHelper, 17 tests)
dotnet test Bridge.App.Tests/
```

Or run all at once (outside the sandbox):

```bash
dotnet test Bridge.slnx
```

---

## Project Structure

```
Bridge.slnx
├── Bridge.Converter/          CLI converter tool
│   ├── Program.cs             Entry point (--input, --output, --suit-map, --pretty)
│   ├── AriaTreeParser.cs      Walks ARIA-tree HTML → List<BidNode>
│   ├── SvgResolver.cs         SVG path → Unicode symbol (♥ ♠ ♦ ♣)
│   ├── suits.json             Default suit SVG path mappings
│   └── Models/
│       ├── BidNode.cs
│       └── BridgeDocument.cs
│
├── Bridge.Converter.Tests/    xUnit tests for the converter
│
├── Bridge.App/                Blazor WebAssembly viewer
│   ├── Components/
│   │   ├── TreeNode.razor     Recursive node renderer with suit colouring
│   │   ├── TreeView.razor     Top-level list; ExpandAll / CollapseAll / ApplyFilter
│   │   └── SearchBar.razor    Debounced search input with match count
│   ├── Pages/
│   │   ├── SystemPage.razor   /system
│   │   └── DwustronnyPage.razor  /dwustronny
│   ├── Services/
│   │   ├── BridgeDataService.cs  Loads and caches JSON via HttpClient
│   │   └── TreeFilterHelper.cs   Filter + ancestor-expansion logic
│   └── wwwroot/data/
│       ├── system.json        Generated from System_ania.html
│       └── dwustronny.json    Generated from dwustronny_ania.html
│
└── Bridge.App.Tests/          xUnit tests for the app logic
```
