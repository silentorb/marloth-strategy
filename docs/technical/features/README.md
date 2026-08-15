# Technical feature docs (read on demand)

**Source of truth** for architecture and engineering contracts. **Do not** open every file for general tasks.

Feature docs may later be grouped under `ui/`, `gameplay/`, `session/`, and `platform/` (further nesting may be added later).

1. Skim the **trigger** lines below.
2. If a trigger matches your current task, read **only** that markdown file (and linked paths as needed).

| File | Read when… |
|------|------------|
| [platform/error-handling.md](platform/error-handling.md) | Adding or changing **APIs**, loaders, boot paths, or any **multi-step** logic where failures must be chosen (throw vs explicit outcome vs abort). |
| [platform/testing.md](platform/testing.md) | Working on **automated tests**, test layout, or **bug-driven regression** policy (failing test first / escalate brittle coverage). |
| [gameplay/production-simulation.md](gameplay/production-simulation.md) | Changing **production graph** types, port signals, **AdvanceTick** / **AdvanceTicks** / **AdvanceTickWithReport**, **time partitions**, effort/assignment math, payroll/treasury, **scenario presets**, or seed factories in Simulation. |
| [ui/console-client.md](ui/console-client.md) | Changing **console Client** session loop, modal screens (`w` / `a`), prompts (Enter / `-` / `+` / `q`), step resolution prefs, stock/I/O formatting, **SCENARIO_PRESET** / **SCENARIO_SEED**, or IDE play launch for the App. |
| [../technical-design.md](../technical-design.md) | **Architecture**, docs-as-SoT, console vs eventual Godot, TDD, or repository layout. |
| [../../game/game-design.md](../../game/game-design.md) | Reading **gameplay vision**, genre, or surface (console vs Godot). **Do not edit** unless the user explicitly instructed changes to that file. |
