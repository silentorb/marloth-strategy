# Error handling

Cross-cutting standards for product and agent-authored code. Feature docs may still define specific fail-fast or abort contracts; this file is the shared policy for **how** to choose and wire failures.

## Principles

1. Prefer **explicit outcomes** (`Try*`, local Ok/Error result types, validated returns) for **expected** failure modes callers can or must handle.
2. Use **exceptions** for **truly exceptional** cases: programmer invariants (misused APIs), corrupted impossible state, or a documented **fail-fast abort** boundary where recovery is not defined.
3. Do **not** swallow failures (empty `catch`, silent defaults that look like success).
4. Do **not** leave half-initialized interactive state when multi-step setup fails.
5. For any path longer than a line or two: **choose and document** the failure mode (throw, return outcome, abort, soft no-op) before implementing; match the layer guidance below.

Do **not** introduce a shared `Result<T>` library unless a later design change explicitly adds one. When adding explicit flows, prefer local Ok/Error types or `Try*` APIs consistent with nearby code.

## Layer guidance (current)

| Layer | Guidance |
|-------|----------|
| **Console prototype / game logic** | Throw on API misuse and broken invariants. Prefer `Try*` / nullable / explicit outcomes for expected misses (lookups, invalid player commands). Prefer a **single catch/abort boundary** at the process entry for fatal boot or unrecoverable errors rather than scattering uncaught throws. |
| **Future Godot / client** | Soft early-return for missing **optional** nodes is fine. Required scene wiring should fail loudly at attach/boot time. Document abort boundaries when that surface exists. |

## Agent checklist

When adding or editing multi-step logic:

1. List failure points on the path.
2. Classify each as **expected** (explicit outcome) vs **exceptional / abort** (throw or single catch boundary).
3. Ensure callers (or the abort boundary) **see** the failure—no silent success lookalikes.
4. Cover the documented failure contract with tests when sound (see [testing.md](testing.md)).
5. Update the relevant feature doc if failure behavior is product-visible.

Agent rule: [`.cursor/rules/error-handling.mdc`](../../../../.cursor/rules/error-handling.mdc).
