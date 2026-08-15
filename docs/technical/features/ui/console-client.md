# Console client

## Summary

`MarlothStrategy.Console.Client` owns the ASCII session: seed boot, prompt loop, modal screen selection, and formatting of clear-and-redraw panel screens. `MarlothStrategy.Console.App` is a thin host. Simulation stays free of console I/O and exposes authoritative tick results via `AdvanceTickWithReport`.

## When to read this

- Changing console prompts, session loop, modal screens, or panel report formatting
- Wiring Client to Simulation tick/report APIs (including multi-tick macro advance)
- Adding IDE play launch configuration for the console App
- Changing box-drawing / panel layout helpers
- Adding or changing optional play-only dotenv / `.env` overrides on the App host (`SCENARIO_PRESET`, `SCENARIO_SEED`)
- Changing persistent player preferences under untracked `./config/user.json` (step resolution)
- Changing calendar header formatting derived from time partitions

## Host and session

- Before constructing `GameConfig`, App loads an optional repo `.env` via DotNetEnv (`NoClobber` + `TraversePath`). Missing file is a no-op; unset keys must keep **production** play behavior. Shell-set env vars win over file values. Play-only developer tweaks — not used by tests (App is not on the test graph). See [`.env.example`](../../../../.env.example).
- App reads `SCENARIO_PRESET` (optional name; whitespace/empty → unset → random scenario) and `SCENARIO_SEED` (optional integer; unset → `Random.Shared.Next()`; invalid → fail-fast at the abort boundary) into `GameConfig`, then calls `ConsoleClient.Run(config, userConfigPath)` with `./config/user.json` relative to the process working directory.
- Prefer a **single abort boundary** at process entry for fatal/unrecoverable errors ([error handling](../platform/error-handling.md)); do not scatter catches in Client.
- Client boots with `ScenarioBootstrap.CreateInitialState(config)`, loads `UserConfig` from the supplied path (missing file → `TimePartitions.DefaultStepResolution` without creating a file; invalid JSON / unknown members / unavailable `stepResolution` → `InvalidOperationException` with path), clears the screen, prints the tick-0 **workflow** screen, then loops until quit/EOF ([console loop](../../../game/features/session/console-loop.md)).
- On Enter: capture prior state, `AdvanceTicksWithReport(state, TicksPer(stepResolution))`, then clear and print the **current** modal screen with change arrows when applicable. On the workflow screen, `FormatScreen(state, previous, result, …, baseline: tick0)` so change arrows use committed stocks (and cycles / actors) and the Δ column uses tick-0 baselines. Multi-tick steps compare pre-step state to post-step state (one summary for the whole interval). The last tick's `ProductionTickResult` is passed through for API symmetry; the Client does not print per-node I/O rows.
- On `-` / `+` (`=` unshifted, `+` shifted, or numpad Add): move one step finer/coarser along `TimePartitions.StepResolutions` (`tick` then configured units). Endpoint presses are no-ops. On a real change, **save** `user.json` first (create `config/` as needed; atomic temp+replace), then update the in-memory selection and redraw without advancing time. Write failures abort through the App boundary so interactive selection cannot diverge from a failed persist.
- On `w` / `W` / `a` / `A`: select the workflow or actors screen and redraw without advancing time (no change arrows). Selecting the already-active screen is a no-op redraw. Session pacing keys (Enter, `-`/`+`, `n`, `q`) remain active on every screen.
- On `n` / `N`: create a fresh initial state from the session's existing `GameConfig`, replace the tick-0 baseline, reset the active screen to **workflow**, and redraw the tick-0 workflow screen while keeping the selected step resolution. A seeded random scenario therefore restarts deterministically rather than choosing a new seed.
- Interactive input uses `Console.ReadKey(intercept: true)` so **Enter**, **`-`**, **`=`/`+`**, **`w`/`W`**, **`a`/`A`**, **`n`/`N`**, and **`q`/`Q`** are single keypresses. When `Console.IsInputRedirected`, fall back to `ReadLine` (empty line / `-` / `+` / `=` / `w` / `a` / `n` / `q`) for agent piped smoke. Decoding lives in `PromptDecoder`.
- Invalid prompt input clears, redraws the current screen, prints a short hint, and re-prompts. Expected player mistakes are not exceptions.
- The prompt sits **below** the framed report (not inside a panel). Prompt text names the current step resolution and screen keys (e.g. `Enter = next week, - = finer, + (= key) = coarser, w/a = workflow/actors, n = new game, q = quit>`).

## Screens

`ScreenId` selects which formatter `DrawReport` invokes. Both screens share the top status strip via `StatusHeader.Build` (title, `Tick N`, calendar, `screen: {workflow|actors}`, optional `scenario:`, `actors:` roster line).

| Screen | Key | Formatter | Layout |
|--------|-----|-----------|--------|
| Workflow (default) | `w` | `TickReportPrinter.FormatScreen` | Header + left node subpanels \| right flow graph (`PanelLayout.Compose`) |
| Actors | `a` | `ActorsScreenPrinter.FormatScreen` | Header + full-width stacked actor subpanels (`PanelLayout.ComposeStacked`) |

## Persistent user config

- Path: `./config/user.json` under the process working directory (repo-root when launched from the solution). Gitignored via `/config/` so committed Simulation `config/` stays tracked.
- Schema (camelCase, indented on write):

```json
{
  "stepResolution": "week"
}
```

- Read options match Simulation loaders (case-insensitive properties, comments/trailing commas allowed, unknown members disallowed). Missing file is an expected default; corrupt or invalid values fail fast with the path in the message.
- Ownership: `UserConfigStore` in Console.Client; App supplies the path. Do not load this from `AppContext.BaseDirectory/config` (that tree is committed game data).

## Screen redraw

- Each full report uses `Console.Clear()` then writes a fresh composed frame. Cursor-home / fixed-viewport scrolling is **not** used yet; if the frame is taller than the window, the terminal buffer handles scrolling.
- Frame width defaults to **120** columns. At the session boundary, when `Console.WindowWidth` is available and ≥ 40, clamp to `min(WindowWidth, 120)`. Formatter APIs take an explicit `width` for tests.

## Workflow panel layout

The workflow screen is three logical regions composed with classic single- and double-line box-drawing characters. The bottom split is **left:right = 1:1** (left interior is half of `totalWidth - 3`).

```text
╔══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╗
║ Marloth Strategy                                                                                                     ║
║ Tick N                                                                                                               ║
║ month 1, week 1/4, day 1/7                                                                                           ║
║ screen: workflow                                                                                                     ║
║ actors: …                                                                                                            ║
╠════════════════════════════════════════════════════════╦═════════════════════════════════════════════════════════════╣
╠══════════════════════════  node-id  ═══════════════════╣                                                             ║
║               │            │ Δ      │ actor weight     ║  (flow graph)                                               ║
║   enchantment:│            │        │                  ║                                                             ║
║     volume:   │  0 → 9     │+9      │                  ║                                                             ║
║   cycles:     │  0         │        │                  ║                                                             ║
╠════════════════════════════════════════════════════════╣                                                             ║
╠════════════════════════  other-node  ══════════════════╣                                                             ║
║               │            │ Δ      │ …                ║                                                             ║
║   money:      │100         │ 0      │                  ║                                                             ║
╚════════════════════════════════════════════════════════╩═════════════════════════════════════════════════════════════╝
```

| Region | Border | Content |
|--------|--------|---------|
| Top status | Double outer | Title, `Tick N`, calendar positions (largest → smallest), `screen: workflow`, optional scenario line, `actors:` line |
| Left state | Double outer shared with right; the **left/right split is a double vertical** (`║`, joining the frame with `╦` at the header split and `╩` at the floor); panels use double-line `╠`/`╣` horizontal dividers, and a titled panel adds an **extra double-line title rule** on the next line, carving the node id centered with two spaces on each side (`════  id  ════`). A titled panel therefore uses **two border lines**; the first panel reuses the header split as its divider | One subpanel per graph node (id order). Body is **split horizontally** into four columns when a tick-0 baseline is supplied: property name (indentation preserved) | value | signed net change since tick 0 (`Δ`, with a header row under the title) | preferred assignments as `actorId weight` (ordinal by actor id; blank when none). Without a baseline the `Δ` column is omitted. Vertical column dividers inside the body remain single-line `│` |
| Right flow | Double outer; interior uses single-line node boxes | MSAGL Sugiyama + rectilinear routing with `RelativeFloatingPort` anchors (inputs on top, outputs on bottom, spaced per port); ASCII rasterizes those polylines |

## Actors panel layout

The actors screen uses the same top status strip (`screen: actors`) and full-width stacked subpanels (one per actor, ordinal by actor id). Each actor subpanel keeps its divider and adds a double-line title rule for the actor id on the next line; the body splits horizontally into properties | assignments.

```text
╔══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╗
║ Marloth Strategy                                                                                                     ║
║ Tick N                                                                                                               ║
║ month 1, week 1/4, day 1/7                                                                                           ║
║ screen: actors                                                                                                       ║
║ actors: …                                                                                                            ║
╠══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╣
╠══════════════════════════════════════  intern  ══════════════════════════════════════════════════════════════════════╣
║   capacity:    │ 1                                                         │ enchant 1                               ║
║   wage:        │ 2                                                         │ sell 1                                  ║
║   stats:       │                                                           │                                         ║
║     enchanting:│10                                                         │                                         ║
╠══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╣
╠═══════════════════════════════════════  boss  ═══════════════════════════════════════════════════════════════════════╣
║   capacity:    │ 1                                                         │ …                                       ║
╚══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╝
```

| Region | Border | Content |
|--------|--------|---------|
| Top status | Double outer | Same shared header as workflow with `screen: actors` |
| Actor subpanels | Full-width; panels use double-line `╠`/`╣` horizontal dividers and titled panels add an extra double-line title rule on the next line, carving the actor id centered with two spaces on each side; empty-roster fallback is untitled | Body columns: property name (indentation preserved) | value | preferred assignments as `nodeId weight` (ordinal by node id; blank when none). Properties are `capacity`, `wage` (`none` when unpaid), `stats` (ordinal keys; value `none` when empty, otherwise nested stat rows). Vertical column dividers remain single-line `│`. Empty roster: one untitled subpanel `actors: 0` |

Helpers: `BoxDrawing` (character constants), `AsciiCanvas` (char buffer), `PanelLayout` / `PanelSubpanel` (`Compose` for workflow split; `ComposeStacked` for full-width actor stacks; optional double-line title rules; `LeftInteriorWidthForTotal`), `PanelColumns` (shared key-column sizing and cell clipping), `StatusHeader` (shared top strip), `DisplayFormatting` (decimal weight/capacity), `FlowGraphLayout` (MSAGL layout + floating ports), `FlowGraphWires` (orthogonal connector glyphs), `FlowGraphPrinter` (quantize MSAGL geometry onto a character grid).

## State content rules

Leaf formatting inside workflow left subpanels (and header actors) follows:

| Rule | Detail |
|------|--------|
| Title / tick | Header lines: `Marloth Strategy`, then `Tick {N}` |
| Calendar | Header line from `TimePartitions.PositionsAt(Tick)`: largest unit first, e.g. `month 1, week 1/4, day 1/7` |
| Screen | Header line `screen: workflow` or `screen: actors` |
| Scenario | When `GameConfig` is supplied: `scenario: {preset-or-random} seed {N}` (`lab01`, `random`, etc.) |
| Actors | One `actors:` line: comma-separated actor ids (ordinal) when present; `0` when the roster is empty. With a previous state, annotate roster changes with `previous → current` (e.g. `a, b, c → a` after unpaid departures) |
| Subpanel borders and titles | Panel outer borders and horizontal boundaries are double-line. Node and actor ids render as centered double-line title rules (`════  id  ════`, two spaces each side) on an **extra line below** the panel's double divider, so a titled panel uses two border lines. Right junction is `╣` in both cases: in the workflow left column the double split vertical passes through it, and on full-width stacks it meets the outer border. Only vertical dividers between content columns *inside* a panel body remain single-line `│`; the panel-to-panel and left/right split borders are all double-line. Title is clipped when the panel is too narrow; falls back to a plain double rule when even one clipped character cannot fit. Ids are not repeated as body rows |
| Nodes | One left subpanel per graph node, ordered by node id. Body splits horizontally: property name; value; optional signed Δ since tick 0; preferred assignments (`actorId weight`, ordinal by actor id) |
| Property name / value columns | Leaf names and their values occupy separate columns separated by a single-line `│`. The name column preserves nesting indentation (two spaces per level, e.g. `  enchantment:` then `    volume:`); the value column holds the formatted value and any `previous → current` annotation. Parent rows such as `  enchantment:` and `  stats:` have an empty value cell. One key-column width is shared by every sibling subpanel so values line up down the whole screen; it is sized to the longest key but always leaves at least 8 cells for values, clipping long names first on narrow terminals |
| Numeric magnitude alignment | Cells whose first whitespace-delimited token is a plain number (`0`, `-15`, `2.5`) are left-padded so their integer parts end at the same offset, stacking ones under ones and tens under tens without right-aligning to the column edge. The pad width is shared by every sibling subpanel, so a three-digit `treasury` money value shifts the single-digit `payroll` value right by two. Change annotations align on their leading number (`  0 → 30` under `100 → 202`); cells that do not start with a number — hashes such as `3eeab2f → a4a4711`, `none`, and blank parent rows — stay flush left. Applies to the value column on both screens and to the workflow Δ column, where the sign counts toward the integer width |
| Ports | Union of the node type’s input and output ports (ordinal by port id). Same-named input/output ports share one committed `PortSignals` stock and one display entry |
| Money (resource) | Committed stock on the port with change arrows from the prior tick (`previous → current` when different). Tick 0 shows committed stock only. Missing stock displays as `0`; numerics **rounded** to nearest integer |
| Enchantment (information) | Nested `hash` (abbreviated, 7 hex chars) plus `volume` / `designs` counts and floating-point `darkness` / `fallacy` when present; absent displays as `0`; volume/designs rounded for display; darkness/fallacy use trimmed fractional formatting so low amounts remain visible; change arrows from prior committed stock |
| Cycles | Shown for all seed node types (`enchant`, `testing`, `design`, `merge`, `sell`, `treasury`, `payroll`). Integer cumulative completed applications from `NodeCycles` (default `0`) since tick 0; with a previous state, annotate as `previous → current` when different |
| Accumulative Δ | When a tick-0 baseline state is supplied, a middle column shows signed net change since that baseline for numeric leaves (money, volume, designs, darkness, fallacy): `+N` / `-N` / `0` (fractional for darkness/fallacy). Money Δ adds `GameState.PortFlowTotals` (lifetime throughput) to committed stock so pass-through ports still report: `sell` money counts sale income `+`, `payroll` money counts disbursed wages `-`, `treasury` money tracks its pile. Header row under the title shows `Δ`. Hash rows, port headers, and the cycles leaf leave the Δ cell blank (cycles are already lifetime) |
| Payroll money | Seed payroll has a `money` input (wage delivery); empty displays as `0` like other resource ports |
| Change annotations | When a previous state is supplied, compare rounded display strings per leaf; if different, print `previous → current` (U+2192); if equal, print current only. Tick 0 has no previous state (no arrows). Empty leaves use `0` (never `-`) |
| Flow graph | Right panel: MSAGL positions and port-anchored edge routes quantized to a character grid; single-line boxed node ids; directed connectors for all port-to-port edges (self-loops annotated); isolated nodes included; top-aligned in the column |
| Actor properties | Actors screen only: `capacity`, nullable `wage` (`none` when unpaid), ordinal `stats` map (`stats: none` when empty); actor id is the subpanel title, not a body row |
| Actor assignments | Actors screen only: `nodeId weight` rows for that actor’s preferred assignments (ordinal by node id) |

`AdvanceTickWithReport` still returns per-node I/O rows for Simulation consumers; the console Client does not print that table.

## IDE play launch

Committed [`.vscode/launch.json`](../../../../.vscode/launch.json) / [`tasks.json`](../../../../.vscode/tasks.json) provide **Marloth Strategy (Console)** so humans can **play** via Run/F5 (or **Run Task**). Launch uses `node-terminal` to run `dotnet run` in the integrated terminal — no C#/`coreclr` debugger required. This is not a human debugging workflow; agents debug via CLI and tests ([AGENTS.md](../../../../AGENTS.md), [testing](../platform/testing.md)).

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing session rules | [Console loop](../../../game/features/session/console-loop.md) |
| Tick report API | [Production simulation](../gameplay/production-simulation.md) |
| Error handling | [Error handling](../platform/error-handling.md) |
