using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public readonly record struct ActorId(string Value)
{
    public override string ToString() => Value;
}

public sealed record Actor(ActorId Id, decimal Capacity);

public sealed record Assignment(ActorId ActorId, NodeId NodeId);
