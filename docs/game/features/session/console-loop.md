# Console session loop

## Summary

The ASCII console prototype runs an open-ended session: the player advances production one tick at a time, sees a clear-and-redraw bordered panel report after each tick (node state on the left, flow graph on the right, status above—with change annotations), and can quit at any prompt. There is no win or loss condition yet.

## When to read this

- Changing player-facing console commands, turn pacing at the session layer, or what is shown after a tick
- Explaining how play sessions relate to production ticks

## Session rules

- One player turn advances **one production tick** (same pacing as [production flows](../gameplay/production-flows.md)).
- At the prompt (interactive terminal: single keypress, no extra Enter after `q`):
  - **Enter** advances the next tick.
  - **`q`** / **`Q`** exits the session immediately.
  - Other keys are rejected with a short hint; the prompt repeats after a full screen redraw.
  - End of stdin (EOF) exits the session.
  - When stdin is redirected (non-interactive), the same rules apply in line mode (empty line = Enter, `q` line = quit).
- There is **no win/loss**. The player may progress indefinitely; nodes may run out of resources and stop meaningfully producing.
- After each advanced tick, the console **clears** and shows a **bordered panel screen**: a top status strip (`Tick N`, `actors:`), a left column of per-node subpanels (port stocks, progress, payroll timer on the left of each subpanel; preferred actor assignments and weights on the right), and a right column with a laid-out flow graph of nodes and edges. Leaf values that changed this tick are annotated as `previous → current` (Unicode right arrow). Unchanged leaves show the current value only.
- Before the first prompt, the console shows the same screen for **tick 0** (no change arrows) so the player has a baseline.

## Related docs

| Topic | Document |
|-------|----------|
| Production turn/tick rules | [Production flows](../gameplay/production-flows.md) |
| Console engineering / panel format | [Console client](../../../technical/features/ui/console-client.md) |
