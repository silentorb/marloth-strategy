using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public sealed record NodeTypeCatalog(ImmutableDictionary<NodeTypeId, NodeType> Types)
{
    public NodeType Get(NodeTypeId id) =>
        Types.TryGetValue(id, out var type)
            ? type
            : throw new InvalidOperationException($"Unknown node type '{id}'.");
}
