# Console client

## Summary

`MarlothStrategy.Console.Client` owns the ASCII session: seed boot, prompt loop, and formatting of tick reports. `MarlothStrategy.Console.App` is a thin host. Simulation stays free of console I/O and exposes authoritative tick results via `AdvanceTickWithReport`.

## When to read this

- Changing console prompts, session loop, stock snapshot, or I/O table formatting
- Wiring Client to Simulation tick/report APIs
- Adding IDE play launch configuration for the console App

## Host and session

- App constructs `GameConfig` and calls `ConsoleClient.Run(config)`.
- Prefer a **single abort boundary** at process entry for fatal/unrecoverable errors ([error handling](../platform/error-handling.md)); do not scatter catches in Client.
- Client seeds with `MagicAgencySeed.CreateInitialState()`, prints a starting stocks snapshot, then loops until quit/EOF ([console loop](../../../game/features/session/console-loop.md)).
- On Enter: `var result = ProductionTick.AdvanceTickWithReport(state); state = result.State;` then print the I/O table for `result.Nodes`.
- Interactive input uses `Console.ReadKey(intercept: true)` so **Enter** and **`q`/`Q`** are single keypresses. When `Console.IsInputRedirected`, fall back to `ReadLine` (empty line / `q`) for agent piped smoke.
- Invalid prompt input reprints a short hint and re-prompts. Expected player mistakes are not exceptions.

## I/O table columns

After each advanced tick, print a header `Tick N` and a fixed-width table:

| Column | Source |
|--------|--------|
| Node | `NodeIoRow.NodeId` |
| Effort | `NodeIoRow.Effort` |
| Input | input signal type + available payload (`money` amount, or enchantment `vol/dark/fall`, or `-` if empty) |
| Consumed | `yes` / `no` (`NodeIoRow.Consumed`) |
| Residual | residual payload or `-` |
| Output | output signal type + produced payload (or `-`) |

Node row order matches Simulation’s tick iteration order.

## IDE play launch

Committed [`.vscode/launch.json`](../../../../.vscode/launch.json) / [`tasks.json`](../../../../.vscode/tasks.json) provide **Marloth Strategy (Console)** so humans can **play** via Run/F5 (or **Run Task**). Launch uses `node-terminal` to run `dotnet run` in the integrated terminal — no C#/`coreclr` debugger required. This is not a human debugging workflow; agents debug via CLI and tests ([AGENTS.md](../../../../AGENTS.md), [testing](../platform/testing.md)).

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing session rules | [Console loop](../../../game/features/session/console-loop.md) |
| Tick report API | [Production simulation](../gameplay/production-simulation.md) |
| Error handling | [Error handling](../platform/error-handling.md) |
