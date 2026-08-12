# Production flows

## Summary

Gameplay centers on **production flows**: directed graphs of **actions** that transform resources. Players optimize dysfunctional configurations. Flows may be **circular**—an action’s input can depend on another action’s output in a cycle.

The first domain is a **magic agency** that creates enchantments as a service.

## When to read this

- Designing or changing production, resources, actions, actors, or assignment rules
- Adding a new production domain or seed scenario
- Explaining turn/tick pacing for production

## Actions and signals

- Each **action** is a station in the graph: a function with typed **inputs** and **outputs**.
- Values that move through the graph are **signals**. Signals are resources (and later richer structures). Initial resource kinds: **money** and **enchantments**.
- Actions consume input signals and produce output signals each production tick.

## Actors and assignment

- An **actor** can be **assigned** to one or more actions.
- Assignment means the actor operates those actions for the tick.
- When an actor is assigned to multiple actions, their **capacity** is split **equally** among those actions (`effort = capacity / assignment count` for that actor).
- Unassigned actions do nothing that tick.
- If several actors are assigned to the same action, their efforts **add**.

Do not use “man/manning” in product language; use **assignment** / **assigned**.

## Circular flows and ticks

- Production updates run in discrete **ticks**. For now, one player turn advances one production tick.
- To avoid resolution-order races on cycles, every action computes outputs from **inputs committed on the previous tick** (buffered signals). Same-tick outputs are not visible to other actions until the next tick.

## Magic agency seed (initial configuration)

| Piece | Detail |
|-------|--------|
| Actors | One actor (`A1`), capacity `1.0` |
| Actions | `enchant` — consumes money, produces enchantments; `sell` — consumes enchantments, produces money |
| Graph | Cycle: enchantments flow `enchant` → `sell`; money flows `sell` → `enchant` |
| Assignment | `A1` assigned to **both** actions → effort `0.5` each |
| Throughput | At effort `1.0`, an action may process up to **20** units of its input per tick (`BaseThroughput = 20`), 1:1 conversion. With effort `0.5`, up to **10** units/tick when stock allows (`floor(BaseThroughput * effort)`). Unconsumed input remains as residual stock. |
| Starting stocks | `enchant` money input = `100`; `sell` enchantments input = `0` |

Expected early behavior: first tick converts 10 money into 10 enchantments at `enchant` while `sell` has nothing to process; second tick `sell` can convert the routed enchantments back into money toward `enchant`.

## Related docs

| Topic | Document |
|-------|----------|
| Simulation graph, tick pipeline, APIs | [Production simulation](../../../technical/features/gameplay/production-simulation.md) |
| High-level vision | [Game design](../../game-design.md) |
