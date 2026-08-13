# Production simulation

## Summary

Authoritative production state lives in **Simulation** as an Imp-inspired node graph plus actors, assignments, buffered port signals, and per-node progress. Updates are **pure** functions composed of discrete batched transforms; the host stores the resulting `GameState` in a mutable variable.

## When to read this

- Changing graph/signal types, tick phases, assignment effort, progress/cost, or seed factories
- Implementing or testing `AdvanceTick` / `GameState`
- Comparing Marloth’s graph model to Imp

## Imp inspiration (no Imp dependency)

Shapes follow Imp’s catalog vs instance split (ports on `NodeType`; instances hold type id + local literals; edges are port-to-port). Differences from core Imp:

| Imp | Marloth production |
|-----|--------------------|
| Data-only transmission; no evaluator | Simulation **evaluates** actions each tick |
| DAG-oriented | **Cycles allowed** via buffered cross-tick signals |
| No actors | **Actors** and **assignments** drive assignment effort |
| Primitive literals | Strongly typed **resource** and **information** signal values |

There is no package/NuGet link to Imp; this is a C# model inspired by Imp’s graph docs.

## Numeric policy

Signal payloads (`Money.Amount`, enchantment `volume` / `darkness` / `fallacy`), node config numerics (`cost`, `effort`, deltas), actor **stats**, and per-node **progress** are floating-point (`double`).

Actor **capacity** and per-node **assignment effort** remain `decimal` ratios. Progress gain converts with `(double)(stat × assignmentEffort)`.

Console display rounds signal numerics to nearest integers; simulation keeps exact doubles.

## Core types

Identifiers are strings (or thin string wrappers): `NodeId`, `EdgeId`, `NodeTypeId`, `PortId`, `SignalTypeId`, `ActorId`.

| Type | Role |
|------|------|
| `Port` / `NodeType` | Catalog: input/output ports + signal types |
| `Node` / `Edge` / `PortReference` / `NodeGraph` | Instance wiring |
| `SignalValue` | Typed payloads: resource `Money(double)` or information `Enchantment(volume, darkness, fallacy)` as `double` |
| `Actor` | `Id`, `Capacity` (`decimal`), `Stats` (`string` → `double`) |
| `Assignment` | Preferred `ActorId` → `NodeId` (many actions per actor) |
| `NodeTypeConfigs` | Per-type behavior numerics loaded from JSON and attached to state |
| `GameState` | Graph + catalog + port signals + actors + preferred assignments + node configs + **node progress** + `Tick` |

Port signals are keyed by `(NodeId, PortId)`. Node progress is keyed by `NodeId`.

### Signal kinds

| Kind | Payload | Route / merge |
|------|---------|---------------|
| **Resource (money)** | Scalar quantity (`Money`) | Continuous circulating value: nodes forward it; routing **sets** the destination (does not stack additive piles). Costs subtract from the value; sell may increment by payout |
| **Information** | Single structure (`Enchantment`) | **Copy** along each outgoing edge; **set** on destination when empty. Mutate only inside node logic |

Residuals are applied first. A routed information copy applies only if the destination is **empty** after residuals; if occupied, that edge’s copy is **skipped** (occupancy). Two routed information writes into an empty port are exceptional (fail-fast).

## Assignment effort and prerequisites

Preferred assignments live on `GameState.Assignments`. Each tick, **effective** assignments are preferred rows whose target node has an enchantment on its process input port.

For each actor, `assignmentEffortPerNode = Capacity / count(effective assignments of that actor)`.  
Per action: assignment effort = sum of contributions. Unassigned / not effective → `0`.

Seed: when sell has no input, only enchant is effective → assignment effort `1.0` on enchant.

Progress gain on a node = sum over effectively assigned actors of `GetStat(actor, key, default) × share`, with defaults `enchanting → 1`, `sales → 1`.

## Progress, config effort, and cost

Each node carries runtime `progress` (`double`, default `0`).

Node configs include:

- **`effort`** — work units per application (seed default `10`)
- **`cost`** (enchant only) — deducted from the continuous money value per successful mutation (seed default `20`)

While `progress >= config.effort` and (for enchant) money can pay `cost`, applications run; each subtracts `config.effort` from progress; enchant also deducts `cost` from money.

Money is exclusively port I/O. The circulating value starts from committed money on the cycle (enchant’s money input when present, else sell’s). Same-tick: enchant emits money after its costs, then sell pass-throughs or **increments by payout**. Edges `enchant.money → sell.money` and `sell.money → enchant.money` commit the results. Insufficient money for enchant costs skips that application (progress unchanged for that attempt).

## Node behaviors

Tunable numerics live in `config/node-types/{enchant,sell}.json` (heterogeneous schemas). Actors load from `config/actors/*.json`. Port layouts stay in code.

### `enchant`

- Inputs: `enchantment`, `money`.
- Outputs: `enchantment`, `money`.
- Stat: `enchanting` (default `1`).
- With assignment effort `> 0` and an input enchantment: add progress; run mutate applications while affordable; **consume** input and **emit** either the mutated result or a **pass-through** copy of the input. Fan-out copies the emitted enchantment only.
- Mutation formula: `volume + volumeDelta`, `darkness + darknessDelta`, `fallacy + darkness + fallacyConstant` (defaults `10` / `1` / `1`).
- Money: consume input and **emit** `money_in - granted * cost` on the money output (pass-through when `granted == 0`).

### `sell`

- Inputs: `enchantment`, `money`; output: `money`.
- Stat: `sales` (default `1`).
- Add progress from assignment; when `progress >= effort`, consume enchantment and **increment** money by `max(payoutFloor, volume - fallacy)`. Otherwise leave enchantment residual and **return** money unchanged (pass-through).
- Emits money on its money output for routing back to enchant. No money cost.

## Tick pipeline

Public API (pure):

```csharp
GameState AdvanceTick(GameState state);
ProductionTickResult AdvanceTickWithReport(GameState state);
// AdvanceTick(state) => AdvanceTickWithReport(state).State;
```

`ProductionTickResult` carries the next `GameState` plus `ImmutableArray<NodeIoRow> Nodes` (one row per node, same order as tick iteration). Each `NodeIoRow` reports the **primary** process ports (enchantment in/out for `enchant` and `sell`; money out for `sell`) with typed `SignalValue` available / residual / produced fields, whether the primary input was consumed, and **`MoneyIn` / `MoneyOut`** for that node’s continuous money transform this tick (enchant: start→after cost; sell: after enchant→after sell pass-through or increment).

Pipeline (each step returns new data; no mutation of prior state):

1. **`ResolveInputs`** — For each action input port, take the value already committed on that port.
2. **`ResolveEffectiveAssignments` / assignment effort** — Filter preferred assignments by prerequisites; split capacity.
3. **`ComputeOutputs`** — Node-type-specific behavior using each node’s port inputs, assignment effort, stats, progress, and cost. Same-tick money chain (enchant then sell). Node iteration order must not change results.
4. **`CommitSignals`** — Residuals; route outputs (money **set**; information set if empty / skip if occupied).
5. **`NextState`** — New signals, updated progress map, `Tick + 1`.

Host pattern:

```csharp
GameState state = MagicAgencySeed.CreateInitialState();
state = AdvanceTick(state); // mutable binding, immutable values
// or: var result = AdvanceTickWithReport(state); state = result.State;
```

## Magic agency seed

Factory: `MagicAgencySeed.CreateInitialState()` (loads node configs and actors from `config/` under the app base directory; overloads accept explicit configs/actors).

- Node types: `enchant` (in/out `enchantment` + `money`), `sell` (in `enchantment` + `money`, out `money`).
- Nodes: `enchant`, `sell`.
- Edges: `enchant.enchantment` → `enchant.enchantment`; `enchant.enchantment` → `sell.enchantment`; `enchant.money` → `sell.money`; `sell.money` → `enchant.money`.
- Actor `intern` from JSON (capacity `1.0`, stats as configured), preferred assignments to both nodes.
- Initial signals (port priming): `enchant.enchantment = (0,0,0)`, `enchant.money = 100`; progress empty/`0`; `Tick = 0`.

## Layout

Under `src/MarlothStrategy.Simulation/`:

- `Graph/` — structural Imp-like types
- `Production/` — signals, catalog, `GameState`, seed, `AdvanceTick`, config DTOs/loaders
- `config/node-types/` — JSON behavior numerics per node type (copied to output)
- `config/actors/` — JSON actor definitions (copied to output)

## Error handling

Seed and tick assume a well-formed graph for v1 (programmer invariants). Malformed catalogs or missing node types are exceptional (`InvalidOperationException`). Missing or invalid node-type or actor JSON at seed/boot is exceptional (fail-fast with path context). Expected empty stocks and occupancy skips are normal.

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing production rules | [Production flows](../../../game/features/gameplay/production-flows.md) |
| Console session / I/O table | [Console client](../ui/console-client.md) |
| Error handling policy | [Error handling](../platform/error-handling.md) |
| Tests | [Testing](../platform/testing.md) |
