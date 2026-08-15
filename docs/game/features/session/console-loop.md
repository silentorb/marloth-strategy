# Console session loop

## Summary

The ASCII console prototype runs an open-ended session: the player advances production by **one tick** or by one configured **macro** time unit, sees a clear-and-redraw bordered panel report after each advance (node state on the left, flow graph on the right, status above—with change annotations), and can quit at any prompt. There is no win or loss condition yet.

## When to read this

- Changing player-facing console commands, turn pacing at the session layer, or what is shown after a tick
- Explaining how play sessions relate to production ticks and nested time partitions

## Session rules

- Session pacing:
  - **Enter** advances **one production tick**.
  - **Space** advances one configured **macro** unit (seed default: one **week** = 7 ticks). Macro advance runs that many ordinary ticks in sequence; it does **not** snap to the next calendar boundary when already mid-unit.
  - Nested time partitions (day / week / month by default) are labels over the tick counter; see [production flows](../gameplay/production-flows.md) and [production simulation](../../../technical/features/gameplay/production-simulation.md).
- At the prompt (interactive terminal: single keypress, no extra Enter after `q` or Space):
  - **Enter** advances the next tick.
  - **Space** advances one macro unit.
  - **`q`** / **`Q`** exits the session immediately.
  - Other keys are rejected with a short hint; the prompt repeats after a full screen redraw.
  - End of stdin (EOF) exits the session.
  - When stdin is redirected (non-interactive), the same rules apply in line mode (empty line = Enter, `space` line = Space, `q` line = quit).
- There is **no win/loss**. The player may progress indefinitely; nodes may run out of resources and stop meaningfully producing.
- After each advance, the console **clears** and shows a **bordered panel screen**: a top status strip (`Tick N`, calendar positions such as `month 1, week 1/4, day 1/7`, `scenario: {preset-or-random} seed {N}`, `actors:`), a left column of per-node subpanels (port stocks and cumulative **cycles** on the left of each subpanel; signed net change since tick 0 in a middle **Δ** column; preferred actor assignments and weights on the right), and a right column with a laid-out flow graph of nodes and edges. Leaf values that changed this advance are annotated as `previous → current` (Unicode right arrow), comparing the state before the whole advance to the state after. Unchanged leaves show the current value only.
- Before the first prompt, the console shows the same screen for **tick 0** (no change arrows) so the player has a baseline.

## Related docs

| Topic | Document |
|-------|----------|
| Production turn/tick rules | [Production flows](../gameplay/production-flows.md) |
| Console engineering / panel format | [Console client](../../../technical/features/ui/console-client.md) |
