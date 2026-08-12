using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public readonly record struct ActorId(string Value)
{
    public override string ToString() => Value;
}

public sealed record Actor(
    ActorId Id,
    decimal Capacity,
    ImmutableDictionary<string, double> Stats);

public sealed record Assignment(ActorId ActorId, NodeId NodeId);
