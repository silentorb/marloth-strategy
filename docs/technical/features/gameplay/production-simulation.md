# Production simulation

## Summary

Authoritative production state lives in **Simulation** as an Imp-inspired node graph plus actors, assignments, buffered port signals, per-node progress, and per-node timers. Updates are **pure** functions composed of discrete batched transforms; the host stores the resulting `GameState` in a mutable variable.

## When to read this

- Changing graph/signal types, tick phases, assignment effort, progress, payroll/treasury, or seed factories
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

Signal payloads (`Money.Amount`, enchantment `volume` / `darkness` / `fallacy`), node config numerics (`effort`, deltas, wages, payout floor), actor **stats** / **wage**, and per-node **progress** are floating-point (`double`).

Actor **capacity** and per-node **assignment effort** remain `decimal` ratios. Progress gain converts with `(double)(stat × assignmentEffort)`.

Payroll **period** and remaining **timers** are integers (`int`).

Console display rounds signal numerics to nearest integers; simulation keeps exact doubles.

## Core types

Identifiers are strings (or thin string wrappers): `NodeId`, `EdgeId`, `NodeTypeId`, `PortId`, `SignalTypeId`, `ActorId`.

| Type | Role |
|------|------|
| `Port` / `NodeType` | Catalog: input/output ports + signal types |
| `Node` / `Edge` / `PortReference` / `NodeGraph` | Instance wiring |
| `SignalValue` | Typed payloads: resource `Money(double)` or information `Enchantment(volume, darkness, fallacy)` as `double` |
| `Actor` | `Id`, `Capacity` (`decimal`), `Stats` (`string` → `double`), optional `Wage` (`double?`) |
| `Assignment` | Preferred `ActorId` → `NodeId` (many nodes per actor) |
| `NodeTypeConfigs` | Per-type behavior numerics loaded from JSON and attached to state |
| `GameState` | Graph + catalog + port signals + actors + preferred assignments + node configs + **node progress** + **node timers** + `Tick` |

Port signals are keyed by `(NodeId, PortId)`. Node progress and node timers are keyed by `NodeId`.

### Signal kinds

| Kind | Payload | Route / merge |
|------|---------|---------------|
| **Resource (money)** | Scalar quantity (`Money`) | Owned on the holding node; routing **adds** into the destination pile. Sell emits payout; payroll debits treasury |
| **Information** | Single structure (`Enchantment`) | **Copy** along each outgoing edge; **set** on destination when empty. Mutate only inside node logic |

Residuals are applied first. A routed information copy applies only if the destination is **empty** after residuals; if occupied, that edge’s copy is **skipped** (occupancy). Two routed information writes into an empty port are exceptional (fail-fast).

## Assignment effort and prerequisites

Preferred assignments live on `GameState.Assignments`. Each tick, **effective** assignments are preferred rows whose target node has an enchantment on its process input port.

For each actor, `assignmentEffortPerNode = Capacity / count(effective assignments of that actor)`.  
Per node: assignment effort = sum of contributions. Unassigned / not effective → `0`.

Seed: when sell has no input, only enchant is effective → assignment effort `1.0` on enchant.

Progress gain on a node = sum over effectively assigned actors of `GetStat(actor, key, default) × share`, with defaults `enchanting → 1`, `sales → 1`.

## Progress and config effort

Each process node carries runtime `progress` (`double`, default `0`).

Node configs include:

- **`effort`** (enchant / sell) — work units per application (seed default `10`)
- **`defaultWage`** / **`period`** (payroll) — wage fallback and payday interval (seed defaults `10` / `5`)

While `progress >= config.effort`, applications run; each subtracts `config.Effort` from progress. Enchant has no money cost.

## Node behaviors

Tunable numerics live in `config/node-types/{enchant,sell,payroll}.json` (heterogeneous schemas). Actors load from `config/actors/*.json`. Port layouts stay in code. Treasury has no numerics file.

### `enchant`

- Input / output: `enchantment` only.
- Stat: `enchanting` (default `1`).
- With assignment effort `> 0` and an input enchantment: add progress; run mutate applications from progress only; **consume** input and **emit** either the mutated result or a **pass-through** copy of the input. Fan-out copies the emitted enchantment only.
- Mutation formula: `volume + volumeDelta`, `darkness + darknessDelta`, `fallacy + darkness + fallacyConstant` (defaults `10` / `1` / `1`).

### `sell`

- Input: `enchantment`; output: `money`.
- Stat: `sales` (default `1`).
- Add progress from assignment; when `progress >= effort`, consume enchantment and **emit** `max(payoutFloor, volume - fallacy)` on the money output. Otherwise leave enchantment residual and emit no money.
- Edge `sell.money` → `treasury.money` adds the payout into treasury.

### `treasury`

- Input: `money` (stock lives on the port; never consumed as process input).
- No assignment, no progress, no outputs.
- Holds agency cash; sell deposits and payroll debits update this pile.

### `payroll`

- No ports. No assignment.
- Config: `defaultWage`, `period`.
- `GameState.NodeTimers[payroll]` is a countdown seeded to `period`. Each tick: decrement; when remaining hits `0`, payday then reset to `period` (first payday on tick 5 with seed defaults).
- Effective wage per actor = actor `Wage` if set, else `defaultWage`. Payday totals wages of **all** `GameState.Actors` after that tick’s sell deposits.
- If treasury money ≥ total: subtract total. If short: no partial pay; treasury unchanged by payroll; clear all actors and assignments (quit). Empty roster → wage total `0` (no-op).
- v1: exactly one `payroll` and one `treasury` node in the seed graph; missing/duplicate is fail-fast.

## Tick pipeline

Public API (pure):

```csharp
GameState AdvanceTick(GameState state);
ProductionTickResult AdvanceTickWithReport(GameState state);
// AdvanceTick(state) => AdvanceTickWithReport(state).State;
```

`ProductionTickResult` carries the next `GameState` plus `ImmutableArray<NodeIoRow> Nodes` (one row per process-reporting node as implemented, same order as tick iteration). Each `NodeIoRow` reports the **primary** process ports (enchantment in/out for `enchant`; enchantment in / money out for `sell`) with typed `SignalValue` available / residual / produced fields and whether the primary input was consumed.

Pipeline (each step returns new data; no mutation of prior state):

1. **`ResolveInputs`** — For each node input port, take the value already committed on that port.
2. **`ResolveEffectiveAssignments` / assignment effort** — Filter preferred assignments by prerequisites; split capacity.
3. **`ComputeOutputs`** — Node-type-specific behavior using each node’s port inputs, assignment effort, stats, and progress. Nodes are independent (no same-tick money chain). Node iteration order must not change results.
4. **`CommitSignals`** — Residuals; route outputs (money **add**; information set if empty / skip if occupied).
5. **`AdvancePayroll`** — Decrement timers; on payday debit treasury or mass-quit actors.
6. **`NextState`** — New signals, updated progress/timers maps, actors/assignments, `Tick + 1`.

Host pattern:

```csharp
GameState state = MagicAgencySeed.CreateInitialState();
state = AdvanceTick(state); // mutable binding, immutable values
// or: var result = AdvanceTickWithReport(state); state = result.State;
```

## Magic agency seed

Factory: `MagicAgencySeed.CreateInitialState()` (loads node configs and actors from `config/` under the app base directory; overloads accept explicit configs/actors).

- Node types: `enchant` (in/out `enchantment`), `sell` (in `enchantment`, out `money`), `treasury` (in `money`), `payroll` (no ports).
- Nodes: `enchant`, `sell`, `treasury`, `payroll`.
- Edges: `enchant.enchantment` → `enchant.enchantment`; `enchant.enchantment` → `sell.enchantment`; `sell.money` → `treasury.money`.
- Actor `intern` from JSON (capacity `1.0`, stats as configured, wage unset), preferred assignments to enchant and sell.
- Initial signals: `enchant.enchantment = (0,0,0)`, `treasury.money = 100`; progress empty/`0`; payroll timer = `period`; `Tick = 0`.

## Layout

Under `src/MarlothStrategy.Simulation/`:

- `Graph/` — structural Imp-like types
- `Production/` — signals, catalog, `GameState`, seed, `AdvanceTick`, config DTOs/loaders
- `config/node-types/` — JSON behavior numerics per node type (copied to output)
- `config/actors/` — JSON actor definitions (copied to output)

## Error handling

Seed and tick assume a well-formed graph for v1 (programmer invariants). Malformed catalogs or missing node types are exceptional (`InvalidOperationException`). Missing or invalid node-type or actor JSON at seed/boot is exceptional (fail-fast with path context). Expected empty stocks, occupancy skips, and payday mass-quit are normal.

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing production rules | [Production flows](../../../game/features/gameplay/production-flows.md) |
| Console session / I/O table | [Console client](../ui/console-client.md) |
| Error handling policy | [Error handling](../platform/error-handling.md) |
| Tests | [Testing](../platform/testing.md) |
