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
dotnet run --project src/MarlothStrategy
```

Solution: [`MarlothStrategy.sln`](MarlothStrategy.sln).

## Conventions

- Use **Unix (LF)** line endings ([`.gitattributes`](.gitattributes), [`.editorconfig`](.editorconfig); Dev Container sets `files.eol` to `\n`).
- Prefer changing game logic in this repo’s console prototype until a Godot client exists.
- Design/docs-as-source-of-truth can be added later; there is no `docs/` tree yet.
