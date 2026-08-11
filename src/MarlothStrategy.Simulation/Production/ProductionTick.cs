using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public static class ProductionTick
{
    public const decimal BaseThroughput = 2m;

    public static GameState AdvanceTick(GameState state) =>
        AdvanceTick(state, OrderNodes(state.Graph.Nodes.Keys));

    /// <summary>
    /// Advances one production tick. <paramref name="nodeOrder"/> only affects iteration order;
    /// results must be identical for any permutation of the same nodes.
    /// </summary>
    public static GameState AdvanceTick(GameState state, IEnumerable<NodeId> nodeOrder)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nodeOrder);

        var effortByNode = ResolveEffortByNode(state);
        var resolvedInputs = ResolveInputs(state, nodeOrder);
        var (residuals, outputs) = ComputeOutputs(state, nodeOrder, resolvedInputs, effortByNode);
        var nextSignals = CommitSignals(state, residuals, outputs);

        return state with
        {
            PortSignals = nextSignals,
            Tick = state.Tick + 1,
        };
    }

    public static ImmutableDictionary<NodeId, decimal> ResolveEffortByNode(GameState state)
    {
        var assignmentCounts = new Dictionary<ActorId, int>();
        foreach (var assignment in state.Assignments)
        {
            assignmentCounts[assignment.ActorId] =
                assignmentCounts.GetValueOrDefault(assignment.ActorId) + 1;
        }

        var effort = new Dictionary<NodeId, decimal>();
        foreach (var assignment in state.Assignments)
        {
            if (!state.Actors.TryGetValue(assignment.ActorId, out var actor))
            {
                throw new InvalidOperationException(
                    $"Assignment references unknown actor '{assignment.ActorId}'.");
            }

            var count = assignmentCounts[assignment.ActorId];
            if (count <= 0)
            {
                throw new InvalidOperationException(
                    $"Actor '{assignment.ActorId}' has non-positive assignment count.");
            }

            var share = actor.Capacity / count;
            effort[assignment.NodeId] = effort.GetValueOrDefault(assignment.NodeId) + share;
        }

        return effort.ToImmutableDictionary();
    }

    private static ImmutableDictionary<PortKey, SignalValue> ResolveInputs(
        GameState state,
        IEnumerable<NodeId> nodeOrder)
    {
        var resolved = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();
        foreach (var nodeId in nodeOrder)
        {
            if (!state.Graph.Nodes.TryGetValue(nodeId, out var node))
            {
                throw new InvalidOperationException($"Unknown node '{nodeId}' in tick order.");
            }

            var nodeType = state.Catalog.Get(node.Type);
            foreach (var portId in nodeType.Inputs.Keys)
            {
                var key = new PortKey(nodeId, portId);
                if (state.PortSignals.TryGetValue(key, out var value))
                {
                    resolved[key] = value;
                }
            }
        }

        return resolved.ToImmutable();
    }

    private static (
        ImmutableDictionary<PortKey, SignalValue> Residuals,
        ImmutableDictionary<PortKey, SignalValue> Outputs) ComputeOutputs(
        GameState state,
        IEnumerable<NodeId> nodeOrder,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableDictionary<NodeId, decimal> effortByNode)
    {
        var residuals = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();
        var outputs = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();

        foreach (var nodeId in nodeOrder)
        {
            var node = state.Graph.Nodes[nodeId];
            var nodeType = state.Catalog.Get(node.Type);
            var effort = effortByNode.GetValueOrDefault(nodeId, 0m);

            if (nodeType.Inputs.Count != 1 || nodeType.Outputs.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Node type '{nodeType.Id}' must have exactly one input and one output in v1.");
            }

            var inputPort = nodeType.Inputs.Values.Single();
            var outputPort = nodeType.Outputs.Values.Single();
            var inputKey = new PortKey(nodeId, inputPort.Id);
            var outputKey = new PortKey(nodeId, outputPort.Id);

            var available = resolvedInputs.TryGetValue(inputKey, out var inputValue)
                ? inputValue.Quantity
                : 0m;

            var processed = Math.Min(available, BaseThroughput * effort);
            if (processed < 0m)
            {
                throw new InvalidOperationException("Processed amount must not be negative.");
            }

            var residualAmount = available - processed;
            residuals[inputKey] = CreateSignal(inputPort.Type.Id, residualAmount);
            outputs[outputKey] = CreateSignal(outputPort.Type.Id, processed);
        }

        return (residuals.ToImmutable(), outputs.ToImmutable());
    }

    private static ImmutableDictionary<PortKey, SignalValue> CommitSignals(
        GameState state,
        ImmutableDictionary<PortKey, SignalValue> residuals,
        ImmutableDictionary<PortKey, SignalValue> outputs)
    {
        var next = residuals.ToBuilder();

        foreach (var edge in state.Graph.Edges.Values)
        {
            var fromKey = new PortKey(edge.From.Node, edge.From.Port);
            if (!outputs.TryGetValue(fromKey, out var produced) || produced.Quantity == 0m)
            {
                continue;
            }

            var toKey = new PortKey(edge.To.Node, edge.To.Port);
            if (next.TryGetValue(toKey, out var existing))
            {
                if (existing.TypeId != produced.TypeId)
                {
                    throw new InvalidOperationException(
                        $"Signal type mismatch routing {fromKey} -> {toKey}: " +
                        $"{produced.TypeId} vs {existing.TypeId}.");
                }

                next[toKey] = existing.AddQuantity(produced.Quantity);
            }
            else
            {
                // Destination port type is validated against catalog when present.
                if (state.Graph.Nodes.TryGetValue(toKey.Node, out var toNode))
                {
                    var toType = state.Catalog.Get(toNode.Type);
                    if (toType.Inputs.TryGetValue(toKey.Port, out var toPort) &&
                        toPort.Type.Id != produced.TypeId)
                    {
                        throw new InvalidOperationException(
                            $"Edge routes {produced.TypeId} into port typed {toPort.Type.Id}.");
                    }
                }

                next[toKey] = produced;
            }
        }

        return next.ToImmutable();
    }

    private static SignalValue CreateSignal(SignalTypeId typeId, decimal amount)
    {
        if (typeId == SignalTypes.Money)
        {
            return new SignalValue.Money(amount);
        }

        if (typeId == SignalTypes.Enchantments)
        {
            return new SignalValue.Enchantments(amount);
        }

        throw new InvalidOperationException($"Unsupported signal type '{typeId}'.");
    }

    private static IReadOnlyList<NodeId> OrderNodes(IEnumerable<NodeId> nodeIds) =>
        nodeIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
}
