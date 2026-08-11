using System.Collections.Immutable;

namespace MarlothStrategy.Simulation.Graph;

public sealed record NodeType(
    NodeTypeId Id,
    ImmutableDictionary<PortId, Port> Inputs,
    ImmutableDictionary<PortId, Port> Outputs);
