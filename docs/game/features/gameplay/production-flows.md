# Production flows

## Summary

Gameplay centers on **production flows**: directed graphs of **nodes** that transform signals. Players optimize dysfunctional configurations. Flows may be **circular**—a node’s input can depend on another node’s output in a cycle.

The first domain is a **magic agency** that creates enchantments as a service.

## When to read this

- Designing or changing production, resources, nodes, actors, or assignment rules
- Adding a new production domain, scenario preset, or random scenario rules
- Explaining turn/tick pacing for production

## Nodes and signals

- Each **node** is a station in the graph: typed **inputs** and **outputs**, plus type-specific behavior. (Player-facing labels may differ; internal docs use **node**.)
- Values that move through the graph are **signals**. There are two kinds:

| Kind | Behavior | Example |
|------|----------|---------|
| **Resource** | Primitive scalar. **Money** is owned on the node that holds it; routing toward treasury **enqueues** pending moves (applied by treasury progress) | **money** (quantity) |
| **Information** | Complex structure; **copied** on route and **mutated** only inside node logic | **enchantment** (content-addressed block with discrete `volume` / `darkness` / `fallacy` unit sets) |

- Money signals carry a quantity of money owned at a port. Enchantment signals carry a **single** enchantment block (not a count of enchantments). Aggregate numeric size is the count of units in each property.
- Nodes consume input signals and produce output signals each production tick according to their rules.
- Routing and fan-out only **copy** emitted values; they never mutate information.
- Each enchantment modification creates a new **block** with a content hash of its parent hash plus unit sets. Game state keeps a map of hash → block for ancestry.

## Actors, stats, wages, and assignment

- An **actor** has a **capacity**, a dictionary of **stats** (e.g. `enchanting`, `testing`, `sales`, `treasury`, `payroll`), and an optional **wage**. If wage is unset, the payroll node’s **`defaultWage`** applies.
- Preferred **assignments** list which nodes an actor may operate, each with a positive **weight** (relative ratio). Each tick, only nodes whose **prerequisites** are met become **effective** assignments. Actors are not assigned to nodes that fail prerequisites that tick.
- **Every** seed node type uses a progress system and requires an actor to apply work (payroll’s **timer** alone is the exception—see payroll below).
- Prerequisites:
  - `enchant` / `testing` / `sell`: an enchantment on the process input
  - `merge`: enchantments on both `primary` and `secondary` inputs
  - `treasury`: at least one pending money move
  - `payroll`: payday due (timer remaining is `0`)
- When an actor has multiple effective assignments, capacity is split by relative weights: **assignment effort** = `capacity × weight / Σ(effective weights)` for that actor. Equal weights yield an even split. Efforts from several actors on one node **add**.
- Unassigned process nodes do nothing that tick (except payroll timer countdown).
- Progress gain on a node is `stat × assignmentEffort` for the relevant stat. If an actor lacks that stat, the default is **1**.

Do not use “man/manning” in product language; use **assignment** / **assigned**.

## Progress, work effort, and money

- Every seed node tracks runtime **progress** (carried across ticks).
- Node config **`effort`** is the base work units required per application. Each node type may use a different effort.
- While progress covers the required work for an application, the node may apply one or more times in a tick; each application subtracts its required work from progress.
- **`enchant`:** with assignment effort and an input enchantment, the node always forwards the enchantment—either **mutate** or **pass-through**. Required work per mutation is `effort + enchantment.darkness` (darkness **unit count** on the value being mutated; recomputed after each mutate in the same tick). No money ports.
- **`testing`:** with assignment effort and an input enchantment, always forwards the enchantment. Each completed application removes `fallacyReduction ×` (number of actors effectively assigned to testing that tick) **discrete fallacy units** (lowest ids first), floored at empty. Pass-through when under effort.
- **`merge`:** with assignment effort and both `primary` and `secondary` enchantment inputs, consumes both and emits one resolved block when progress covers `effort`. Same hash → that block; if one is in the other’s parent chain → newer tip (fast-forward, no new block); no common ancestor → **primary**; otherwise three-way unit merge into a new block (parent = primary). Under effort: residual both inputs.
- **`sell`:** when progress allows (`effort`), consume the enchantment and **emit** the sale payout on its money output. Otherwise leave the enchantment as residual and emit no money. Routed money toward treasury becomes a **pending inbound** move (not immediately added to the pile).
- **`treasury`:** holds agency money on its `money` input/output port (committed pile on the input; successful out-moves **emit** on the output for edge routing). Config `effort` (seed `2`) per **one** pending money move (in or out). Assigned actors gain progress while the pending queue is non-empty; each application dequeues one move and applies it to the committed pile (in adds; out debits or mass-quits if short).
- **`payroll`:** `money` **input** (receives routed wage payouts; does not hold stock across ticks—disbursed/consumed). Config `defaultWage`, `period`, and `effort` (seed `5`). **Timer** decrements every tick with no actor (`remaining > 0` → subtract 1). When `remaining == 0`, payday is **due**. Payday application (actor + progress ≥ effort) **enqueues** a pending money-out for the wage total when a funding edge exists from a treasury `money` port to this payroll `money` input, and resets the timer to `period`; it does **not** debit treasury. Empty roster / wage total `0` resets the timer without enqueueing. Shortfall is checked when treasury executes the out-move: if the pile cannot cover **all** wages, **no partial pay**—pile unchanged by that move, out-move dropped, **all actors quit**.

## Circular flows and ticks

- Production updates run in discrete **ticks**. For now, one player turn advances one production tick.
- Enchantment uses **buffered** cross-tick signals (outputs visible next tick). Sell payouts enqueue treasury inbound on commit; payroll payday enqueues outbound when its application completes (routing declared by `treasury.money` → `payroll.money`). Treasury applies only moves already pending at the start of the tick (no same-tick money chain); successful outs emit money along outgoing edges.
- Information ports hold a single value. If a consumer still has stock after residuals, a routed copy to that port is **skipped** that commit (occupancy).

## Scenarios (initial configuration)

Play boots from a **named preset** or a **seeded random** scenario. Unset `SCENARIO_PRESET` generates a random scenario; unset `SCENARIO_SEED` picks an integer seed at boot. The console status header shows `scenario: {preset-or-random} seed {N}` so a session can be reproduced.

### Essential graph and testing+merge variation

The **essential** graph is the economic spine:

- Nodes: `enchant`, `sell`, `treasury`, `payroll`
- Edges: `enchant.enchantment` → `sell.enchantment`; `sell.money` → `treasury.money` (pending inbound); `treasury.money` → `payroll.money`

The only graph variation is whether **testing + merge** are included as a unit. When included, the direct `enchant→sell` edge is replaced by today’s fan-out/loop: `enchant` → `testing` and `enchant` → `merge.primary`; `testing` → `sell` and `testing` → `merge.secondary`; `merge` → `enchant`; money edges unchanged.

Node types (always in the catalog): `enchant` — mutate or pass-through; `testing` — remove fallacy units; `merge` — combine primary/secondary branches; `sell` — consume enchantment, emit payout; `treasury` — store money via pending moves; `payroll` — timer + payday enqueue + money input.

| Piece | Detail |
|-------|--------|
| Enchant formula | On each mutation: append `volumeDelta` volume units, `darknessDelta` darkness units, and `(darknessCount + fallacyConstant)` fallacy units (defaults `10` / `1` / `1`). Required work: `effort + darknessCount` |
| Testing formula | On each application: remove `fallacyReduction × effectiveActorCount` fallacy units by ascending id (defaults `effort: 10`, `fallacyReduction: 5`) |
| Merge formula | Effort `5` (lower than enchant). Fast-forward / primary-on-divergence / else three-way set merge per property: omit ancestor units missing from either side; otherwise union |
| Sell formula | payout = `max(payoutFloor, volumeCount - fallacyCount)` (default `payoutFloor=0`) |
| Work / payroll | Config `effort: 10` on enchant/testing/sell; merge `effort: 5`; treasury `effort: 2`; payroll `defaultWage: 10`, `period: 5`, `effort: 5`; progress gain uses actor stats × assignment effort (`merging` default `1`); wage total covers **all** roster actors |
| Starting stocks | Prime ports: `enchant` enchantment = genesis empty block; `treasury` money = `100`; other process ports empty; payroll timer = `period`; pending money moves empty; block map holds genesis; node progress `0` |
| Config | Node numerics in `config/node-types/{enchant,testing,merge,sell,treasury,payroll}.json`; actor definitions in `config/actors/*.json`; named presets and the actor pool in `config/scenarios/`; port layouts and graph construction stay in code |

### Preset `lab01`

Named JSON preset matching the original magic-agency start: `includeTestingMerge: true`; roster `intern` (capacity `1.0`, stats `enchanting: 10`, `sales: 10`) and `boss` (capacity `1.0`, stats `sales: 10`, `payroll: 10`, `treasury: 10`); wages unset. Preferred assignments (weight `1` each): intern → enchant, merge, testing; boss → payroll, sell, treasury.

`MagicAgencySeed.CreateInitialState()` still loads `lab01` for tests and compatibility.

### Actor pool and random generation

Random generation does **not** use every file under `config/actors/`. Eligibility is the explicit id list in `config/scenarios/actor-pool.json` (intern, boss, plus additional pool members). Other actor JSON on disk may exist without being in the pool.

When generating: coin-flip testing+merge; pick **2–4** distinct pool actors without replacement; assign preferred nodes (weight `1`) so **every graph node has at least one assignment**. Overlap (multiple actors preferred on the same node) is allowed but sparse, and more likely when actors are plentiful relative to nodes. Same `SCENARIO_SEED` yields the same graph flag, roster, and assignments.

Expected early behavior on `lab01`: first tick only enchant is effective among enchantment nodes (testing/sell/merge empty); capacity may also split to treasury/payroll only when their prerequisites hold. Enchant mutations cost more as darkness rises. Sell deposits wait for treasury progress before the pile grows. Every `period` ticks the timer hits due; payday needs payroll progress, then treasury progress to debit (or mass-quit). Console shows aggregate unit counts and an abbreviated block hash.

## Related docs

| Topic | Document |
|-------|----------|
| Simulation graph, tick pipeline, APIs | [Production simulation](../../../technical/features/gameplay/production-simulation.md) |
| High-level vision | [Game design](../../game-design.md) |
