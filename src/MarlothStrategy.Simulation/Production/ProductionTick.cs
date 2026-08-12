using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public static class ProductionTick
{
    public static GameState AdvanceTick(GameState state) =>
        AdvanceTickWithReport(state).State;

    public static GameState AdvanceTick(GameState state, IEnumerable<NodeId> nodeOrder) =>
        AdvanceTickWithReport(state, nodeOrder).State;

    public static ProductionTickResult AdvanceTickWithReport(GameState state) =>
        AdvanceTickWithReport(state, OrderNodes(state.Graph.Nodes.Keys));

    /// <summary>
    /// Advances one production tick and returns per-node I/O. <paramref name="nodeOrder"/> only
    /// affects iteration order; results must be identical for any permutation of the same nodes.
    /// </summary>
    public static ProductionTickResult AdvanceTickWithReport(
        GameState state,
        IEnumerable<NodeId> nodeOrder)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nodeOrder);

        var orderedNodes = nodeOrder as IReadOnlyList<NodeId> ?? nodeOrder.ToArray();
        var effortByNode = ResolveEffortByNode(state);
        var resolvedInputs = ResolveInputs(state, orderedNodes);
        var (residuals, outputs, rows) = ComputeOutputs(
            state,
            orderedNodes,
            resolvedInputs,
            effortByNode);
        var nextSignals = CommitSignals(state, residuals, outputs);

        var nextState = state with
        {
            PortSignals = nextSignals,
            Tick = state.Tick + 1,
        };

        return new ProductionTickResult(nextState, rows);
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
        ImmutableDictionary<PortKey, SignalValue> Outputs,
        ImmutableArray<NodeIoRow> Rows) ComputeOutputs(
        GameState state,
        IEnumerable<NodeId> nodeOrder,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableDictionary<NodeId, decimal> effortByNode)
    {
        var residuals = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();
        var outputs = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();
        var rows = ImmutableArray.CreateBuilder<NodeIoRow>();

        foreach (var nodeId in nodeOrder)
        {
            var node = state.Graph.Nodes[nodeId];
            var nodeType = state.Catalog.Get(node.Type);
            var effort = effortByNode.GetValueOrDefault(nodeId, 0m);

            if (node.Type == MagicAgencySeed.EnchantTypeId)
            {
                var enchantConfig = state.NodeConfigs.Enchant;
                var limit = ProcessLimit(enchantConfig.BaseThroughput, effort);
                rows.Add(ComputeEnchant(
                    nodeId,
                    nodeType,
                    enchantConfig,
                    effort,
                    limit,
                    resolvedInputs,
                    residuals,
                    outputs));
            }
            else if (node.Type == MagicAgencySeed.SellTypeId)
            {
                var sellConfig = state.NodeConfigs.Sell;
                var limit = ProcessLimit(sellConfig.BaseThroughput, effort);
                rows.Add(ComputeSell(
                    nodeId,
                    nodeType,
                    sellConfig,
                    effort,
                    limit,
                    resolvedInputs,
                    residuals,
                    outputs));
            }
            else
            {
                throw new InvalidOperationException($"Unsupported node type '{node.Type}'.");
            }
        }

        return (residuals.ToImmutable(), outputs.ToImmutable(), rows.ToImmutable());
    }

    private static int ProcessLimit(double baseThroughput, decimal effort) =>
        (int)decimal.Floor((decimal)baseThroughput * effort);

    private static NodeIoRow ComputeEnchant(
        NodeId nodeId,
        NodeType nodeType,
        EnchantNodeConfig config,
        decimal effort,
        int limit,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableDictionary<PortKey, SignalValue>.Builder residuals,
        ImmutableDictionary<PortKey, SignalValue>.Builder outputs)
    {
        var enchantmentPort = MagicAgencySeed.EnchantmentPortId;
        var moneyPort = MagicAgencySeed.MoneyPortId;
        var enchantmentKey = new PortKey(nodeId, enchantmentPort);
        var moneyKey = new PortKey(nodeId, moneyPort);
        var outputKey = new PortKey(nodeId, enchantmentPort);

        if (!nodeType.Inputs.ContainsKey(enchantmentPort) ||
            !nodeType.Inputs.ContainsKey(moneyPort) ||
            !nodeType.Outputs.ContainsKey(enchantmentPort))
        {
            throw new InvalidOperationException(
                $"Node type '{nodeType.Id}' does not match enchant port layout.");
        }

        // Money is treasury only — never consumed.
        if (resolvedInputs.TryGetValue(moneyKey, out var money))
        {
            residuals[moneyKey] = money;
        }

        resolvedInputs.TryGetValue(enchantmentKey, out var available);
        var canRun = limit >= 1 && available is SignalValue.Enchantment;
        SignalValue? produced = null;
        SignalValue? residual = available;

        if (canRun && available is SignalValue.Enchantment enchantment)
        {
            produced = enchantment.Mutate(config);
            residual = null;
            outputs[outputKey] = produced;
        }

        if (residual is not null)
        {
            residuals[enchantmentKey] = residual;
        }

        return new NodeIoRow(
            nodeId,
            effort,
            enchantmentPort,
            SignalTypes.Enchantment,
            available,
            Consumed: canRun,
            residual,
            enchantmentPort,
            SignalTypes.Enchantment,
            produced);
    }

    private static NodeIoRow ComputeSell(
        NodeId nodeId,
        NodeType nodeType,
        SellNodeConfig config,
        decimal effort,
        int limit,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableDictionary<PortKey, SignalValue>.Builder residuals,
        ImmutableDictionary<PortKey, SignalValue>.Builder outputs)
    {
        var enchantmentPort = MagicAgencySeed.EnchantmentPortId;
        var moneyPort = MagicAgencySeed.MoneyPortId;
        var inputKey = new PortKey(nodeId, enchantmentPort);
        var outputKey = new PortKey(nodeId, moneyPort);

        if (!nodeType.Inputs.ContainsKey(enchantmentPort) ||
            !nodeType.Outputs.ContainsKey(moneyPort))
        {
            throw new InvalidOperationException(
                $"Node type '{nodeType.Id}' does not match sell port layout.");
        }

        resolvedInputs.TryGetValue(inputKey, out var available);
        var canRun = limit >= 1 && available is SignalValue.Enchantment;
        SignalValue? produced = null;
        SignalValue? residual = available;

        if (canRun && available is SignalValue.Enchantment enchantment)
        {
            produced = new SignalValue.Money(enchantment.SellPayout(config));
            residual = null;
            outputs[outputKey] = produced;
        }

        if (residual is not null)
        {
            residuals[inputKey] = residual;
        }

        return new NodeIoRow(
            nodeId,
            effort,
            enchantmentPort,
            SignalTypes.Enchantment,
            available,
            Consumed: canRun,
            residual,
            moneyPort,
            SignalTypes.Money,
            produced);
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
            if (!outputs.TryGetValue(fromKey, out var produced))
            {
                continue;
            }

            if (produced is SignalValue.Money { Amount: 0 })
            {
                continue;
            }

            var routed = produced.Copy();
            var toKey = new PortKey(edge.To.Node, edge.To.Port);
            ValidateDestinationType(state, toKey, routed);

            if (next.TryGetValue(toKey, out var existing))
            {
                if (existing.TypeId != routed.TypeId)
                {
                    throw new InvalidOperationException(
                        $"Signal type mismatch routing {fromKey} -> {toKey}: " +
                        $"{routed.TypeId} vs {existing.TypeId}.");
                }

                next[toKey] = routed.Kind switch
                {
                    SignalKind.Resource => existing.AddResource(routed),
                    SignalKind.Information => throw new InvalidOperationException(
                        $"Cannot merge information signals on port {toKey}."),
                    _ => throw new InvalidOperationException(
                        $"Unknown signal kind '{routed.Kind}'."),
                };
            }
            else
            {
                next[toKey] = routed;
            }
        }

        return next.ToImmutable();
    }

    private static void ValidateDestinationType(
        GameState state,
        PortKey toKey,
        SignalValue produced)
    {
        if (!state.Graph.Nodes.TryGetValue(toKey.Node, out var toNode))
        {
            return;
        }

        var toType = state.Catalog.Get(toNode.Type);
        if (toType.Inputs.TryGetValue(toKey.Port, out var toPort) &&
            toPort.Type.Id != produced.TypeId)
        {
            throw new InvalidOperationException(
                $"Edge routes {produced.TypeId} into port typed {toPort.Type.Id}.");
        }
    }

    private static IReadOnlyList<NodeId> OrderNodes(IEnumerable<NodeId> nodeIds) =>
        nodeIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
}
