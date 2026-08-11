# Automated testing

## Status

The suite starts with **unit** tests on Simulation.

| Project | Layer | Framework |
|---------|-------|-----------|
| [`tests/MarlothStrategy.Simulation.Tests/`](../../../../tests/MarlothStrategy.Simulation.Tests/) | Unit | xUnit |

```bash
dotnet test
```

## Intended layers

| Layer | Purpose |
|-------|---------|
| **Unit** | Fast, engine-agnostic tests of pure logic and small APIs. |
| **Simulation functional** | Broader game-logic journeys without a Godot runtime (console / headless sim). |
| **Client** (later) | Godot or other presentation-layer tests when that surface exists. |

## Principles

- Tests verify **documented** requirements ([technical design](../../technical-design.md), feature docs), not undocumented code quirks.
- Prefer the **lowest layer** that can express the behavior under test.
- Keep unit and simulation tests runnable with `dotnet test` only where possible.

## Bug regressions / debugging

Debugging should be as test-driven as practical. When a **user-reported bug** was not caught by the suite, a regression test is part of the fix—unless a sound test is not available.

### Workflow

1. Confirm whether an existing test **should** have failed for this bug.
2. If not, add a **failing test first** at the **lowest layer** that can reproduce it (unit → simulation functional → client, as those layers exist).
3. Fix the product code; keep the test green.
4. Assert **documented** requirements. If the report itself defines missing behavior, update the docs in the same change and test against that.

### Escalate instead of a bad test

Stop and discuss with the user (do **not** quietly ship weak coverage) when a reproduction would require:

- Hacking or expanding general testing harnesses beyond the fix
- Likely **brittle** assertions
- Likely **flaky**, **hanging**, or **non-deterministic** behavior
- Deliberately breaking the environment in ways happy-path automation does not support cleanly

Then the user can choose: skip the test for this bug, invest in harness work, or redesign code for testability.

If there is still no test suite, reproduce manually, fix, and note the gap; do not invent a brittle harness solely for the bug unless asked.

Agent rule: [`.cursor/rules/bug-regression-tests.mdc`](../../../../.cursor/rules/bug-regression-tests.mdc).

## Related docs

| Topic | Document |
|-------|----------|
| Docs-as-SoT, layout | [Technical design](../../technical-design.md) |
| Gameplay vision (not test mechanics) | [Game design](../../../game/game-design.md) |
