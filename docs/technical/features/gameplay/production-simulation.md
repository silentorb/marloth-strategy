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
| Primitive literals | Strongly typed **resource** signal values (extensible later) |

There is no package/NuGet link to Imp; this is a C# model inspired by Imp’s graph docs.

## Core types

Identifiers are strings (or thin string wrappers): `NodeId`, `EdgeId`, `NodeTypeId`, `PortId`, `SignalTypeId`, `ActorId`.

| Type | Role |
|------|------|
| `Port` / `NodeType` | Catalog: input/output ports + signal types |
| `Node` / `Edge` / `PortReference` / `NodeGraph` | Instance wiring |
| `SignalValue` | Typed quantities (v1: `Money`, `Enchantments` as decimals) |
| `Actor` | `Id`, `Capacity` |
| `Assignment` | `ActorId` → `NodeId` (many actions per actor) |
| `GameState` | Graph + node-type catalog + port signal map + actors + assignments + `Tick` |

Port signals are keyed by `(NodeId, PortId)`.

## Effort

For each actor, `effortPerAssignment = Capacity / count(assignments of that actor)`.  
Per action: `effort = sum(effort contributions of assigned actors)`.  
Unassigned → `0`.

Seed: capacity `1.0`, two assignments → `0.5` each.

Throughput: `processed = min(availableInput, BaseThroughput * effort)` with `BaseThroughput = 2` and 1:1 conversion. Residual unconsumed input stays on the input port.

## Two-phase tick

Public API (pure):

```csharp
GameState AdvanceTick(GameState state);
```

Pipeline (each step returns new data; no mutation of prior state):

1. **`ResolveInputs`** — For each action input port, take the value already committed on that port (fed by prior routing / seed). Incoming edges describe which producer output was routed there last commit; reads never use same-tick outputs from other nodes.
2. **`ComputeOutputs`** — For each action, given resolved inputs + effort, emit output amounts and input residuals. Node iteration order must not change results.
3. **`CommitSignals`** — Build the next port signal map: residuals on inputs; route each produced output along outgoing edges onto consumer input ports (additive if multiple edges target the same port—seed has one edge per resource). Clear producer output ports after routing (or store only on destination inputs—implementation keeps destination inputs as the stock locations).
4. **`NextState`** — Same structure, new signals, `Tick + 1`.

Host pattern:

```csharp
GameState state = MagicAgencySeed.CreateInitialState();
state = AdvanceTick(state); // mutable binding, immutable values
```

## Magic agency seed

Factory: `MagicAgencySeed.CreateInitialState()`.

- Node types: `enchant` (in `money`, out `enchantments`), `sell` (in `enchantments`, out `money`).
- Nodes: `enchant`, `sell`.
- Edges: `enchant.enchantments` → `sell.enchantments`; `sell.money` → `enchant.money`.
- Actor `A1` capacity `1.0`, assigned to both nodes.
- Initial signals: `enchant.money = 10`, `sell.enchantments = 0`; `Tick = 0`.

## Layout

Under `src/MarlothStrategy.Simulation/`:

- `Graph/` — structural Imp-like types
- `Production/` — signals, catalog, `GameState`, seed, `AdvanceTick`

## Error handling

Seed and tick assume a well-formed graph for v1 (programmer invariants). Malformed catalogs or missing node types are exceptional (`InvalidOperationException`). Expected empty stocks are normal (process zero).

## Related docs

| Topic | Document |
|-------|----------|
| Player-facing production rules | [Production flows](../../../game/features/gameplay/production-flows.md) |
| Error handling policy | [Error handling](../platform/error-handling.md) |
| Tests | [Testing](../platform/testing.md) |
