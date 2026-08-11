using System.Collections.Immutable;

namespace MarlothStrategy.Simulation.Graph;

public sealed record NodeGraph(
    ImmutableDictionary<NodeId, Node> Nodes,
    ImmutableDictionary<EdgeId, Edge> Edges);
