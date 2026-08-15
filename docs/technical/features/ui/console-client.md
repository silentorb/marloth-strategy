# Console client

## Summary

`MarlothStrategy.Console.Client` owns the ASCII session: seed boot, prompt loop, and formatting of the clear-and-redraw panel screen. `MarlothStrategy.Console.App` is a thin host. Simulation stays free of console I/O and exposes authoritative tick results via `AdvanceTickWithReport`.

## When to read this

- Changing console prompts, session loop, or panel report formatting
- Wiring Client to Simulation tick/report APIs
- Adding IDE play launch configuration for the console App
- Changing box-drawing / panel layout helpers
- Adding or changing optional play-only dotenv / `.env` overrides on the App host (`SCENARIO_PRESET`, `SCENARIO_SEED`)

## Host and session

- Before constructing `GameConfig`, App loads an optional repo `.env` via DotNetEnv (`NoClobber` + `TraversePath`). Missing file is a no-op; unset keys must keep **production** play behavior. Shell-set env vars win over file values. Play-only developer tweaks — not used by tests (App is not on the test graph). See [`.env.example`](../../../../.env.example).
- App reads `SCENARIO_PRESET` (optional name; whitespace/empty → unset → random scenario) and `SCENARIO_SEED` (optional integer; unset → `Random.Shared.Next()`; invalid → fail-fast at the abort boundary) into `GameConfig`, then calls `ConsoleClient.Run(config)`.
- Prefer a **single abort boundary** at process entry for fatal/unrecoverable errors ([error handling](../platform/error-handling.md)); do not scatter catches in Client.
- Client boots with `ScenarioBootstrap.CreateInitialState(config)`, clears the screen, prints the tick-0 panel screen, then loops until quit/EOF ([console loop](../../../game/features/session/console-loop.md)).
- On Enter: capture prior state, `var result = ProductionTick.AdvanceTickWithReport(state); state = result.State;` then clear and print `FormatScreen(state, previous, result, …, baseline: tick0)` so change arrows use committed stocks (and cycles / actors) and the Δ column uses tick-0 baselines.
- Interactive input uses `Console.ReadKey(intercept: true)` so **Enter** and **`q`/`Q`** are single keypresses. When `Console.IsInputRedirected`, fall back to `ReadLine` (empty line / `q`) for agent piped smoke.
- Invalid prompt input clears, redraws the current screen, prints a short hint, and re-prompts. Expected player mistakes are not exceptions.
- The prompt sits **below** the framed report (not inside a panel).

## Screen redraw

- Each full report uses `Console.Clear()` then writes a fresh composed frame. Cursor-home / fixed-viewport scrolling is **not** used yet; if the frame is taller than the window, the terminal buffer handles scrolling.
- Frame width defaults to **120** columns. At the session boundary, when `Console.WindowWidth` is available and ≥ 40, clamp to `min(WindowWidth, 120)`. Formatter APIs take an explicit `width` for tests.

## Panel layout

The screen is three logical regions composed with classic single- and double-line box-drawing characters. The bottom split is **left:right = 1:1** (left interior is half of `totalWidth - 3`).

```text
╔══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╗
║ Marloth Strategy                                                                                                     ║
║ Tick N                                                                                                               ║
║ actors: …                                                                                                            ║
╠════════════════════════════════════════════════════════╤═════════════════════════════════════════════════════════════╣
║ node-id:            │ Δ      │ actor weight            │  (flow graph)                                               ║
║   port: …           │ +9     │                         │                                                             ║
╟─────────────────────┴────────┴─────────────────────────┤                                                             ║
║ other-node:         │ Δ      │ …                       │                                                             ║
║   …                 │        │                         │                                                             ║
╚════════════════════════════════════════════════════════╧═════════════════════════════════════════════════════════════╝
```

| Region | Border | Content |
|--------|--------|---------|
| Top status | Double outer | Title, `Tick N`, `actors:` line |
| Left state | Double outer shared with right; node **subpanels** stacked with single-line horizontal dividers (`╟`/`╢` against double verticals) | One subpanel per graph node (id order). Each subpanel is **split horizontally** into three columns when a tick-0 baseline is supplied: left = port / cycles leaves; middle = signed net change since tick 0 (`Δ`); right = preferred assignments as `actorId weight` (ordinal by actor id; blank when none). Without a baseline, the middle column is omitted |
| Right flow | Double outer; interior uses single-line node boxes | MSAGL Sugiyama + rectilinear routing with `RelativeFloatingPort` anchors (inputs on top, outputs on bottom, spaced per port); ASCII rasterizes those polylines |

Helpers: `BoxDrawing` (character constants), `AsciiCanvas` (char buffer), `PanelLayout` (compose top + split bottom; `LeftInteriorWidthForTotal`), `FlowGraphLayout` (MSAGL layout + floating ports), `FlowGraphWires` (orthogonal connector glyphs), `FlowGraphPrinter` (quantize MSAGL geometry onto a character grid).

## State content rules

Leaf formatting inside left subpanels (and header actors) follows:

| Rule | Detail |
|------|--------|
| Title / tick | Header lines: `Marloth Strategy`, then `Tick {N}` |
| Scenario | When `GameConfig` is supplied: `scenario: {preset-or-random} seed {N}` (`lab01`, `random`, etc.) |
| Actors | One `actors:` line: comma-separated actor ids (ordinal) when present; `0` when the roster is empty. With a previous state, annotate roster changes with `previous → current` (e.g. `boss, intern → 0` after a mass quit) |
| Nodes | One left subpanel per graph node, ordered by node id. Each subpanel splits horizontally: state leaves; optional signed Δ since tick 0; preferred assignments (`actorId weight`, ordinal by actor id) |
| Ports | Union of the node type’s input and output ports (ordinal by port id). Same-named input/output ports share one committed `PortSignals` stock and one display entry |
| Money (resource) | Committed stock on the port with change arrows from the prior tick (`previous → current` when different). Tick 0 shows committed stock only. Missing stock displays as `0`; numerics **rounded** to nearest integer |
| Enchantment (information) | Nested `hash` (abbreviated, 7 hex chars) plus `volume` / `designs` counts and floating-point `darkness` / `fallacy` when present; absent displays as `0`; volume/designs rounded for display; darkness/fallacy use trimmed fractional formatting so low amounts remain visible; change arrows from prior committed stock |
| Cycles | Shown for all seed node types (`enchant`, `testing`, `design`, `merge`, `sell`, `treasury`, `payroll`). Integer cumulative completed applications from `NodeCycles` (default `0`) since tick 0; with a previous state, annotate as `previous → current` when different |
| Accumulative Δ | When a tick-0 baseline state is supplied, a middle column shows signed net change since that baseline for numeric leaves (money, volume, designs, darkness, fallacy): `+N` / `-N` / `0` (fractional for darkness/fallacy). Header row shows `Δ`. Hash rows, port headers, and the cycles leaf leave the Δ cell blank (cycles are already lifetime) |
| Payroll money | Seed payroll has a `money` input (wage delivery); empty displays as `0` like other resource ports |
| Change annotations | When a previous state is supplied, compare rounded display strings per leaf; if different, print `previous → current` (U+2192); if equal, print current only. Tick 0 has no previous state (no arrows). Empty leaves use `0` (never `-`) |
| Flow graph | Right panel: MSAGL positions and port-anchored edge routes quantized to a character grid; single-line boxed node ids; directed connectors for all port-to-port edges (self-loops annotated); isolated nodes included; top-aligned in the column |

`AdvanceTickWithReport` still returns per-node I/O rows for Simulation consumers; the console Client does not print that table.

## IDE play launch

Committed [`.vscode/launch.json`](../../../../.vscode/launch.json) / [`tasks.json`](../../../../.vscode/tasks.json) provide **Marloth Strategy (Console)** so humans can **play** via Run/F5 (or **Run Task**). Launch uses `node-terminal` to run `dotnet run` in the integrated terminal — no C#/`coreclr` debugger required. This is not a human debugging workflow; agents debug via CLI and tests ([AGENTS.md](../../../../AGENTS.md), [testing](../platform/testing.md)).

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing session rules | [Console loop](../../../game/features/session/console-loop.md) |
| Tick report API | [Production simulation](../gameplay/production-simulation.md) |
| Error handling | [Error handling](../platform/error-handling.md) |
