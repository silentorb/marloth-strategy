# Marloth Strategy technical design

## High-level requirements

- **Marloth Strategy** is a single-player turn-based strategy game. The **current** surface is a C# ASCII console prototype; a **Godot** client is eventual, not present yet.
- C# is the primary programming language (.NET 8).
- Heavily requirements-driven, documented under the `./docs` directory. **`./docs` is the source of truth for functionality**: high-level vision in [`docs/game/game-design.md`](../game/game-design.md) (agents must not edit that file without explicit user instruction); secondary game rules under [`docs/game/features/`](../game/features/); architecture and contracts under [`docs/technical/`](.). Code and tests implement those documents. When behavior changes, update the docs in the same change (or first). If code and docs disagree, docs win and code is fixed.
- Heavily test-driven when a suite exists. Tests verify **documented** requirements (values and rules stated in feature docs), not undocumented code quirks. User-reported gaps that the suite missed get a regression test when a sound one exists at the lowest practical layer; otherwise escalate rather than adding brittle or flaky coverage (see [features/platform/testing.md](features/platform/testing.md) **Bug regressions / debugging**).
- Prefer **explicit error outcomes** for expected failures; use **exceptions** only for truly exceptional cases or documented fail-fast abort boundaries (see [features/platform/error-handling.md](features/platform/error-handling.md)).
- Prefer changing game logic in this repo’s **console prototype** until a Godot client exists.

## Repository layout (current)

| Path | Purpose |
|------|---------|
| [`src/MarlothStrategy.Simulation/`](../../src/MarlothStrategy.Simulation/) | Authoritative game logic/state; shared by console and (later) Godot surfaces. No console I/O. Hosts host-supplied config types such as `GameConfig`. |
| [`src/MarlothStrategy.Console.Client/`](../../src/MarlothStrategy.Console.Client/) | ASCII presentation / input; depends on Simulation |
| [`src/MarlothStrategy.Console.App/`](../../src/MarlothStrategy.Console.App/) | Thin console host: build config, call Client. Not game rules |
| [`MarlothStrategy.sln`](../../MarlothStrategy.sln) | Solution |
| [`Directory.Build.props`](../../Directory.Build.props) | Shared MSBuild defaults (`net8.0`, nullable, implicit usings) |
| [`docs/`](../) | Design and technical source of truth |
| [`tests/`](../../tests/) | Automated tests (starts with Simulation unit tests) |

**Dependencies:** Console.App → Console.Client → Simulation; Console.App also references Simulation so it can construct `GameConfig` and pass it into Client. A future Godot App/Client will reuse Simulation the same way.

Godot-oriented directory layout (assets, scenes, ui, etc.) will be documented here when that surface exists.
