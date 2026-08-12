# Production simulation

## Summary

Authoritative production state lives in **Simulation** as an Imp-inspired node graph plus actors, assignments, and buffered port signals. Updates are **pure** functions composed of discrete batched transforms; the host stores the resulting `GameState` in a mutable variable.

## When to read this

- Changing graph/signal types, tick phases, effort, or seed factories
- Implementing or testing `AdvanceTick` / `GameState`
- Comparing Marloth’s graph model to Imp

## Imp inspiration (no Imp dependency)

Shapes follow Imp’s catalog vs instance split (ports on `NodeType`; instances hold type id + local literals; edges are port-to-port). Differences from core Imp:

| Imp | Marloth production |
|-----|--------------------|
| Data-only transmission; no evaluator | Simulation **evaluates** actions each tick |
| DAG-oriented | **Cycles allowed** via buffered cross-tick signals |
| No actors | **Actors** and **assignments** drive effort |
| Primitive literals | Strongly typed **resource** and **information** signal values |

There is no package/NuGet link to Imp; this is a C# model inspired by Imp’s graph docs.

## Numeric policy

Signal payloads (`Money.Amount`, enchantment `volume` / `darkness` / `fallacy`) and node behavior numerics from config are floating-point (`double`).

Actor **capacity** and per-node **effort** remain `decimal` ratios. When converting fractional effort into a process limit, **round down** (`decimal.Floor` / toward −∞).

Console display rounds signal numerics to nearest integers; simulation keeps exact doubles.

## Core types

Identifiers are strings (or thin string wrappers): `NodeId`, `EdgeId`, `NodeTypeId`, `PortId`, `SignalTypeId`, `ActorId`.

| Type | Role |
|------|------|
| `Port` / `NodeType` | Catalog: input/output ports + signal types |
| `Node` / `Edge` / `PortReference` / `NodeGraph` | Instance wiring |
| `SignalValue` | Typed payloads: resource `Money(double)` or information `Enchantment(volume, darkness, fallacy)` as `double` |
| `Actor` | `Id`, `Capacity` (`decimal`) |
| `Assignment` | `ActorId` → `NodeId` (many actions per actor) |
| `NodeTypeConfigs` | Per-type behavior numerics loaded from JSON and attached to state |
| `GameState` | Graph + node-type catalog + port signal map + actors + assignments + node configs + `Tick` |

Port signals are keyed by `(NodeId, PortId)`.

### Signal kinds

| Kind | Payload | Route / merge |
|------|---------|---------------|
| **Resource** | Scalar quantity (`Money`) | **Add** into destination stocks; consume by subtracting |
| **Information** | Single structure (`Enchantment`) | **Copy** along each outgoing edge; **set** on destination (no additive merge). Mutate only inside node logic |

Two information writes into the same port in one commit are exceptional (fail-fast). Empty residual plus one routed copy is the normal information commit path.

## Effort

For each actor, `effortPerAssignment = Capacity / count(assignments of that actor)`.  
Per action: `effort = sum(effort contributions of assigned actors)`.  
Unassigned → `0`.

Seed: capacity `1.0`, two assignments → `0.5` each.

Process limit: `limit = floor(baseThroughput * effort)` using each node type’s `baseThroughput` from config (seed defaults: `20`).

- Resource converters (when applicable): `processed = min(available, limit)`.
- Information nodes (`enchant`, `sell`): run when `limit >= 1` and an enchantment input is present; process **at most one** enchantment per tick.

## Node behaviors

Tunable numerics live in `config/node-types/{enchant,sell}.json` (heterogeneous schemas). Port layouts stay in code. Seed defaults match the formulas below.

### `enchant`

- Inputs: `enchantment` (processed), `money` (treasury stock only — never consumed).
- Output: `enchantment`.
- When run: consume input enchantment; emit copy with  
  `volume + volumeDelta`, `darkness + darknessDelta`, `fallacy + darkness + fallacyConstant` (input darkness).  
  Defaults: `volumeDelta=10`, `darknessDelta=1`, `fallacyConstant=1`.

### `sell`

- Input: `enchantment`; output: `money`.
- When run: consume input enchantment; produce `Money(max(payoutFloor, volume - fallacy))`.  
  Default: `payoutFloor=0`.

## Two-phase tick

Public API (pure):

```csharp
GameState AdvanceTick(GameState state);
ProductionTickResult AdvanceTickWithReport(GameState state);
// AdvanceTick(state) => AdvanceTickWithReport(state).State;
```

`ProductionTickResult` carries the next `GameState` plus `ImmutableArray<NodeIoRow> Nodes` (one row per node, same order as tick iteration). Each `NodeIoRow` reports the **primary** process ports (enchantment in/out for `enchant` and `sell`; money out for `sell`) with typed `SignalValue` available / residual / produced fields and whether the primary input was consumed.

Stock diffs alone are not a substitute for the report under cycles (net stocks can be unchanged while nodes still process).

Pipeline (each step returns new data; no mutation of prior state):

1. **`ResolveInputs`** — For each action input port, take the value already committed on that port (fed by prior routing / seed). Incoming edges describe which producer output was routed there last commit; reads never use same-tick outputs from other nodes.
2. **`ComputeOutputs`** — For each action, given resolved inputs + effort, emit outputs and input residuals via **node-type-specific** behavior. Node iteration order must not change results.
3. **`CommitSignals`** — Build the next port signal map: residuals on inputs; for each outgoing edge, route a **copy** of the produced output onto the consumer input port (resource: add; information: set). Clear producer output ports after routing (destination inputs are the stock locations).
4. **`NextState`** — Same structure, new signals, `Tick + 1`.

Host pattern:

```csharp
GameState state = MagicAgencySeed.CreateInitialState();
state = AdvanceTick(state); // mutable binding, immutable values
// or: var result = AdvanceTickWithReport(state); state = result.State;
```

## Magic agency seed

Factory: `MagicAgencySeed.CreateInitialState()` (loads node configs from `config/node-types/` under the app base directory; overload accepts an explicit `NodeTypeConfigs`).

- Node types: `enchant` (in `enchantment` + `money`, out `enchantment`), `sell` (in `enchantment`, out `money`).
- Nodes: `enchant`, `sell`.
- Edges: `enchant.enchantment` → `enchant.enchantment`; `enchant.enchantment` → `sell.enchantment`; `sell.money` → `enchant.money`.
- Actor `A1` capacity `1.0`, assigned to both nodes.
- Initial signals: `enchant.enchantment = (0,0,0)`, `enchant.money = 100`; `Tick = 0`.

## Layout

Under `src/MarlothStrategy.Simulation/`:

- `Graph/` — structural Imp-like types
- `Production/` — signals, catalog, `GameState`, seed, `AdvanceTick`, config DTOs/loader
- `config/node-types/` — JSON behavior numerics per node type (copied to output)

## Error handling

Seed and tick assume a well-formed graph for v1 (programmer invariants). Malformed catalogs or missing node types are exceptional (`InvalidOperationException`). Missing or invalid node-type JSON at seed/boot is exceptional (fail-fast with path context). Expected empty stocks are normal (process zero / idle).

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing production rules | [Production flows](../../../game/features/gameplay/production-flows.md) |
| Console session / I/O table | [Console client](../ui/console-client.md) |
| Error handling policy | [Error handling](../platform/error-handling.md) |
| Tests | [Testing](../platform/testing.md) |
