# Console client

## Summary

`MarlothStrategy.Console.Client` owns the ASCII session: seed boot, prompt loop, and formatting of the clear-and-redraw panel screen. `MarlothStrategy.Console.App` is a thin host. Simulation stays free of console I/O and exposes authoritative tick results via `AdvanceTickWithReport`.

## When to read this

- Changing console prompts, session loop, or panel report formatting
- Wiring Client to Simulation tick/report APIs
- Adding IDE play launch configuration for the console App
- Changing box-drawing / panel layout helpers

## Host and session

- App constructs `GameConfig` and calls `ConsoleClient.Run(config)`.
- Prefer a **single abort boundary** at process entry for fatal/unrecoverable errors ([error handling](../platform/error-handling.md)); do not scatter catches in Client.
- Client seeds with `MagicAgencySeed.CreateInitialState()`, clears the screen, prints the tick-0 panel screen, then loops until quit/EOF ([console loop](../../../game/features/session/console-loop.md)).
- On Enter: capture prior state, `var result = ProductionTick.AdvanceTickWithReport(state); state = result.State;` then clear and print `FormatScreen(state, previous, result)` so change arrows use committed stocks (and payroll timer / actors).
- Interactive input uses `Console.ReadKey(intercept: true)` so **Enter** and **`q`/`Q`** are single keypresses. When `Console.IsInputRedirected`, fall back to `ReadLine` (empty line / `q`) for agent piped smoke.
- Invalid prompt input clears, redraws the current screen, prints a short hint, and re-prompts. Expected player mistakes are not exceptions.
- The prompt sits **below** the framed report (not inside a panel).

## Screen redraw

- Each full report uses `Console.Clear()` then writes a fresh composed frame. Cursor-home / fixed-viewport scrolling is **not** used yet; if the frame is taller than the window, the terminal buffer handles scrolling.
- Frame width defaults to **100** columns. At the session boundary, when `Console.WindowWidth` is available and ≥ 40, clamp to `min(WindowWidth, 100)`. Formatter APIs take an explicit `width` for tests.

## Panel layout

The screen is three logical regions composed with classic single- and double-line box-drawing characters. The bottom split is **left:right = 1:2** (left interior is one-third of `totalWidth - 3`).

```text
╔══════════════════════════════════════════════════════════════════════════════════════════════════╗
║ Marloth Strategy                                                                                 ║
║ Tick N                                                                                           ║
║ actors: …                                                                                        ║
╠═════════════════════════════════╤════════════════════════════════════════════════════════════════╣
║ node-id:                        │  (flow graph)                                                  ║
║   port: …                       │                                                                ║
╟─────────────────────────────────┤                                                                ║
║ other-node:                     │                                                                ║
║   …                             │                                                                ║
╚═════════════════════════════════╧════════════════════════════════════════════════════════════════╝
```

| Region | Border | Content |
|--------|--------|---------|
| Top status | Double outer | Title, `Tick N`, `actors:` line |
| Left state | Double outer shared with right; node **subpanels** stacked with single-line horizontal dividers (`╟`/`╢` against double verticals) | One subpanel per graph node (id order): same port / timer / progress leaves as before |
| Right flow | Double outer; interior uses single-line node boxes | MSAGL Sugiyama layout of nodes and edges from `GameState.Graph` (node→node); all edges drawn as connectors — no residual edge list |

Helpers: `BoxDrawing` (character constants), `AsciiCanvas` (char buffer), `PanelLayout` (compose top + split bottom; `LeftInteriorWidthForTotal`), `FlowGraphLayout` (MSAGL Sugiyama), `FlowGraphWires` (orthogonal connector direction masks → corner/tee/cross glyphs), `FlowGraphPrinter` (quantize layout onto a character grid).

## State content rules

Leaf formatting inside left subpanels (and header actors) follows:

| Rule | Detail |
|------|--------|
| Title / tick | Header lines: `Marloth Strategy`, then `Tick {N}` |
| Actors | One `actors:` line: comma-separated actor ids when present; `0` when the roster is empty. With a previous state, annotate roster changes with `previous → current` (e.g. `intern → 0` after a mass quit) |
| Nodes | One left subpanel per graph node, ordered by node id |
| Ports | Union of the node type’s input and output ports (ordinal by port id). Same-named input/output ports share one committed `PortSignals` stock and one display entry |
| Money (resource) | Committed stock on the port with change arrows from the prior tick (`previous → current` when different). Tick 0 shows committed stock only. Missing stock displays as `0`; numerics **rounded** to nearest integer |
| Enchantment (information) | Nested `hash` (abbreviated, 7 hex chars) plus `volume` / `darkness` / `fallacy` aggregate counts when present; absent displays as `0`; counts rounded for display; change arrows from prior committed stock |
| Progress | Shown for all seed node types (`enchant`, `testing`, `merge`, `sell`, `treasury`, `payroll`). Rounded numeric from `NodeProgress` (default `0`) |
| Timer | Shown for `payroll` from `NodeTimers` (rounded display as integer string); change arrows when previous state is supplied |
| Payroll money | Seed payroll has a `money` input (wage delivery); empty displays as `0` like other resource ports |
| Change annotations | When a previous state is supplied, compare rounded display strings per leaf; if different, print `previous → current` (U+2192); if equal, print current only. Tick 0 has no previous state (no arrows). Empty leaves use `0` (never `-`) |
| Flow graph | Right panel: MSAGL Sugiyama positions quantized to a character grid; single-line boxed node ids; directed connectors for all node→node edges (self-loops annotated); isolated nodes included; top-aligned in the column |

`AdvanceTickWithReport` still returns per-node I/O rows for Simulation consumers; the console Client does not print that table.

## IDE play launch

Committed [`.vscode/launch.json`](../../../../.vscode/launch.json) / [`tasks.json`](../../../../.vscode/tasks.json) provide **Marloth Strategy (Console)** so humans can **play** via Run/F5 (or **Run Task**). Launch uses `node-terminal` to run `dotnet run` in the integrated terminal — no C#/`coreclr` debugger required. This is not a human debugging workflow; agents debug via CLI and tests ([AGENTS.md](../../../../AGENTS.md), [testing](../platform/testing.md)).

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing session rules | [Console loop](../../../game/features/session/console-loop.md) |
| Tick report API | [Production simulation](../gameplay/production-simulation.md) |
| Error handling | [Error handling](../platform/error-handling.md) |
