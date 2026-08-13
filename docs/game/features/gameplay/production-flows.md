# Production flows

## Summary

Gameplay centers on **production flows**: directed graphs of **nodes** that transform signals. Players optimize dysfunctional configurations. Flows may be **circular**—a node’s input can depend on another node’s output in a cycle.

The first domain is a **magic agency** that creates enchantments as a service.

## When to read this

- Designing or changing production, resources, nodes, actors, or assignment rules
- Adding a new production domain or seed scenario
- Explaining turn/tick pacing for production

## Nodes and signals

- Each **node** is a station in the graph: typed **inputs** and **outputs**, plus type-specific behavior. (Player-facing labels may differ; internal docs use **node**.)
- Values that move through the graph are **signals**. There are two kinds:

| Kind | Behavior | Example |
|------|----------|---------|
| **Resource** | Primitive scalar. **Money** is owned on the node that holds it; routing **adds** piles at the destination | **money** (quantity) |
| **Information** | Complex structure; **copied** on route and **mutated** only inside node logic | **enchantment** (single value with `volume`, `darkness`, `fallacy`) |

- Money signals carry a quantity of money owned at a port. Enchantment signals carry a **single** enchantment (not a count).
- Nodes consume input signals and produce output signals each production tick according to their rules.
- Routing and fan-out only **copy** emitted values; they never mutate information.

## Actors, stats, wages, and assignment

- An **actor** has a **capacity**, a dictionary of **stats** (e.g. `enchanting`, `sales`), and an optional **wage**. If wage is unset, the payroll node’s **`defaultWage`** applies.
- Preferred **assignments** list which nodes an actor may operate. Each tick, only nodes whose **prerequisites** are met become **effective** assignments (for `enchant` / `sell`: an enchantment on the process input). Actors are not assigned to nodes that fail prerequisites that tick. `treasury` and `payroll` are never assignment targets.
- When an actor has multiple effective assignments, capacity is split equally: **assignment effort** = `capacity / effective count` for that actor. Efforts from several actors on one node **add**.
- Unassigned process nodes do nothing that tick.
- Progress gain on a node is `stat × assignmentEffort` for the relevant stat (`enchanting` on enchant, `sales` on sell). If an actor lacks that stat, the default is **1**.

Do not use “man/manning” in product language; use **assignment** / **assigned**.

## Progress, work effort, and money

- Process nodes (`enchant`, `sell`) track runtime **progress** (carried across ticks).
- Node config **`effort`** is the work units required per application (mutation or sale).
- While `progress >= effort`, the node may apply one or more times in a tick; each application subtracts `effort` from progress.
- **`enchant`:** with assignment effort and an input enchantment, the node always forwards the enchantment to its output—either **mutate** (one or more applications) or **pass-through** (emit the same value unchanged). Progress may still rise on a pass-through tick. No money ports; no per-mutation money cost.
- **`sell`:** when progress allows, consume the enchantment and **emit** the sale payout on its money output (routed into treasury). Otherwise leave the enchantment as residual (no information pass-through) and emit no money.
- **`treasury`:** holds agency money on its `money` input port. Sell deposits add to that pile. No assignment, no progress.
- **`payroll`:** no ports. Runs every tick without assignment. Countdown timer (`period` ticks); on payday, pays every actor’s effective wage from treasury. If treasury cannot cover **all** wages, **no partial pay**—treasury is unchanged by payroll and **all actors quit** (removed from the roster; assignments cleared).

## Circular flows and ticks

- Production updates run in discrete **ticks**. For now, one player turn advances one production tick.
- Enchantment uses **buffered** cross-tick signals (outputs visible next tick). Money is owned per node; sell payouts add into treasury on commit. Payroll debits (or mass quit) run **after** that tick’s deposits.
- Information ports hold a single value. If a consumer still has stock after residuals, a routed copy to that port is **skipped** that commit (occupancy).

## Magic agency seed (initial configuration)

| Piece | Detail |
|-------|--------|
| Actors | One actor (`intern`), capacity `1.0`, stats `enchanting: 10`, `sales: 10`, no explicit wage (from `config/actors/intern.json`) |
| Nodes | `enchant` — mutate or pass-through enchantment; `sell` — consume enchantment, emit payout; `treasury` — store money; `payroll` — pay wages on a timer |
| Enchant formula | On each mutation: `volume + volumeDelta`, `darkness + darknessDelta`, `fallacy + darkness + fallacyConstant` (defaults `10` / `1` / `1`) |
| Sell formula | payout = `max(payoutFloor, volume - fallacy)` (default `payoutFloor=0`) |
| Graph | Enchantment **fans out**: copy `enchant` → `enchant` (feedback) and copy `enchant` → `sell`; money: `sell.money` → `treasury.money` (additive) |
| Assignment | Preferred: `intern` → `enchant` and `sell`; effective set drops nodes without an enchantment input |
| Work / payroll | Config `effort: 10` on enchant/sell; payroll `defaultWage: 10`, `period: 5`; progress gain uses actor stats × assignment effort |
| Config | Node numerics in `config/node-types/{enchant,sell,payroll}.json`; actors in `config/actors/*.json`; port layouts and seed wiring stay in code |
| Starting stocks | Seed primes ports: `enchant` enchantment = `(volume:0, darkness:0, fallacy:0)`; `treasury` money = `100`; sell empty; payroll timer = `period`; node progress `0` |

Expected early behavior: first tick sell has no enchantment so `intern` assigns only to `enchant` (assignment effort `1.0`); progress gains `10`, one mutation runs (`(0,0,0)` → `(10,1,1)`); treasury stays `100`; payroll timer decrements. Later ticks split effort when both have enchantment inputs; sell completes when its progress allows, adding the payout to treasury. Every `period` ticks, payroll pays wages or all actors quit if treasury is short. Signal values are floating-point; the console rounds them for display.

## Related docs

| Topic | Document |
|-------|----------|
| Simulation graph, tick pipeline, APIs | [Production simulation](../../../technical/features/gameplay/production-simulation.md) |
| High-level vision | [Game design](../../game-design.md) |
