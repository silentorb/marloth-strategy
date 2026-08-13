# Console client

## Summary

`MarlothStrategy.Console.Client` owns the ASCII session: seed boot, prompt loop, and formatting of tick state snapshots. `MarlothStrategy.Console.App` is a thin host. Simulation stays free of console I/O and exposes authoritative tick results via `AdvanceTickWithReport`.

## When to read this

- Changing console prompts, session loop, or YAML-like state snapshot formatting
- Wiring Client to Simulation tick/report APIs
- Adding IDE play launch configuration for the console App

## Host and session

- App constructs `GameConfig` and calls `ConsoleClient.Run(config)`.
- Prefer a **single abort boundary** at process entry for fatal/unrecoverable errors ([error handling](../platform/error-handling.md)); do not scatter catches in Client.
- Client seeds with `MagicAgencySeed.CreateInitialState()`, prints a tick-0 state snapshot, then loops until quit/EOF ([console loop](../../../game/features/session/console-loop.md)).
- On Enter: capture prior state, `var result = ProductionTick.AdvanceTickWithReport(state); state = result.State;` then print `FormatStateSnapshot(state, previous, result)` so change arrows use committed stocks (and payroll timer / actors).
- Interactive input uses `Console.ReadKey(intercept: true)` so **Enter** and **`q`/`Q`** are single keypresses. When `Console.IsInputRedirected`, fall back to `ReadLine` (empty line / `q`) for agent piped smoke.
- Invalid prompt input reprints a short hint and re-prompts. Expected player mistakes are not exceptions.

## State snapshot format

After each advanced tick (and once at tick 0), print a Markdown-style heading and a YAML-like tree:

```text
## Tick N

actors: …

node-id:
  port: …
  progress: …

other-node:
  …
```

Rules:

| Rule | Detail |
|------|--------|
| Heading | `## Tick {N}` then a blank line |
| Actors | One `actors:` line after the heading: comma-separated actor ids when present; `0` when the roster is empty. With a previous state, annotate roster changes with `previous → current` (e.g. `intern → 0` after a mass quit) |
| Nodes | One block per graph node, ordered by node id; blank line between node blocks |
| Ports | Union of the node type’s input and output ports (ordinal by port id). Same-named input/output ports share one committed `PortSignals` stock and one display entry |
| Money (resource) | Committed stock on the port with change arrows from the prior tick (`previous → current` when different). Tick 0 shows committed stock only. Missing stock displays as `0`; numerics **rounded** to nearest integer |
| Enchantment (information) | Nested `volume` / `darkness` / `fallacy` when present; absent displays as `0`; same rounding; change arrows from prior committed stock |
| Progress | Shown for all seed node types (`enchant`, `testing`, `sell`, `treasury`, `payroll`). Rounded numeric from `NodeProgress` (default `0`) |
| Timer | Shown for `payroll` from `NodeTimers` (rounded display as integer string); change arrows when previous state is supplied |
| Change annotations | When a previous state is supplied, compare rounded display strings per leaf; if different, print `previous → current` (U+2192); if equal, print current only. Tick 0 has no previous state (no arrows). Empty leaves use `0` (never `-`) |

`AdvanceTickWithReport` still returns per-node I/O rows for Simulation consumers; the console Client does not print that table.

## IDE play launch

Committed [`.vscode/launch.json`](../../../../.vscode/launch.json) / [`tasks.json`](../../../../.vscode/tasks.json) provide **Marloth Strategy (Console)** so humans can **play** via Run/F5 (or **Run Task**). Launch uses `node-terminal` to run `dotnet run` in the integrated terminal — no C#/`coreclr` debugger required. This is not a human debugging workflow; agents debug via CLI and tests ([AGENTS.md](../../../../AGENTS.md), [testing](../platform/testing.md)).

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing session rules | [Console loop](../../../game/features/session/console-loop.md) |
| Tick report API | [Production simulation](../gameplay/production-simulation.md) |
| Error handling | [Error handling](../platform/error-handling.md) |
