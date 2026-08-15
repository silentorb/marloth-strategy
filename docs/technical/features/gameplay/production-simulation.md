# Production simulation

## Summary

Authoritative production state lives in **Simulation** as an Imp-inspired node graph plus actors, assignments, buffered port signals, per-node progress, per-node timers, per-node **cycles** (completed applications), and a FIFO **pending money-move** queue for treasury. Updates are **pure** functions composed of discrete batched transforms; the host stores the resulting `GameState` in a mutable variable.

## When to read this

- Changing graph/signal types, tick phases, assignment effort, progress, payroll/treasury, testing, design, merge, port-level `+`, scenario presets, **time partitions**, or seed factories
- Implementing or testing `AdvanceTick` / `AdvanceTicks` / `GameState`
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

Enchantment **volume / designs** are discrete unit counts (`int`). Enchantment **darkness / fallacy** are non-negative floating-point scalars (`double`) and **do not** participate in content hashing. Config `volumeDelta` / `designDelta` load as `double` and are rounded to non-negative integers (`AwayFromZero`) when allocating units. Darkness/fallacy config values (`darknessDelta`, `designDarknessDelta`, `fallacyConstant`, `darknessReduction`, `fallacyReduction`) are used as floating-point amounts without integer rounding.

Actor **capacity** and per-node **assignment effort** remain `decimal` ratios. Progress gain converts with `(double)(stat × assignmentEffort)`.

Payroll **period** and elapsed **timers** are integers (`int`).

Console display shows volume/design counts, trimmed fractional darkness/fallacy, and an abbreviated content hash; simulation keeps exact unit sets, scalar values, and full hashes.

## Core types

Identifiers are strings (or thin string wrappers): `NodeId`, `EdgeId`, `NodeTypeId`, `PortId`, `SignalTypeId`, `ActorId`.

| Type | Role |
|------|------|
| `Port` / `NodeType` | Catalog: input/output ports + signal types |
| `Node` / `Edge` / `PortReference` / `NodeGraph` | Instance wiring |
| `SignalValue` | Typed payloads: resource `Money(double)`, or information `Enchantment(EnchantmentBlock)` |
| `EnchantmentBlock` | Content-addressed block: `Hash`, `ParentHash`, ordered unique unit-id arrays for volume/designs, plus floating-point `Darkness` / `Fallacy` (excluded from hash) |
| `Actor` | `Id`, `Capacity` (`decimal`), `Stats` (`string` → `double`), optional `Wage` (`double?`) |
| `Assignment` | Preferred `ActorId` → `NodeId` with positive relative `Weight` (`decimal`, default `1`) (many nodes per actor) |
| `PendingMoneyMove` | FIFO treasury queue entry: direction `In` / `Out` + `Amount` |
| `NodeTypeConfigs` | Per-type behavior numerics loaded from JSON and attached to state |
| `GameState` | Graph + catalog + port signals + **port flow totals** + actors + preferred assignments + node configs + **node progress** + **node timers** + **node cycles** + **pending money moves** + **enchantment block map** + **next unit id** + `Tick` + **`TimePartitions`** (immutable nested calendar) |

Port signals and port flow totals are keyed by `(NodeId, PortId)`. Node progress, node timers, and node cycles are keyed by `NodeId`.

`PortFlowTotals` (`double`, default `0`) accumulates money that passes through a port without resting in committed stock, so throughput survives after the stock clears: `sell` adds the emitted payout (`+`) and `payroll` subtracts the wages it disburses off its money input (`-`). Treasury keeps money in committed stock and adds nothing, so its pile is never double-counted. The console Δ column reads stock plus this total.

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
| `enchant` / `testing` / `design` / `sell` | Enchantment on process input port |
| `merge` | Enchantments on both `primary` and `secondary` input ports |
| `treasury` | `PendingMoneyMoves` non-empty |
| `payroll` | Open payroll run with attempt not yet submitted |

For each actor, over that actor’s effective assignments: `assignmentEffortPerNode = Capacity × weight / Σ(effective weights)`. Equal weights yield an even split (`Capacity / count`). Weights must be `> 0`; a non-positive weight or zero effective weight sum is fail-fast.  
Per node: assignment effort = sum of contributions. Unassigned / not effective → `0`.

Progress gain on a node = sum over effectively assigned actors of `GetStat(actor, key, default) × share`, with defaults `enchanting` / `testing` / `designing` / `sales` / `merging` / `treasury` / `payroll` → `1`.

## Progress and config effort

Each seed node carries runtime `progress` (`double`, default `0`) and cumulative **cycles** (`int`, default `0`) — the count of completed applications since tick 0.

Node configs include:

- **`effort`** (enchant / testing / design / merge / sell / treasury) — base work units per application
- enchant also has `volumeDelta`, `darknessDelta`, `designDarknessDelta`, `fallacyConstant`
- design also has `designDelta`, `darknessReduction`
- testing also has `fallacyReduction`
- sell also has `payoutFloor`
- payroll has `schedule` (`periodUnit`, `positionUnit`, `startLead`, `dueDay`), `baseEffort`, `perActorEffort` (required work = `baseEffort + perActorEffort ×` snapshot obligation count)

**Enchant** required work per mutation = `config.effort + current.darkness` (recomputed after each mutate). Other nodes use `config.effort` per application. While progress covers the required amount, applications run and subtract that amount; each application increments that node’s `NodeCycles` by `1`.

## Node behaviors

Tunable numerics live in `config/node-types/{enchant,testing,design,merge,sell,treasury,payroll}.json` (heterogeneous schemas). Actor definitions load from `config/actors/*.json`. Scenario presets and the random actor pool live in `config/scenarios/`. Port layouts stay in code.

### `enchant`

- Input / output: `enchantment`.
- Stat: `enchanting` (default `1`).
- With assignment effort `> 0` and an input enchantment: add progress; run mutate applications from progress only; **consume** input and **emit** either the mutated result or a **pass-through** copy of the input. Fan-out copies the emitted enchantment only.
- Mutation: for each of `volumeDelta` volume units, prefer the oldest design unit not already in volume (designs are never removed); otherwise allocate a new unit id. Add `designDarknessDelta` darkness per design-derived volume unit and `darknessDelta` per newly allocated volume unit; add `(priorDarkness + fallacyConstant)` fallacy; parent hash = prior block; register in `EnchantmentBlocks`.
- Required work per mutation: `effort + darkness` on the enchantment being mutated.
- Hash content: parent hash + volume units + designs units (darkness/fallacy excluded).

### `design`

- Input / output: `enchantment`.
- Config: `effort` (seed `3`), `designDelta` (seed `1`), `darknessReduction` (seed `0.9`).
- Stat: `designing` (default `1`).
- Prerequisite: enchantment on input. With assignment effort `> 0` and an input enchantment: add progress; while `progress >= effort`, append `designDelta` design units and subtract `darknessReduction` darkness (clamped at zero); **consume** and **emit** (grown or pass-through).

### `testing`

- Input / output: `enchantment` only.
- Stat: `testing` (default `1`).
- With assignment effort `> 0` and an input enchantment: add progress; while `progress >= effort`, subtract fallacy and subtract `effort`; **consume** and **emit** (reduced or pass-through).
- Each application subtracts `fallacyReduction × effectiveActorCount` from fallacy (seed defaults `effort: 10`, `fallacyReduction: 5`). New block parent = input hash when fallacy changes.

### `merge`

- Inputs: `primary`, `secondary` (enchantment); output: `enchantment`.
- Config: `effort` (seed `1`).
- Stat: `merging` (default `1`).
- Prerequisite: both inputs present. With assignment effort and progress ≥ effort: consume both; emit `TryCombine` result (or nothing if incompatible).
- Resolution (same as port-level `+`): same hash → that block; ancestor/descendant → newer tip; any pair with no common ancestor → no value; else n-way merge for volume/designs (omit ancestor units missing from any side; otherwise union) and scalar delta-merge for darkness/fallacy (`ancestor + Σ(branch − ancestor)`, clamped at zero); new block parent = lexicographically smaller incomparable-tip hash.

### `sell`

- Input: `enchantment`; output: `money`.
- Stat: `sales` (default `1`).
- Add progress from assignment; when `progress >= effort`, consume enchantment and **emit** `max(payoutFloor, volumeCount - fallacy)` on the money output. Otherwise leave enchantment residual and emit no money.
- Each emitted payout adds to `PortFlowTotals` on the money output (lifetime sale income).
- Edge `sell.money` → `treasury.money` enqueues pending inbound (does not immediately grow the committed pile).

### `treasury`

- Input / output: `money` (committed stock on the input; never consumed as process input; successful **Out** applications **emit** on the output for edge routing).
- Config: `effort` (seed `1`).
- Stat: `treasury` (default `1`).
- Always residuals the committed money pile.
- Effective when pending queue non-empty. Gain progress when assigned; each application dequeues **one** move from the start-of-tick queue and subtracts `effort`. **In** adds to the pile. **Out** (payroll-tagged): deterministically shuffle unpaid obligations for that run, pay each whole wage that fits the remaining pile, debit/emit that sum, mark those actors paid on the active run, and drop the remainder of the move; nobody leaves immediately. Ordinary amount-only outs (tests / legacy) debit when affordable, otherwise drop without debiting and without clearing the roster.
- Does not process moves enqueued later in the same tick (no same-tick money chain).

### `payroll`

- Input: `money` (receives routed wage payouts; consumed/disbursed — not residualled across ticks). Each disbursed amount subtracts from `PortFlowTotals` on that port (lifetime wages paid).
- Config: `schedule` (`periodUnit` / `positionUnit` / `startLead` / `dueDay`; seed `month` / `day` / `0` / `10`), `baseEffort`, `perActorEffort` (seed `1` / `1`). `dueDay` must leave enough ticks for payroll to finish **and** treasury to deliver on a later tick.
- Stat: `payroll` (default `1`).
- `GameState.ActivePayrollRun` is optional; seed starts with none.
- **Open:** at the start of each tick, when there is no active run and the calendar is on the configured start day within the period (seed: last day of `month` = `daysInPeriod - startLead`), open a run whose `PeriodIndex` is the absolute period index at that tick and whose obligations snapshot every currently waged actor (ordered by id) with that actor’s wage. Unwaged actors are excluded.
- **Effective:** while an active run exists and `AttemptSubmitted` is false. Gain progress when assigned; when `progress >=` required work (`baseEffort + perActorEffort ×` obligation count), if wage total `> 0` require at least one funding edge from a treasury `money` port to this node’s `money` input, then enqueue one pending outbound tagged with the run’s period index and per-actor obligations; reset progress to `0`; set `AttemptSubmitted`. Missing funding edge with wage total `> 0` is fail-fast. Wage total `0` → submit the attempt without enqueue.
- Only one attempt per run. Actors count as paid only when treasury emits their full wage.
- **Deadline:** after compute/commit on a tick, if the next tick is past the due day of the following period (seed: after day `10` of `PeriodIndex + 1`), remove still-unpaid obligation actors and only their assignments, cancel pending outs tagged for that run, and clear `ActivePayrollRun`.
- v1: exactly one `payroll` and one `treasury` node in the seed graph; missing/duplicate is fail-fast.

## Time partitions

Nested calendar labels over the monotonic `Tick` counter. Configured in `config/time-partitions.json` and attached to `GameState.TimePartitions` at bootstrap. **Tick remains the only mutable clock**; day/week/month indices are derived, not stored.

### Schema

```json
{
  "units": [
    { "name": "day", "contains": 1, "of": "tick" },
    { "name": "week", "contains": 7, "of": "day" },
    { "name": "month", "contains": 4, "of": "week" }
  ],
  "advanceUnit": "week"
}
```

Validation (fail-fast): positive `contains`; unique unit names; no unit named `tick`; exactly one connected acyclic chain rooted at `of: "tick"`; `advanceUnit` is a declared non-`tick` unit; tick-duration products must fit in `int`.

Seed defaults: 1 tick = 1 day; 7 days = 1 week; 4 weeks = 1 month; session macro advance uses **week** (7 ticks).

### Positions and rollover

`TimePartitionConfig.PositionsAt(tick)` returns one-based positions for each configured unit (smallest → largest). Nested units report `index/ofParent` (e.g. day `1/7`, week `1/4`); the largest unit is unbounded (`month 1`, `month 2`, …). At tick `0` every unit is at position `1`. Exact multiples roll into the next unit (tick `7` → day `1/7`, week `2/4`).

### Boundary and position queries

`BoundariesCrossed(fromTick, toTick)` returns configured unit names (smallest → largest) whose absolute index increases over `(fromTick, toTick]`. Empty when `fromTick == toTick`.

`AbsoluteIndex(unit, tick)` is `tick / TicksPer(unit)` (zero-based). `PositionWithin(childUnit, parentUnit, tick)` is the one-based index of the child inside the parent at that tick. Payroll’s schedule uses these helpers (not a raw tick countdown).

### Macro advance

```csharp
GameState AdvanceTicks(GameState state, int tickCount);
ProductionTickResult AdvanceTicksWithReport(GameState state, int tickCount);
```

Composes the ordinary tick pipeline exactly `tickCount` times (`tickCount > 0`). Session Space uses `state.TimePartitions.AdvanceTickCount` (duration of `advanceUnit`), advancing that many ticks **from the current tick** — not snapping to the next boundary.

## Tick pipeline

Public API (pure):

```csharp
GameState AdvanceTick(GameState state);
ProductionTickResult AdvanceTickWithReport(GameState state);
GameState AdvanceTicks(GameState state, int tickCount);
ProductionTickResult AdvanceTicksWithReport(GameState state, int tickCount);
// AdvanceTick(state) => AdvanceTickWithReport(state).State;
```

`ProductionTickResult` carries the next `GameState` plus `ImmutableArray<NodeIoRow> Nodes` (one row per process-reporting node as implemented, same order as tick iteration). Each `NodeIoRow` reports the **primary** process ports (enchantment in/out for `enchant` / `testing` / `design`; primary in / enchantment out for `merge`; enchantment in / money out for `sell`) with typed `SignalValue` available / residual / produced fields and whether the primary input was consumed. Multi-tick advance returns the **final** tick's report.

Pipeline (each step returns new data; no mutation of prior state):

1. **`OpenPayrollRunIfDue`** — If there is no active run and the calendar is on the configured start day, snapshot waged actors into `ActivePayrollRun`.
2. **`ResolveInputs`** — For each node input port, take the value already committed on that port.
3. **`ResolveEffectiveAssignments` / assignment effort** — Filter preferred assignments by prerequisites; split each actor’s capacity by relative weights over effective rows.
4. **`ComputeOutputs`** — Node-type-specific behavior using each node’s port inputs, assignment effort, stats, progress, active payroll run, and start-of-tick pending moves. Nodes are independent (no same-tick money chain). Node iteration order must not change results. May enqueue payroll outbound onto a next-pending builder (when a treasury→payroll money funding edge exists); treasury only drains the start-of-tick queue into residuals / payroll-run paid marks and may emit money on successful outs. Sale payouts and payroll disbursements also accumulate onto `PortFlowTotals`.
5. **`CommitSignals`** — Residuals; route outputs (group by destination; residual + routed copies **`+`** — money `AddResource`, enchantment `TryCombine`; incompatible enchantment histories omit the dest key; money to treasury **enqueues inbound** on the pending builder, including treasury→payroll wage delivery). Register any new combined enchantment block.
6. **`ClosePayrollIfPastDue`** — If the next tick is past the active run’s due day, remove unpaid obligation actors and their assignments, cancel stale payroll outs for that run, and clear the run.
7. **`NextState`** — New signals, updated progress/cycles/pending/flow-total maps, actors/assignments, active payroll run, `Tick + 1`.

Host pattern:

```csharp
GameState state = ScenarioBootstrap.CreateInitialState(config);
state = AdvanceTick(state); // mutable binding, immutable values
// or: var result = AdvanceTickWithReport(state); state = result.State;
```

`MagicAgencySeed.CreateInitialState()` remains as a compatibility factory that loads preset `lab01`.

## Scenarios

Play bootstrap: `ScenarioBootstrap.CreateInitialState(GameConfig)` (loads node configs, actor definitions, scenario JSON, and **time partitions** from `config/` under the app base directory; overloads accept explicit configs/actors/pool/time partitions). `GameConfig.ScenarioPreset` selects a named file `config/scenarios/{name}.json`; null/whitespace generates a random scenario from `SCENARIO_SEED`. Unknown presets, invalid JSON, missing actors, assignments to absent nodes, invalid payroll schedule units, or presets that include both testing and design are fail-fast (`InvalidOperationException` with path context).

- Node types: `enchant` (in/out `enchantment`), `design` (in/out `enchantment`), `testing` (in/out `enchantment`), `merge` (in `primary`/`secondary`, out `enchantment`; catalog only — not in seeded graphs), `sell` (in `enchantment`, out `money`), `treasury` (in/out `money`), `payroll` (in `money`). Catalog always includes all seven types.
- **Essential graph:** nodes `enchant`, `sell`, `treasury`, `payroll`; edges `enchant.enchantment` → `enchant.enchantment` (self-loop); `enchant.enchantment` → `sell.enchantment`; `sell.money` → `treasury.money` (pending inbound on commit); `treasury.money` → `payroll.money`.
- **Testing variation:** add node `testing`; keep the enchant self-loop; replace `enchant→sell` with `enchant.enchantment` → `testing.enchantment`; `testing.enchantment` → `sell.enchantment`; `testing.enchantment` → `enchant.enchantment` (fan-in `+` with the self-loop); money edges unchanged.
- **Design variation:** add node `design`; replace the enchant self-loop with `enchant.enchantment` → `design.enchantment` → `enchant.enchantment` (design is the sole input into enchant); keep `enchant.enchantment` → `sell.enchantment` and money edges. Mutually exclusive with testing.
- **Preset `lab01`:** `includeTesting: true`, `includeDesign: false`; actors `intern` (wage `2`) and `boss` (wage `3`), stats as configured. Preferred assignments (weight `1` each): intern → enchant, testing; boss → payroll, sell, treasury.
- **Actor pool:** `config/scenarios/actor-pool.json` lists eligible actor ids (not every file under `config/actors/`). Random generation: equal thirds among none / testing / design (never both); 2–4 distinct pool actors; preferred assignments (weight `1`) cover **every** graph node; multi-actor overlap on a node is allowed but sparse (more likely when actors are plentiful relative to nodes). Deterministic for a fixed seed.
- Initial signals: `enchant.enchantment` = genesis empty block; `treasury.money` = `100`; `EnchantmentBlocks` contains genesis; `NextUnitId` = `1`; progress empty/`0`; cycles empty/`0`; no active payroll run; pending money moves empty; `Tick = 0`; `TimePartitions` from committed `config/time-partitions.json`.

## Layout

Under `src/MarlothStrategy.Simulation/`:

- `Graph/` — structural Imp-like types
- `Production/` — signals, catalog, `GameState`, scenario bootstrap/generation, seed compatibility, `AdvanceTick` / `AdvanceTicks`, config DTOs/loaders, pending money moves, payroll run
- `Time/` — nested time partition types, position/boundary queries, loader
- `config/node-types/` — JSON behavior numerics per node type (copied to output)
- `config/actors/` — JSON actor definitions (copied to output)
- `config/scenarios/` — named presets (`lab01.json`) and `actor-pool.json` (copied to output)
- `config/time-partitions.json` — nested calendar hierarchy and session `advanceUnit` (copied to output)

## Error handling

Seed and tick assume a well-formed graph for v1 (programmer invariants). Malformed catalogs or missing node types are exceptional (`InvalidOperationException`). Missing or invalid node-type, actor, preset, actor-pool, or **time-partition** JSON at seed/boot is exceptional (fail-fast with path context). Expected empty stocks, incompatible-enchantment empty ports, payroll funding shortfalls (partial whole-actor pay), unpaid departures at the deadline, and empty pending queues are normal.

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing production rules | [Production flows](../../../game/features/gameplay/production-flows.md) |
| Console session / I/O table | [Console client](../ui/console-client.md) |
| Error handling policy | [Error handling](../platform/error-handling.md) |
| Tests | [Testing](../platform/testing.md) |
