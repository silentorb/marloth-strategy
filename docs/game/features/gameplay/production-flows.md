# Production flows

## Summary

Gameplay centers on **production flows**: directed graphs of **actions** that transform signals. Players optimize dysfunctional configurations. Flows may be **circular**—an action’s input can depend on another action’s output in a cycle.

The first domain is a **magic agency** that creates enchantments as a service.

## When to read this

- Designing or changing production, resources, actions, actors, or assignment rules
- Adding a new production domain or seed scenario
- Explaining turn/tick pacing for production

## Actions and signals

- Each **action** is a station in the graph: a function with typed **inputs** and **outputs**.
- Values that move through the graph are **signals**. There are two kinds:

| Kind | Behavior | Example |
|------|----------|---------|
| **Resource** | Primitive scalar; **added** and **subtracted** when routed or consumed | **money** (quantity) |
| **Information** | Complex structure; **copied** on route and **mutated** only inside node logic | **enchantment** (single value with `volume`, `darkness`, `fallacy`) |

- Money signals carry a quantity of money. Enchantment signals carry a **single** enchantment (not a count).
- Actions consume input signals and produce output signals each production tick according to their node rules.
- Routing and fan-out only **copy** emitted values; they never mutate information.

## Actors, stats, and assignment

- An **actor** has a **capacity** and a dictionary of **stats** (e.g. `enchanting`, `sales`).
- Preferred **assignments** list which actions an actor may operate. Each tick, only actions whose **prerequisites** are met become **effective** assignments (for `enchant` / `sell`: an enchantment on the process input). Actors are not assigned to actions that fail prerequisites that tick.
- When an actor has multiple effective assignments, capacity is split equally: **assignment effort** = `capacity / effective count` for that actor. Efforts from several actors on one action **add**.
- Unassigned actions do nothing that tick.
- Progress gain on an action is `stat × assignmentEffort` for the relevant stat (`enchanting` on enchant, `sales` on sell). If an actor lacks that stat, the default is **1**.

Do not use “man/manning” in product language; use **assignment** / **assigned**.

## Progress, work effort, and cost

- Each action tracks runtime **progress** (carried across ticks).
- Node config **`effort`** is the work units required per application (mutation or sale). Config **`cost`** is money charged from the agency treasury per successful application.
- While `progress >= effort` and the treasury can pay `cost`, the action may apply one or more times in a tick; each application subtracts `effort` from progress and charges `cost`.
- **`enchant`:** with assignment effort and an input enchantment, the node always forwards the enchantment to its output—either **mutate** (one or more paid applications) or **pass-through** (emit the same value unchanged). Progress may still rise on a pass-through tick.
- **`sell`:** when progress and treasury allow, consume the enchantment and produce money; otherwise leave the input as residual (no information pass-through).

## Circular flows and ticks

- Production updates run in discrete **ticks**. For now, one player turn advances one production tick.
- To avoid resolution-order races on cycles, every action computes outputs from **inputs committed on the previous tick** (buffered signals). Same-tick outputs are not visible to other actions until the next tick.
- Information ports hold a single value. If a consumer still has stock after residuals, a routed copy to that port is **skipped** that commit (occupancy).

## Magic agency seed (initial configuration)

| Piece | Detail |
|-------|--------|
| Actors | One actor (`intern`), capacity `1.0`, stats `enchanting: 10`, `sales: 10` (from `config/actors/intern.json`) |
| Actions | `enchant` — mutate or pass-through an enchantment; `sell` — consume an enchantment, produce money |
| Enchant formula | On each mutation: `volume + volumeDelta`, `darkness + darknessDelta`, `fallacy + darkness + fallacyConstant` (defaults `10` / `1` / `1`) |
| Sell formula | money = `max(payoutFloor, volume - fallacy)` (default `payoutFloor=0`) |
| Graph | Enchantment **fans out**: copy `enchant` → `enchant` (feedback) and copy `enchant` → `sell`; money flows `sell` → `enchant` (treasury) |
| Assignment | Preferred: `intern` → both actions; effective set drops nodes without an enchantment input |
| Work / cost | Config `effort: 10`, `cost: 20` per type; progress gain uses actor stats × assignment effort |
| Config | Node numerics in `config/node-types/{enchant,sell}.json`; actors in `config/actors/*.json`; port layouts and seed wiring stay in code |
| Starting stocks | `enchant` enchantment = `(volume:0, darkness:0, fallacy:0)`; `enchant` money = `100`; `sell` enchantment empty; node progress `0` |

Expected early behavior: first tick sell has no input so `intern` assigns only to `enchant` (assignment effort `1.0`); progress gains `10`, one mutation runs (`(0,0,0)` → `(10,1,1)`), treasury pays `cost` → money `80`, copies fan out to `enchant` and `sell`. Later ticks split effort when both have inputs; sell completes when its progress and treasury allow. Signal values are floating-point; the console rounds them for display.

## Related docs

| Topic | Document |
|-------|----------|
| Simulation graph, tick pipeline, APIs | [Production simulation](../../../technical/features/gameplay/production-simulation.md) |
| High-level vision | [Game design](../../game-design.md) |
