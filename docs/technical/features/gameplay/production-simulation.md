# Production simulation

## Summary

Authoritative production state lives in **Simulation** as an Imp-inspired node graph plus actors, assignments, buffered port signals, per-node progress, per-node timers, and a FIFO **pending money-move** queue for treasury. Updates are **pure** functions composed of discrete batched transforms; the host stores the resulting `GameState` in a mutable variable.

## When to read this

- Changing graph/signal types, tick phases, assignment effort, progress, payroll/treasury, testing, merge, port-level `+`, scenario presets, or seed factories
- Implementing or testing `AdvanceTick` / `GameState`
- Comparing Marloth’s graph model to Imp

## Imp inspiration (no Imp dependency)

Shapes follow Imp’s catalog vs instance split (ports on `NodeType`; instances hold type id + local literals; edges are port-to-port). Differences from core Imp:

| Imp | Marloth production |
|-----|--------------------|
| Data-only transmission; no evaluator | Simulation **evaluates** nodes each tick |
| DAG-oriented | **Cycles allowed** via buffered cross-tick signals |
| No actors | **Actors** and **assignments** drive assignment effort |
| Primitive literals | Strongly typed **resource** and **information** signal values |

There is no package/NuGet link to Imp; this is a C# model inspired by Imp’s graph docs.

## Numeric policy

Money amounts (`Money.Amount`), sell payout floor, node **effort** values, actor **stats** / **wage**, and per-node **progress** are floating-point (`double`).

Enchantment **volume / darkness / fallacy** are discrete unit counts (`int`). Config deltas (`volumeDelta`, `darknessDelta`, `fallacyConstant`, `fallacyReduction`) load as `double` and are rounded to non-negative integers (`AwayFromZero`) when allocating or removing units.

Actor **capacity** and per-node **assignment effort** remain `decimal` ratios. Progress gain converts with `(double)(stat × assignmentEffort)`.

Payroll **period** and elapsed **timers** are integers (`int`).

Console display shows aggregate unit counts and an abbreviated content hash; simulation keeps exact unit sets and full hashes.

## Core types

Identifiers are strings (or thin string wrappers): `NodeId`, `EdgeId`, `NodeTypeId`, `PortId`, `SignalTypeId`, `ActorId`.

| Type | Role |
|------|------|
| `Port` / `NodeType` | Catalog: input/output ports + signal types |
| `Node` / `Edge` / `PortReference` / `NodeGraph` | Instance wiring |
| `SignalValue` | Typed payloads: resource `Money(double)` or information `Enchantment(EnchantmentBlock)` |
| `EnchantmentBlock` | Content-addressed block: `Hash`, `ParentHash`, ordered unique unit-id arrays for volume/darkness/fallacy |
| `Actor` | `Id`, `Capacity` (`decimal`), `Stats` (`string` → `double`), optional `Wage` (`double?`) |
| `Assignment` | Preferred `ActorId` → `NodeId` with positive relative `Weight` (`decimal`, default `1`) (many nodes per actor) |
| `PendingMoneyMove` | FIFO treasury queue entry: direction `In` / `Out` + `Amount` |
| `NodeTypeConfigs` | Per-type behavior numerics loaded from JSON and attached to state |
| `GameState` | Graph + catalog + port signals + actors + preferred assignments + node configs + **node progress** + **node timers** + **pending money moves** + **enchantment block map** + **next unit id** + `Tick` |

Port signals are keyed by `(NodeId, PortId)`. Node progress and node timers are keyed by `NodeId`.

### Signal kinds

| Kind | Payload | Route / merge |
|------|---------|---------------|
| **Resource (money)** | Scalar quantity (`Money`) | Owned on the holding node. Money routed onto treasury’s money port **enqueues** a pending inbound move (does not `AddResource` into the committed pile). Other money edges **`+`** via `AddResource`. Payroll payday enqueues outbound; treasury applications apply one pending move per effort |
| **Information** | Single structure (`Enchantment` block) | **Copy** along each outgoing edge. At the destination, residual plus every routed copy **`+`** via `EnchantmentOps.TryCombine`. Mutate only inside node logic |

Residuals are applied first, then all routed copies into a destination combine in one n-ary `+`. Incompatible enchantment histories (any pair with no common ancestor) yield no value: the destination is **empty**, including any residual. Combine is order-independent.

## Assignment effort and prerequisites

Preferred assignments live on `GameState.Assignments`. Each tick, **effective** assignments are preferred rows whose target node meets prerequisites:

| Node type | Prerequisite |
|-----------|--------------|
| `enchant` / `testing` / `sell` | Enchantment on process input port |
| `merge` | Enchantments on both `primary` and `secondary` input ports |
| `treasury` | `PendingMoneyMoves` non-empty |
| `payroll` | Timer elapsed `>= period` (payday due) |

For each actor, over that actor’s effective assignments: `assignmentEffortPerNode = Capacity × weight / Σ(effective weights)`. Equal weights yield an even split (`Capacity / count`). Weights must be `> 0`; a non-positive weight or zero effective weight sum is fail-fast.  
Per node: assignment effort = sum of contributions. Unassigned / not effective → `0`.

Progress gain on a node = sum over effectively assigned actors of `GetStat(actor, key, default) × share`, with defaults `enchanting` / `testing` / `sales` / `merging` / `treasury` / `payroll` → `1`.

## Progress and config effort

Each seed node carries runtime `progress` (`double`, default `0`).

Node configs include:

- **`effort`** (enchant / testing / merge / sell / treasury / payroll) — base work units per application
- enchant also has `volumeDelta`, `darknessDelta`, `fallacyConstant`
- testing also has `fallacyReduction`
- sell also has `payoutFloor`
- payroll also has `defaultWage` / `period`

**Enchant** required work per mutation = `config.effort + current.darknessCount` (recomputed after each mutate). Other nodes use `config.effort` per application. While progress covers the required amount, applications run and subtract that amount.

## Node behaviors

Tunable numerics live in `config/node-types/{enchant,testing,merge,sell,treasury,payroll}.json` (heterogeneous schemas). Actor definitions load from `config/actors/*.json`. Scenario presets and the random actor pool live in `config/scenarios/`. Port layouts stay in code.

### `enchant`

- Input / output: `enchantment` only.
- Stat: `enchanting` (default `1`).
- With assignment effort `> 0` and an input enchantment: add progress; run mutate applications from progress only; **consume** input and **emit** either the mutated result or a **pass-through** copy of the input. Fan-out copies the emitted enchantment only.
- Mutation: append `volumeDelta` / `darknessDelta` / `(darknessCount + fallacyConstant)` new unit ids; parent hash = prior block; register in `EnchantmentBlocks`.
- Required work per mutation: `effort + darknessCount` on the enchantment being mutated.

### `testing`

- Input / output: `enchantment` only.
- Stat: `testing` (default `1`).
- With assignment effort `> 0` and an input enchantment: add progress; while `progress >= effort`, remove fallacy units and subtract `effort`; **consume** and **emit** (reduced or pass-through).
- Each application removes `fallacyReduction × effectiveActorCount` units from fallacy by ascending id (seed defaults `effort: 10`, `fallacyReduction: 5`). New block parent = input hash when units are removed.

### `merge`

- Inputs: `primary`, `secondary` (enchantment); output: `enchantment`.
- Config: `effort` (seed `1`).
- Stat: `merging` (default `1`).
- Prerequisite: both inputs present. With assignment effort and progress ≥ effort: consume both; emit `TryCombine` result (or nothing if incompatible).
- Resolution (same as port-level `+`): same hash → that block; ancestor/descendant → newer tip; any pair with no common ancestor → no value; else n-way merge per property (omit ancestor units missing from any side; otherwise union), new block parent = lexicographically smaller incomparable-tip hash.

### `sell`

- Input: `enchantment`; output: `money`.
- Stat: `sales` (default `1`).
- Add progress from assignment; when `progress >= effort`, consume enchantment and **emit** `max(payoutFloor, volumeCount - fallacyCount)` on the money output. Otherwise leave enchantment residual and emit no money.
- Edge `sell.money` → `treasury.money` enqueues pending inbound (does not immediately grow the committed pile).

### `treasury`

- Input / output: `money` (committed stock on the input; never consumed as process input; successful **Out** applications **emit** on the output for edge routing).
- Config: `effort` (seed `1`).
- Stat: `treasury` (default `1`).
- Always residuals the committed money pile.
- Effective when pending queue non-empty. Gain progress when assigned; each application dequeues **one** move from the start-of-tick queue and subtracts `effort`. **In** adds to the pile. **Out** debits if pile ≥ amount and emits that amount on the money output (routed by edges, e.g. to payroll); otherwise mass-quit (clear actors and assignments), drop the out-move, leave pile unchanged, emit nothing.
- Does not process moves enqueued later in the same tick (no same-tick money chain).

### `payroll`

- Input: `money` (receives routed wage payouts; consumed/disbursed — not residualled across ticks).
- Config: `defaultWage`, `period`, `effort` (seed `10` / `5` / `1`).
- Stat: `payroll` (default `1`).
- `GameState.NodeTimers[payroll]` is an elapsed count seeded to `0`.
- **Timer (no actor):** when start-of-tick `elapsed < period`, end-of-tick sets `elapsed + 1`. When start-of-tick `elapsed >= period`, payday is due (timer unchanged unless payday application resets it to `0`).
- **Payday due (start of tick `elapsed >= period`):** effective for assignment. Gain progress when assigned; when `progress >= effort`, if wage total `> 0` require at least one funding edge from a treasury `money` port to this node’s `money` input, then enqueue pending outbound for the wage total of all current actors; reset progress to `0`; reset timer to `0`. Missing funding edge with wage total `> 0` is fail-fast. Wage total `0` → reset progress and timer without enqueue.
- Effective wage per actor = actor `Wage` if set, else `defaultWage`. Shortfall / mass-quit runs when treasury applies the out-move.
- v1: exactly one `payroll` and one `treasury` node in the seed graph; missing/duplicate is fail-fast.

## Tick pipeline

Public API (pure):

```csharp
GameState AdvanceTick(GameState state);
ProductionTickResult AdvanceTickWithReport(GameState state);
// AdvanceTick(state) => AdvanceTickWithReport(state).State;
```

`ProductionTickResult` carries the next `GameState` plus `ImmutableArray<NodeIoRow> Nodes` (one row per process-reporting node as implemented, same order as tick iteration). Each `NodeIoRow` reports the **primary** process ports (enchantment in/out for `enchant` / `testing`; primary in / enchantment out for `merge`; enchantment in / money out for `sell`) with typed `SignalValue` available / residual / produced fields and whether the primary input was consumed.

Pipeline (each step returns new data; no mutation of prior state):

1. **`ResolveInputs`** — For each node input port, take the value already committed on that port.
2. **`ResolveEffectiveAssignments` / assignment effort** — Filter preferred assignments by prerequisites; split each actor’s capacity by relative weights over effective rows.
3. **`ComputeOutputs`** — Node-type-specific behavior using each node’s port inputs, assignment effort, stats, progress, and start-of-tick pending moves. Nodes are independent (no same-tick money chain). Node iteration order must not change results. May enqueue payroll outbound onto a next-pending builder (when a treasury→payroll money funding edge exists); treasury only drains the start-of-tick queue into residuals / actor updates and may emit money on successful outs.
4. **`CommitSignals`** — Residuals; route outputs (group by destination; residual + routed copies **`+`** — money `AddResource`, enchantment `TryCombine`; incompatible enchantment histories omit the dest key; money to treasury **enqueues inbound** on the pending builder, including treasury→payroll wage delivery). Register any new combined enchantment block.
5. **`AdvancePayrollTimer`** — If payroll elapsed `< period`, increment by 1 (no auto-pay).
6. **`NextState`** — New signals, updated progress/timers/pending maps, actors/assignments, `Tick + 1`.

Host pattern:

```csharp
GameState state = ScenarioBootstrap.CreateInitialState(config);
state = AdvanceTick(state); // mutable binding, immutable values
// or: var result = AdvanceTickWithReport(state); state = result.State;
```

`MagicAgencySeed.CreateInitialState()` remains as a compatibility factory that loads preset `lab01`.

## Scenarios

Play bootstrap: `ScenarioBootstrap.CreateInitialState(GameConfig)` (loads node configs, actor definitions, and scenario JSON from `config/` under the app base directory; overloads accept explicit configs/actors/pool). `GameConfig.ScenarioPreset` selects a named file `config/scenarios/{name}.json`; null/whitespace generates a random scenario from `SCENARIO_SEED`. Unknown presets, invalid JSON, missing actors, or assignments to absent nodes are fail-fast (`InvalidOperationException` with path context).

- Node types: `enchant` (in/out `enchantment`), `testing` (in/out `enchantment`), `merge` (in `primary`/`secondary`, out `enchantment`; catalog only — not in seeded graphs), `sell` (in `enchantment`, out `money`), `treasury` (in/out `money`), `payroll` (in `money`). Catalog always includes all six types.
- **Essential graph:** nodes `enchant`, `sell`, `treasury`, `payroll`; edges `enchant.enchantment` → `enchant.enchantment` (self-loop); `enchant.enchantment` → `sell.enchantment`; `sell.money` → `treasury.money` (pending inbound on commit); `treasury.money` → `payroll.money`.
- **Testing variation:** add node `testing`; keep the enchant self-loop; replace `enchant→sell` with `enchant.enchantment` → `testing.enchantment`; `testing.enchantment` → `sell.enchantment`; `testing.enchantment` → `enchant.enchantment` (fan-in `+` with the self-loop); money edges unchanged.
- **Preset `lab01`:** `includeTesting: true`; actors `intern` and `boss` (stats as configured, wages unset). Preferred assignments (weight `1` each): intern → enchant, testing; boss → payroll, sell, treasury.
- **Actor pool:** `config/scenarios/actor-pool.json` lists eligible actor ids (not every file under `config/actors/`). Random generation: coin-flip testing; 2–4 distinct pool actors; preferred assignments (weight `1`) cover **every** graph node; multi-actor overlap on a node is allowed but sparse (more likely when actors are plentiful relative to nodes). Deterministic for a fixed seed.
- Initial signals: `enchant.enchantment` = genesis empty block; `treasury.money` = `100`; `EnchantmentBlocks` contains genesis; `NextUnitId` = `1`; progress empty/`0`; payroll timer = `0`; pending money moves empty; `Tick = 0`.

## Layout

Under `src/MarlothStrategy.Simulation/`:

- `Graph/` — structural Imp-like types
- `Production/` — signals, catalog, `GameState`, scenario bootstrap/generation, seed compatibility, `AdvanceTick`, config DTOs/loaders, pending money moves
- `config/node-types/` — JSON behavior numerics per node type (copied to output)
- `config/actors/` — JSON actor definitions (copied to output)
- `config/scenarios/` — named presets (`lab01.json`) and `actor-pool.json` (copied to output)

## Error handling

Seed and tick assume a well-formed graph for v1 (programmer invariants). Malformed catalogs or missing node types are exceptional (`InvalidOperationException`). Missing or invalid node-type, actor, preset, or actor-pool JSON at seed/boot is exceptional (fail-fast with path context). Expected empty stocks, incompatible-enchantment empty ports, payday mass-quit, and empty pending queues are normal.

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing production rules | [Production flows](../../../game/features/gameplay/production-flows.md) |
| Console session / I/O table | [Console client](../ui/console-client.md) |
| Error handling policy | [Error handling](../platform/error-handling.md) |
| Tests | [Testing](../platform/testing.md) |
