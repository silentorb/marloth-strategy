# Production simulation

## Summary

Authoritative production state lives in **Simulation** as an Imp-inspired node graph plus actors, assignments, buffered port signals, per-node progress, per-node timers, and a FIFO **pending money-move** queue for treasury. Updates are **pure** functions composed of discrete batched transforms; the host stores the resulting `GameState` in a mutable variable.

## When to read this

- Changing graph/signal types, tick phases, assignment effort, progress, payroll/treasury, testing, or seed factories
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

Signal payloads (`Money.Amount`, enchantment `volume` / `darkness` / `fallacy`), node config numerics (`effort`, deltas, wages, payout floor, fallacy reduction), actor **stats** / **wage**, and per-node **progress** are floating-point (`double`).

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
| `PendingMoneyMove` | FIFO treasury queue entry: direction `In` / `Out` + `Amount` |
| `NodeTypeConfigs` | Per-type behavior numerics loaded from JSON and attached to state |
| `GameState` | Graph + catalog + port signals + actors + preferred assignments + node configs + **node progress** + **node timers** + **pending money moves** + `Tick` |

Port signals are keyed by `(NodeId, PortId)`. Node progress and node timers are keyed by `NodeId`.

### Signal kinds

| Kind | Payload | Route / merge |
|------|---------|---------------|
| **Resource (money)** | Scalar quantity (`Money`) | Owned on the holding node. Money routed onto treasury’s money port **enqueues** a pending inbound move (does not `AddResource` into the committed pile). Payroll payday enqueues outbound; treasury applications apply one pending move per effort |
| **Information** | Single structure (`Enchantment`) | **Copy** along each outgoing edge; **set** on destination when empty. Mutate only inside node logic |

Residuals are applied first. A routed information copy applies only if the destination is **empty** after residuals; if occupied, that edge’s copy is **skipped** (occupancy). Two routed information writes into an empty port are exceptional (fail-fast).

## Assignment effort and prerequisites

Preferred assignments live on `GameState.Assignments`. Each tick, **effective** assignments are preferred rows whose target node meets prerequisites:

| Node type | Prerequisite |
|-----------|--------------|
| `enchant` / `testing` / `sell` | Enchantment on process input port |
| `treasury` | `PendingMoneyMoves` non-empty |
| `payroll` | Timer remaining is `0` (payday due) |

For each actor, `assignmentEffortPerNode = Capacity / count(effective assignments of that actor)`.  
Per node: assignment effort = sum of contributions. Unassigned / not effective → `0`.

Progress gain on a node = sum over effectively assigned actors of `GetStat(actor, key, default) × share`, with defaults `enchanting` / `testing` / `sales` / `treasury` / `payroll` → `1`.

## Progress and config effort

Each seed node carries runtime `progress` (`double`, default `0`).

Node configs include:

- **`effort`** (enchant / testing / sell / treasury / payroll) — base work units per application
- enchant also has `volumeDelta`, `darknessDelta`, `fallacyConstant`
- testing also has `fallacyReduction`
- sell also has `payoutFloor`
- payroll also has `defaultWage` / `period`

**Enchant** required work per mutation = `config.effort + current.darkness` (recomputed after each mutate). Other nodes use `config.effort` per application. While progress covers the required amount, applications run and subtract that amount.

## Node behaviors

Tunable numerics live in `config/node-types/{enchant,testing,sell,treasury,payroll}.json` (heterogeneous schemas). Actors load from `config/actors/*.json`. Port layouts stay in code.

### `enchant`

- Input / output: `enchantment` only.
- Stat: `enchanting` (default `1`).
- With assignment effort `> 0` and an input enchantment: add progress; run mutate applications from progress only; **consume** input and **emit** either the mutated result or a **pass-through** copy of the input. Fan-out copies the emitted enchantment only.
- Mutation formula: `volume + volumeDelta`, `darkness + darknessDelta`, `fallacy + darkness + fallacyConstant` (defaults `10` / `1` / `1`).
- Required work per mutation: `effort + darkness` on the enchantment being mutated.

### `testing`

- Input / output: `enchantment` only.
- Stat: `testing` (default `1`).
- With assignment effort `> 0` and an input enchantment: add progress; while `progress >= effort`, apply fallacy reduction and subtract `effort`; **consume** and **emit** (reduced or pass-through).
- Each application: `fallacy = max(0, fallacy - fallacyReduction × effectiveActorCount)` where `effectiveActorCount` is the number of actors with a positive share on testing that tick (seed defaults `effort: 10`, `fallacyReduction: 5`).

### `sell`

- Input: `enchantment`; output: `money`.
- Stat: `sales` (default `1`).
- Add progress from assignment; when `progress >= effort`, consume enchantment and **emit** `max(payoutFloor, volume - fallacy)` on the money output. Otherwise leave enchantment residual and emit no money.
- Edge `sell.money` → `treasury.money` enqueues pending inbound (does not immediately grow the committed pile).

### `treasury`

- Input: `money` (committed stock on the port; never consumed as process input).
- Config: `effort` (seed `2`).
- Stat: `treasury` (default `1`).
- Always residuals the committed money pile.
- Effective when pending queue non-empty. Gain progress when assigned; each application dequeues **one** move from the start-of-tick queue and subtracts `effort`. **In** adds to the pile. **Out** debits if pile ≥ amount; otherwise mass-quit (clear actors and assignments), drop the out-move, leave pile unchanged.
- Does not process moves enqueued later in the same tick (no same-tick money chain).

### `payroll`

- No ports.
- Config: `defaultWage`, `period`, `effort` (seed `10` / `5` / `5`).
- Stat: `payroll` (default `1`).
- `GameState.NodeTimers[payroll]` is a countdown seeded to `period`.
- **Timer (no actor):** when start-of-tick `remaining > 0`, end-of-tick sets `remaining - 1`. When start-of-tick `remaining == 0`, payday is due (timer unchanged unless payday application resets it to `period`).
- **Payday due (start of tick `remaining == 0`):** effective for assignment. Gain progress when assigned; when `progress >= effort`, enqueue pending outbound for the wage total of all current actors, subtract effort, reset timer to `period`. Wage total `0` → reset timer without enqueue.
- Effective wage per actor = actor `Wage` if set, else `defaultWage`. Shortfall / mass-quit runs when treasury applies the out-move.
- v1: exactly one `payroll` and one `treasury` node in the seed graph; missing/duplicate is fail-fast.

## Tick pipeline

Public API (pure):

```csharp
GameState AdvanceTick(GameState state);
ProductionTickResult AdvanceTickWithReport(GameState state);
// AdvanceTick(state) => AdvanceTickWithReport(state).State;
```

`ProductionTickResult` carries the next `GameState` plus `ImmutableArray<NodeIoRow> Nodes` (one row per process-reporting node as implemented, same order as tick iteration). Each `NodeIoRow` reports the **primary** process ports (enchantment in/out for `enchant` / `testing`; enchantment in / money out for `sell`) with typed `SignalValue` available / residual / produced fields and whether the primary input was consumed.

Pipeline (each step returns new data; no mutation of prior state):

1. **`ResolveInputs`** — For each node input port, take the value already committed on that port.
2. **`ResolveEffectiveAssignments` / assignment effort** — Filter preferred assignments by prerequisites; split capacity.
3. **`ComputeOutputs`** — Node-type-specific behavior using each node’s port inputs, assignment effort, stats, progress, and start-of-tick pending moves. Nodes are independent (no same-tick money chain). Node iteration order must not change results. May enqueue payroll outbound onto a next-pending builder; treasury only drains the start-of-tick queue into residuals / actor updates.
4. **`CommitSignals`** — Residuals; route outputs (information set if empty / skip if occupied; money to treasury **enqueues inbound** on the pending builder).
5. **`AdvancePayrollTimer`** — If payroll remaining `> 0`, decrement by 1 (no auto-pay).
6. **`NextState`** — New signals, updated progress/timers/pending maps, actors/assignments, `Tick + 1`.

Host pattern:

```csharp
GameState state = MagicAgencySeed.CreateInitialState();
state = AdvanceTick(state); // mutable binding, immutable values
// or: var result = AdvanceTickWithReport(state); state = result.State;
```

## Magic agency seed

Factory: `MagicAgencySeed.CreateInitialState()` (loads node configs and actors from `config/` under the app base directory; overloads accept explicit configs/actors).

- Node types: `enchant` (in/out `enchantment`), `testing` (in/out `enchantment`), `sell` (in `enchantment`, out `money`), `treasury` (in `money`), `payroll` (no ports).
- Nodes: `enchant`, `testing`, `sell`, `treasury`, `payroll`.
- Edges: `enchant.enchantment` → `enchant.enchantment`; `enchant.enchantment` → `testing.enchantment`; `testing.enchantment` → `sell.enchantment`; `sell.money` → `treasury.money` (pending inbound on commit).
- Actor `intern` from JSON (capacity `1.0`, stats as configured, wage unset), preferred assignments to enchant, testing, sell, treasury, and payroll.
- Initial signals: `enchant.enchantment = (0,0,0)`, `treasury.money = 100`; progress empty/`0`; payroll timer = `period`; pending money moves empty; `Tick = 0`.

## Layout

Under `src/MarlothStrategy.Simulation/`:

- `Graph/` — structural Imp-like types
- `Production/` — signals, catalog, `GameState`, seed, `AdvanceTick`, config DTOs/loaders, pending money moves
- `config/node-types/` — JSON behavior numerics per node type (copied to output)
- `config/actors/` — JSON actor definitions (copied to output)

## Error handling

Seed and tick assume a well-formed graph for v1 (programmer invariants). Malformed catalogs or missing node types are exceptional (`InvalidOperationException`). Missing or invalid node-type or actor JSON at seed/boot is exceptional (fail-fast with path context). Expected empty stocks, occupancy skips, payday mass-quit, and empty pending queues are normal.

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing production rules | [Production flows](../../../game/features/gameplay/production-flows.md) |
| Console session / I/O table | [Console client](../ui/console-client.md) |
| Error handling policy | [Error handling](../platform/error-handling.md) |
| Tests | [Testing](../platform/testing.md) |
