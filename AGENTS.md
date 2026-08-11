# Agent notes — marloth-strategy

## Project

- **Marloth Strategy** is a single-player turn-based strategy game.
- **Eventual** surface: Godot game.
- **Current** surface: C# ASCII console prototype (no Godot yet).

## Stack

- .NET 8 (`net8.0`)
- Dev Container under [`.devcontainer/`](.devcontainer/) (Debian base + `dotnet:2` feature; no Godot/WSLg)
- SDK pin: [`global.json`](global.json)

## Entry

```bash
dotnet run --project src/MarlothStrategy.Console.App
```

Play from the IDE: Run/F5 configuration **Marloth Strategy (Console)** (`.vscode/launch.json` → `dotnet run` in the integrated terminal; no C# debugger). Same label exists as a **Run Task**. That is for playing the prototype, not human debugging.

Solution: [`MarlothStrategy.sln`](MarlothStrategy.sln). Projects: `MarlothStrategy.Console.App` (exe host), `MarlothStrategy.Console.Client` (ASCII presentation), `MarlothStrategy.Simulation` (shared game logic).

## Debugging

Users **play** and report bugs; they do **not** step through a debugger. **100% of debugging is agent work.**

- Expect reports from play sessions (symptoms, approximate tick/actions, expected vs actual).
- Reproduce with `dotnet test`, targeted unit coverage, and non-interactive console smoke when the session loop matters, e.g. `printf '\n\nq\n' | dotnet run --project src/MarlothStrategy.Console.App`.
- Prefer a failing regression test at the lowest sound layer before fixing ([bug-regression-tests](.cursor/rules/bug-regression-tests.mdc), [testing.md](docs/technical/features/platform/testing.md)).
- Do not assume the user will attach a debugger, set breakpoints, or interpret call stacks.

## Conventions

- Use **Unix (LF)** line endings ([`.gitattributes`](.gitattributes), [`.editorconfig`](.editorconfig); Dev Container sets `files.eol` to `\n`).
- Prefer changing game logic in this repo’s console prototype until a Godot client exists.
- Match existing style in files you touch.
- **`docs/game/game-design.md` is locked:** Do **not** create, edit, or delete that file unless the **user explicitly instructed** changes to it in the current conversation. Put secondary design detail in [docs/game/features/](docs/game/features/) instead. Reading it is fine; proposing edits without that instruction is not.
- **Bug regressions:** When fixing a user-reported bug the suite missed, add a regression test at the lowest sound layer—or escalate instead of brittle/flaky coverage. See [`.cursor/rules/bug-regression-tests.mdc`](.cursor/rules/bug-regression-tests.mdc) and [docs/technical/features/platform/testing.md](docs/technical/features/platform/testing.md).
- **Error handling:** Prefer explicit outcomes for expected failures; use exceptions only for truly exceptional cases or documented fail-fast abort boundaries. See [`.cursor/rules/error-handling.mdc`](.cursor/rules/error-handling.mdc) and [docs/technical/features/platform/error-handling.md](docs/technical/features/platform/error-handling.md).
- **Plans:** Every Cursor plan must include a dedicated **Testing** section and a **Commit strategy** (see [`.cursor/rules/plan-commit-workflow.mdc`](.cursor/rules/plan-commit-workflow.mdc)).

## Product and engineering docs (source of truth)

[`docs/`](docs/) is the **source of truth for functionality**. Code and tests implement the docs; when they disagree, update code to match docs (and keep docs current when changing behavior).

- [docs/game/game-design.md](docs/game/game-design.md) — vision (locked; see Conventions). Read when needing gameplay feel or surface scope; do not edit without explicit user instruction.
- [docs/game/features/README.md](docs/game/features/README.md) — Game **features index** (secondary design / player-facing rules).
- [docs/technical/technical-design.md](docs/technical/technical-design.md) — Architecture, docs-as-SoT, layout. Read when choosing architecture or tests.
- [docs/README.md](docs/README.md) — Docs layout overview.

## Feature documentation (read on demand)

Do **not** preload the whole `docs/` tree for routine tasks. Skim the feature README trigger tables, then read **only** the matching file(s):

- **Game:** [`docs/game/features/README.md`](docs/game/features/README.md)
- **Technical:** [`docs/technical/features/README.md`](docs/technical/features/README.md)
- Error handling / testing: [`docs/technical/features/platform/error-handling.md`](docs/technical/features/platform/error-handling.md), [`docs/technical/features/platform/testing.md`](docs/technical/features/platform/testing.md).
