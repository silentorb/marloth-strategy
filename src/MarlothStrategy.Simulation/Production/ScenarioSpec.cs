using System.Collections.Immutable;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Resolved scenario roster and graph variation before materializing <see cref="GameState"/>.
/// </summary>
public sealed record ScenarioSpec(
    bool IncludeTesting,
    ImmutableArray<ActorId> ActorIds,
    ImmutableArray<Assignment> Assignments,
    bool IncludeDesign = false);
