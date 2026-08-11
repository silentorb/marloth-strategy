# Game feature docs (read on demand)

**Features index** for Marloth Strategy. **Source of truth** for design, feel, and player-facing rules beyond the high-level notes in [game-design.md](../game-design.md). **Do not** open every file for general engineering tasks.

Feature docs may later be grouped under `ui/`, `gameplay/`, `session/`, and similar folders.

1. Skim the **trigger** lines below.
2. If a trigger matches your current task, read **only** that markdown file.

| File | Read when… |
|------|------------|
| [../game-design.md](../game-design.md) | Reading **gameplay vision**, genre, or surface (console vs Godot). **Do not edit** unless the user explicitly instructed changes to that file. |
| [gameplay/production-flows.md](gameplay/production-flows.md) | Designing or changing **production flows**, actions, resources/signals, **actors**, **assignment**, circular configs, or the magic-agency seed. |
| [session/console-loop.md](session/console-loop.md) | Changing **console session** commands (Enter / `q`), open-ended play, or player-facing **post-tick I/O reports**. |
