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
| **Information** | Complex structure; **copied** on route, **combined** (`+`) on fan-in, **mutated** only inside node logic | **enchantment** (content-addressed block with discrete `volume` / `designs` unit sets plus floating-point `darkness` / `fallacy`) |

- Money signals carry a quantity of money owned at a port. Enchantment signals carry a **single** enchantment block (not a count of enchantments). Aggregate size for volume/designs is the count of units in each set; darkness and fallacy are non-negative scalars.
- Nodes consume input signals and produce output signals each production tick according to their rules.
- Routing and fan-out only **copy** emitted values; they never mutate information. Any input port may have multiple incoming edges. At commit, residual stock plus every routed copy combine with **`+`**: money **adds**; enchantment uses the merge rules below. Incompatible enchantment histories (any pair with no common ancestor) yield **no value** — the destination is empty, including any residual.
- Each enchantment modification creates a new **block** with a content hash of its parent hash plus **volume and designs** unit sets. Darkness and fallacy are stored on the block but **do not** participate in hashing. Game state keeps a map of hash → block for ancestry.

## Actors, stats, wages, and assignment

- An **actor** has a **capacity**, a dictionary of **stats** (e.g. `enchanting`, `testing`, `designing`, `sales`, `treasury`, `payroll`), and an optional **wage**. If wage is unset, the payroll node’s **`defaultWage`** applies.
- Preferred **assignments** list which nodes an actor may operate, each with a positive **weight** (relative ratio). Each tick, only nodes whose **prerequisites** are met become **effective** assignments. Actors are not assigned to nodes that fail prerequisites that tick.
- **Every** seed node type uses a progress system and requires an actor to apply work (payroll’s **timer** alone is the exception—see payroll below).
- Prerequisites:
  - `enchant` / `testing` / `design` / `sell`: an enchantment on the process input
  - `merge`: enchantments on both `primary` and `secondary` inputs
  - `treasury`: at least one pending money move
  - `payroll`: payday due (timer elapsed reaches `period`)
- When an actor has multiple effective assignments, capacity is split by relative weights: **assignment effort** = `capacity × weight / Σ(effective weights)` for that actor. Equal weights yield an even split. Efforts from several actors on one node **add**.
- Unassigned process nodes do nothing that tick (except payroll timer count-up).
- Progress gain on a node is `stat × assignmentEffort` for the relevant stat. If an actor lacks that stat, the default is **1**.

Do not use “man/manning” in product language; use **assignment** / **assigned**.

## Progress, work effort, and money

- Every seed node tracks runtime **progress** (carried across ticks) and cumulative **cycles** (completed applications since tick 0).
- Node config **`effort`** is the base work units required per application. Each node type may use a different effort.
- While progress covers the required work for an application, the node may apply one or more times in a tick; each application subtracts its required work from progress and increments that node’s cycle count by one.
- **`enchant`:** with assignment effort and an input enchantment, the node always forwards the enchantment—either **mutate** or **pass-through**. Required work per mutation is `effort + enchantment.darkness` (scalar darkness on the value being mutated; recomputed after each mutate in the same tick). Each mutation appends `volumeDelta` volume units by first taking the oldest design units not already in volume (design units remain in `designs`), then allocating new unit ids for any remainder. Each design-derived volume unit adds `designDarknessDelta` darkness; each newly allocated volume unit adds `darknessDelta`. Fallacy increases by `priorDarkness + fallacyConstant`. No money ports.
- **`design`:** enchantment in/out. With assignment effort and an input enchantment, always forwards the enchantment. Each completed application appends `designDelta` new design units and reduces darkness by `darknessReduction` (clamped at zero). Pass-through when under effort. When the design variation is present, all enchantment loopback routes through design so design is the sole input into enchant.
- **`testing`:** with assignment effort and an input enchantment, always forwards the enchantment. Each completed application subtracts `fallacyReduction ×` (number of actors effectively assigned to testing that tick) from fallacy (floored at zero). Pass-through when under effort.
- **`merge`:** optional dedicated node (in the catalog, not in seeded graphs). With assignment effort and both `primary` and `secondary` enchantment inputs, consumes both and emits the same `+` resolution used at every enchantment port when progress covers `effort`. Same hash → that block; if one is in the other’s parent chain → newer tip (fast-forward, no new block); any pair with no common ancestor → **no output**; otherwise n-way unit merge for volume/designs and delta-merge for darkness/fallacy into a new block (parent = lexicographically smaller tip hash). Under effort: residual both inputs.
- **`sell`:** when progress allows (`effort`), consume the enchantment and **emit** the sale payout on its money output. Otherwise leave the enchantment as residual and emit no money. Routed money toward treasury becomes a **pending inbound** move (not immediately added to the pile).
- **`treasury`:** holds agency money on its `money` input/output port (committed pile on the input; successful out-moves **emit** on the output for edge routing). Config `effort` (seed `1`) per **one** pending money move (in or out). Assigned actors gain progress while the pending queue is non-empty; each application dequeues one move and applies it to the committed pile (in adds; out debits or mass-quits if short).
- **`payroll`:** `money` **input** (receives routed wage payouts; does not hold stock across ticks—disbursed/consumed). Config `defaultWage`, `period`, and `effort` (seed `1`). **Timer** increments every tick with no actor (`elapsed < period` → add 1). When `elapsed >= period`, payday is **due**. Payday application (actor + progress ≥ effort) **enqueues** a pending money-out for the wage total when a funding edge exists from a treasury `money` port to this payroll `money` input, and resets progress and the timer to `0`; it does **not** debit treasury. Empty roster / wage total `0` resets progress and the timer without enqueueing. Shortfall is checked when treasury executes the out-move: if the pile cannot cover **all** wages, **no partial pay**—pile unchanged by that move, out-move dropped, **all actors quit**.

## Circular flows and ticks

- Production updates run in discrete **ticks**. For now, one player turn advances one production tick.
- Enchantment uses **buffered** cross-tick signals (outputs visible next tick). Sell payouts enqueue treasury inbound on commit; payroll payday enqueues outbound when its application completes (routing declared by `treasury.money` → `payroll.money`). Treasury applies only moves already pending at the start of the tick (no same-tick money chain); successful outs emit money along outgoing edges.
- Information ports hold a single combined value after commit. Multiple routed copies (and a residual) **`+` together**; conflict empties the port.

## Scenarios (initial configuration)

Play boots from a **named preset** or a **seeded random** scenario. Unset `SCENARIO_PRESET` generates a random scenario; unset `SCENARIO_SEED` picks an integer seed at boot. The console status header shows `scenario: {preset-or-random} seed {N}` so a session can be reproduced.

### Essential graph and optional variations

The **essential** graph is the economic spine:

- Nodes: `enchant`, `sell`, `treasury`, `payroll`
- Edges: `enchant.enchantment` → `enchant.enchantment` (self-loop); `enchant.enchantment` → `sell.enchantment`; `sell.money` → `treasury.money` (pending inbound); `treasury.money` → `payroll.money`

Seeded graphs add **at most one** optional node: **testing** or **design**, never both. The merge node type stays in the catalog but is not placed in seeded graphs.

When **testing** is included, the enchant self-loop stays; `enchant→sell` is replaced by `enchant` → `testing` → `sell`, and testing also fans back onto `enchant.enchantment` (combined with the self-loop via `+`). Money edges unchanged.

When **design** is included, replace the enchant self-loop with `enchant.enchantment` → `design.enchantment` → `enchant.enchantment` (design is the sole input into enchant); keep `enchant.enchantment` → `sell.enchantment` and money edges.

Node types (always in the catalog): `enchant` — mutate or pass-through; `design` — grow designs / reduce darkness on an enchantment; `testing` — reduce fallacy; `merge` — combine two enchantment inputs with the same `+` as port fan-in; `sell` — consume enchantment, emit payout; `treasury` — store money via pending moves; `payroll` — timer + payday enqueue + money input.

| Piece | Detail |
|-------|--------|
| Enchant formula | On each mutation: append `volumeDelta` volume units (prefer unused design units first; designs are never removed), add darkness per volume unit (`darknessDelta`, or `designDarknessDelta` for design-derived), and add `(priorDarkness + fallacyConstant)` fallacy (defaults `10` / `1` / `0.3` / `1`). Required work: `effort + darkness` |
| Design formula | On each application: append `designDelta` design units and subtract `darknessReduction` darkness (defaults `effort: 3`, `designDelta: 1`, `darknessReduction: 0.9`) |
| Testing formula | On each application: subtract `fallacyReduction × effectiveActorCount` from fallacy (defaults `effort: 10`, `fallacyReduction: 5`) |
| Merge / `+` formula | Same hash or ancestor/descendant → newer tip; any pair with no common ancestor → empty; else n-way set merge for volume/designs (omit ancestor units missing from any side; otherwise union) and scalar delta-merge for darkness/fallacy (`ancestor + Σ(branch − ancestor)`, clamped at zero). New block parent = lexicographically smaller incomparable-tip hash. Dedicated merge node effort `1` |
| Sell formula | payout = `max(payoutFloor, volumeCount - fallacy)` (default `payoutFloor=0`) |
| Work / payroll | Config `effort: 10` on enchant/testing/sell; design `effort: 3`; merge `effort: 1`; treasury `effort: 1`; payroll `defaultWage: 10`, `period: 5`, `effort: 1`; progress gain uses actor stats × assignment effort (`designing` / `merging` default `1`); wage total covers **all** roster actors |
| Starting stocks | Prime ports: `enchant` enchantment = genesis empty block; `treasury` money = `100`; other process ports empty; payroll timer = `0`; pending money moves empty; block map holds genesis; node progress `0`; node cycles `0` |
| Config | Node numerics in `config/node-types/{enchant,testing,design,merge,sell,treasury,payroll}.json`; actor definitions in `config/actors/*.json`; named presets and the actor pool in `config/scenarios/`; port layouts and graph construction stay in code |

### Preset `lab01`

Named JSON preset matching the original magic-agency start: `includeTesting: true`, `includeDesign: false`; roster `intern` (capacity `1.0`, stats `enchanting: 10`, `sales: 10`) and `boss` (capacity `1.0`, stats `sales: 10`, `payroll: 10`, `treasury: 10`); wages unset. Preferred assignments (weight `1` each): intern → enchant, testing; boss → payroll, sell, treasury. Presets that set both optional flags are invalid.

`MagicAgencySeed.CreateInitialState()` still loads `lab01` for tests and compatibility.

### Actor pool and random generation

Random generation does **not** use every file under `config/actors/`. Eligibility is the explicit id list in `config/scenarios/actor-pool.json` (intern, boss, plus additional pool members). Other actor JSON on disk may exist without being in the pool.

When generating: choose the optional node with equal probability among **none**, **testing**, and **design** (never both); pick **2–4** distinct pool actors without replacement; assign preferred nodes (weight `1`) so **every graph node has at least one assignment**. Overlap (multiple actors preferred on the same node) is allowed but sparse, and more likely when actors are plentiful relative to nodes. Same `SCENARIO_SEED` yields the same graph flags, roster, and assignments.

Expected early behavior on `lab01`: first tick only enchant is effective among enchantment nodes (testing/sell empty); the mutated copy routes to testing and back onto enchant via the self-loop. Capacity may also split to treasury/payroll only when their prerequisites hold. Enchant mutations cost more as darkness rises (each new non-design volume unit adds `darknessDelta`). Sell deposits wait for treasury progress before the pile grows. Every `period` ticks the timer hits due; payday needs payroll progress, then treasury progress to debit (or mass-quit). Console shows volume/design counts, fractional darkness/fallacy, and an abbreviated block hash.

## Related docs

| Topic | Document |
|-------|----------|
| Simulation graph, tick pipeline, APIs | [Production simulation](../../../technical/features/gameplay/production-simulation.md) |
| High-level vision | [Game design](../../game-design.md) |
