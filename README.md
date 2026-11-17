# TidyUI

TidyUI is a beginner-friendly .NET terminal UI library for arranging responsive text panels in console applications. It provides nested tiling layouts, Unicode borders, text wrapping, and a buffered renderer that updates the terminal when its size changes.

## Compatibility

- **Target framework:** .NET 10 (`net10.0`)
- **Platforms:** Windows, Linux, and macOS console applications with an interactive terminal
- **Terminal:** Uses the .NET `Console` APIs; redirected standard output is not supported by the interactive renderer

## Installation

After the preview is published, install it with:

```sh
dotnet add package TidyUI --version 0.1.0-preview.1
```

## Quick start

```csharp
using TidyUI;

UI.UpdateInterval(100); // milliseconds; 100 ms is the default
UI.AutoResize(true);   // enabled by default

var logs = UI.Panel(
    "Application Logs",
    "TidyUI is running. Resize the terminal to see the layout adapt.");
var status = UI.Panel("System Status", "Press Ctrl+C to exit.");

var layout = UI.SplitVertical(logs, status, ratio: 0.5);
UI.Run(layout);
```

`UI.Run` renders until the process is interrupted or its cancellation token is canceled. For asynchronous hosts, use `await UI.RunAsync(layout, cancellationToken)`.

## Panels and text

Create a panel with `UI.Panel(title, content, padding, truncate)`. Content wraps by default and is clipped to the panel's interior. Set `truncate: true` to clip overflowing lines and content with an ellipsis instead. Padding is measured in terminal cells.

```csharp
var wrapped = UI.Panel("Logs", "Long text wraps to fit the available panel width.");
var clipped = UI.Panel("Status", "A long value can be clipped…", truncate: true);
```

Panels have a single-line Unicode border style. When the assigned region is one cell wide or high, TidyUI uses a dot (`·`) as its minimal fallback; an empty region renders no cells.

## Splitting layouts

- `UI.SplitVertical(first, second, ratio)` arranges children left and right, with a vertical dividing line.
- `UI.SplitHorizontal(first, second, ratio)` arranges children top and bottom, with a horizontal dividing line.

The ratio is the first child's share of the available cells and defaults to `0.5`. Splits can be nested. Layout structure is fixed when rendering starts.

```csharp
var layout = UI.SplitVertical(
    UI.Panel("Navigation", "Home\nSettings"),
    UI.SplitHorizontal(
        UI.Panel("Output", "Build succeeded."),
        UI.Panel("Details", "No warnings."),
        ratio: 0.65),
    ratio: 0.25);
```

## Resizing and update interval

Automatic resize monitoring is enabled by default. TidyUI polls terminal dimensions every 100 milliseconds and recalculates the layout when a changed size is observed. Change the polling interval while rendering with `UI.UpdateInterval(milliseconds)`; values must be positive. Use `UI.AutoResize(false)` to disable layout recalculation, then `UI.AutoResize(true)` to re-enable it.

The renderer reserves the final terminal column on terminals wider than one cell to avoid automatic line wrapping at the right edge. If the dimensions change during output, it abandons that frame, invalidates its previous-frame buffer, and retries after the configured polling interval. Terminal output and restoration failures are reported through `TerminalRenderException`.

## Known limitations

- Targets .NET 10 only.
- Layouts cannot be structurally changed while the renderer is running.
- Text is handled as UTF-16 characters; full grapheme clusters and double-width terminal characters are not measured.
- The library currently has one border style and no colors, keyboard input, menus, buttons, tables, or animation.
- The renderer needs an interactive terminal and does not support redirected standard output.
- A terminal can resize during an individual operating-system write. TidyUI detects size changes around writes and recovers, but output cannot be atomic with an external resize event.

## Demo and tests

Run the demo:

```sh
dotnet run --project TidyUI.Demo
```

Run the test suite:

```sh
dotnet test TidyUI.slnx
```

## Planned

These items are not implemented in this release:

- Custom border styles and colors
- `panel.WriteLine()` and `UI.WriteLine()` convenience methods
- A beginner-friendly project template

## License

Copyright (c) 2026 quake_20

TidyUI is licensed under the MIT License. See [LICENSE](LICENSE) for the full terms.

## Release metadata status

Source repository: [github.com/Andrew281003/TidyUI](https://github.com/Andrew281003/TidyUI).
