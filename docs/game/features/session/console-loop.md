# Console session loop

## Summary

The ASCII console prototype runs an open-ended session: the player advances production one tick at a time, sees a per-node input/output report after each tick, and can quit at any prompt. There is no win or loss condition yet.

## When to read this

- Changing player-facing console commands, turn pacing at the session layer, or what is shown after a tick
- Explaining how play sessions relate to production ticks

## Session rules

- One player turn advances **one production tick** (same pacing as [production flows](../gameplay/production-flows.md)).
- At the prompt (interactive terminal: single keypress, no extra Enter after `q`):
  - **Enter** advances the next tick.
  - **`q`** / **`Q`** exits the session immediately.
  - Other keys are rejected with a short hint; the prompt repeats.
  - End of stdin (EOF) exits the session.
  - When stdin is redirected (non-interactive), the same rules apply in line mode (empty line = Enter, `q` line = quit).
- There is **no win/loss**. The player may progress indefinitely; nodes may run out of resources and stop meaningfully producing.
- After each advanced tick, the console shows a **tabular per-node I/O report** (effort, available input, consumed, residual, produced output).
- Before the first prompt, the console shows a short **starting stocks** snapshot (tick 0) so the player has a baseline.

## Related docs

| Topic | Document |
|-------|----------|
| Production turn/tick rules | [Production flows](../gameplay/production-flows.md) |
| Console engineering / table columns | [Console client](../../../technical/features/ui/console-client.md) |
