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
| **Resource** | Primitive scalar. **Money** is owned on the node that holds it; routing toward treasury **enqueues** pending moves (applied by treasury progress) | **money** (quantity) |
| **Information** | Complex structure; **copied** on route and **mutated** only inside node logic | **enchantment** (single value with `volume`, `darkness`, `fallacy`) |

- Money signals carry a quantity of money owned at a port. Enchantment signals carry a **single** enchantment (not a count).
- Nodes consume input signals and produce output signals each production tick according to their rules.
- Routing and fan-out only **copy** emitted values; they never mutate information.

## Actors, stats, wages, and assignment

- An **actor** has a **capacity**, a dictionary of **stats** (e.g. `enchanting`, `testing`, `sales`, `treasury`, `payroll`), and an optional **wage**. If wage is unset, the payroll node’s **`defaultWage`** applies.
- Preferred **assignments** list which nodes an actor may operate. Each tick, only nodes whose **prerequisites** are met become **effective** assignments. Actors are not assigned to nodes that fail prerequisites that tick.
- **Every** seed node type uses a progress system and requires an actor to apply work (payroll’s **timer** alone is the exception—see payroll below).
- Prerequisites:
  - `enchant` / `testing` / `sell`: an enchantment on the process input
  - `treasury`: at least one pending money move
  - `payroll`: payday due (timer remaining is `0`)
- When an actor has multiple effective assignments, capacity is split equally: **assignment effort** = `capacity / effective count` for that actor. Efforts from several actors on one node **add**.
- Unassigned process nodes do nothing that tick (except payroll timer countdown).
- Progress gain on a node is `stat × assignmentEffort` for the relevant stat. If an actor lacks that stat, the default is **1**.

Do not use “man/manning” in product language; use **assignment** / **assigned**.

## Progress, work effort, and money

- Every seed node tracks runtime **progress** (carried across ticks).
- Node config **`effort`** is the base work units required per application. Each node type may use a different effort.
- While progress covers the required work for an application, the node may apply one or more times in a tick; each application subtracts its required work from progress.
- **`enchant`:** with assignment effort and an input enchantment, the node always forwards the enchantment—either **mutate** or **pass-through**. Required work per mutation is `effort + enchantment.darkness` (darkness on the value being mutated; recomputed after each mutate in the same tick). No money ports.
- **`testing`:** with assignment effort and an input enchantment, always forwards the enchantment. Each completed application reduces fallacy by `fallacyReduction ×` (number of actors effectively assigned to testing that tick), floored at `0`. Pass-through when under effort.
- **`sell`:** when progress allows (`effort`), consume the enchantment and **emit** the sale payout on its money output. Otherwise leave the enchantment as residual and emit no money. Routed money toward treasury becomes a **pending inbound** move (not immediately added to the pile).
- **`treasury`:** holds agency money on its `money` input port. Config `effort` (seed `2`) per **one** pending money move (in or out). Assigned actors gain progress while the pending queue is non-empty; each application dequeues one move and applies it to the committed pile (in adds; out debits or mass-quits if short).
- **`payroll`:** no ports. Config `defaultWage`, `period`, and `effort` (seed `5`). **Timer** decrements every tick with no actor (`remaining > 0` → subtract 1). When `remaining == 0`, payday is **due**. Payday application (actor + progress ≥ effort) **enqueues** a pending money-out for the wage total and resets the timer to `period`; it does **not** debit treasury. Empty roster / wage total `0` resets the timer without enqueueing. Shortfall is checked when treasury executes the out-move: if the pile cannot cover **all** wages, **no partial pay**—pile unchanged by that move, out-move dropped, **all actors quit**.

## Circular flows and ticks

- Production updates run in discrete **ticks**. For now, one player turn advances one production tick.
- Enchantment uses **buffered** cross-tick signals (outputs visible next tick). Sell payouts enqueue treasury inbound on commit; payroll payday enqueues outbound when its application completes. Treasury applies only moves already pending at the start of the tick (no same-tick money chain).
- Information ports hold a single value. If a consumer still has stock after residuals, a routed copy to that port is **skipped** that commit (occupancy).

## Magic agency seed (initial configuration)

| Piece | Detail |
|-------|--------|
| Actors | One actor (`intern`), capacity `1.0`, stats `enchanting: 10`, `sales: 10`, no explicit wage (from `config/actors/intern.json`) |
| Nodes | `enchant` — mutate or pass-through; `testing` — reduce fallacy; `sell` — consume enchantment, emit payout; `treasury` — store money via pending moves; `payroll` — timer + payday enqueue |
| Enchant formula | On each mutation: `volume + volumeDelta`, `darkness + darknessDelta`, `fallacy + darkness + fallacyConstant` (defaults `10` / `1` / `1`). Required work: `effort + darkness` |
| Testing formula | On each application: `fallacy = max(0, fallacy - fallacyReduction × effectiveActorCount)` (defaults `effort: 10`, `fallacyReduction: 5`) |
| Sell formula | payout = `max(payoutFloor, volume - fallacy)` (default `payoutFloor=0`) |
| Graph | Enchantment **fans out**: copy `enchant` → `enchant` (feedback) and copy `enchant` → `testing`; copy `testing` → `sell`; money: `sell.money` → treasury pending inbound |
| Assignment | Preferred: `intern` → `enchant`, `testing`, `sell`, `treasury`, `payroll`; effective set filtered by prerequisites |
| Work / payroll | Config `effort: 10` on enchant/testing/sell; treasury `effort: 2`; payroll `defaultWage: 10`, `period: 5`, `effort: 5`; progress gain uses actor stats × assignment effort |
| Config | Node numerics in `config/node-types/{enchant,testing,sell,treasury,payroll}.json`; actors in `config/actors/*.json`; port layouts and seed wiring stay in code |
| Starting stocks | Seed primes ports: `enchant` enchantment = `(volume:0, darkness:0, fallacy:0)`; `treasury` money = `100`; sell/testing empty; payroll timer = `period`; pending money moves empty; node progress `0` |

Expected early behavior: first tick only enchant is effective among enchantment nodes (testing/sell empty); capacity may also split to treasury/payroll only when their prerequisites hold. Enchant mutations cost more as darkness rises. Sell deposits wait for treasury progress before the pile grows. Every `period` ticks the timer hits due; payday needs payroll progress, then treasury progress to debit (or mass-quit). Signal values are floating-point; the console rounds them for display.

## Related docs

| Topic | Document |
|-------|----------|
| Simulation graph, tick pipeline, APIs | [Production simulation](../../../technical/features/gameplay/production-simulation.md) |
| High-level vision | [Game design](../../game-design.md) |
