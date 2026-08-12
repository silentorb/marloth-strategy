# Production flows

## Summary

Gameplay centers on **production flows**: directed graphs of **actions** that transform signals. Players optimize dysfunctional configurations. Flows may be **circular**—an action’s input can depend on another action’s output in a cycle.

The first domain is a **magic agency** that creates enchantments as a service.

## When to read this

- Designing or changing production, resources, actions, actors, or assignment rules
- Adding a new production domain or seed scenario
- Explaining turn/tick pacing for production

## Actions and signals

- Each **action** is a station in the graph: a function with typed **inputs** and **outputs**.
- Values that move through the graph are **signals**. There are two kinds:

| Kind | Behavior | Example |
|------|----------|---------|
| **Resource** | Primitive scalar; **added** and **subtracted** when routed or consumed | **money** (quantity) |
| **Information** | Complex structure; **copied** on route and **mutated** by actions (working title) | **enchantment** (single value with `volume`, `darkness`, `fallacy`) |

- Money signals carry a quantity of money. Enchantment signals carry a **single** enchantment (not a count).
- Actions consume input signals and produce output signals each production tick according to their node rules.

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
| Actions | `enchant` — consumes an enchantment, produces a mutated copy; `sell` — consumes an enchantment, produces money |
| Enchant formula | `volume + 10`, `darkness + 1`, `fallacy + darkness + 1` (using input darkness) |
| Sell formula | money = `max(0, volume - fallacy)` |
| Graph | Enchantment **fans out**: copy `enchant` → `enchant` (feedback) and copy `enchant` → `sell`; money flows `sell` → `enchant` (treasury; enchant does not consume money) |
| Assignment | `A1` assigned to **both** actions → effort `0.5` each |
| Throughput | At effort `1.0`, an action may process up to **20** resource units per tick when applicable (`BaseThroughput = 20`). Information actions process **at most one** enchantment per tick when `floor(BaseThroughput * effort) >= 1` and an input is present. |
| Starting stocks | `enchant` enchantment = `(volume:0, darkness:0, fallacy:0)`; `enchant` money = `100`; `sell` enchantment empty |

Expected early behavior: first tick `enchant` mutates `(0,0,0)` → `(10,1,1)` and fans copies to itself and `sell` while `sell` is idle; second tick `sell` pays `max(0,10-1)=9` money onto the treasury while `enchant` mutates again to `(20,2,3)`.

## Related docs

| Topic | Document |
|-------|----------|
| Simulation graph, tick pipeline, APIs | [Production simulation](../../../technical/features/gameplay/production-simulation.md) |
| High-level vision | [Game design](../../game-design.md) |
