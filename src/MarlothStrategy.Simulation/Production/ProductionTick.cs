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
    /// affects report row order; simulation results are independent of that permutation.
    /// </summary>
    public static ProductionTickResult AdvanceTickWithReport(
        GameState state,
        IEnumerable<NodeId> nodeOrder)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nodeOrder);

        var reportOrder = nodeOrder as IReadOnlyList<NodeId> ?? nodeOrder.ToArray();
        var resolvedInputs = ResolveInputs(state, OrderNodes(state.Graph.Nodes.Keys));
        var effortByNode = ResolveEffortByNode(state, resolvedInputs);
        var (residuals, outputs, rows, nextProgress, treasuryDebits) = ComputeOutputs(
            state,
            reportOrder,
            resolvedInputs,
            effortByNode);
        var nextSignals = CommitSignals(state, residuals, outputs, treasuryDebits);

        var nextState = state with
        {
            PortSignals = nextSignals,
            NodeProgress = nextProgress,
            Tick = state.Tick + 1,
        };

        return new ProductionTickResult(nextState, rows);
    }

    /// <summary>
    /// Splits each actor's capacity across preferred assignments whose process input has an
    /// enchantment (effective assignments).
    /// </summary>
    public static ImmutableDictionary<NodeId, decimal> ResolveEffortByNode(GameState state) =>
        ResolveEffortByNode(
            state,
            ResolveInputs(state, OrderNodes(state.Graph.Nodes.Keys)));

    public static ImmutableDictionary<NodeId, decimal> ResolveEffortByNode(
        GameState state,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(resolvedInputs);

        var shares = ResolveActorShares(state, resolvedInputs);
        var effort = new Dictionary<NodeId, decimal>();
        foreach (var (assignment, share) in shares)
        {
            effort[assignment.NodeId] = effort.GetValueOrDefault(assignment.NodeId) + share;
        }

        return effort.ToImmutableDictionary();
    }

    public static double GetStat(Actor actor, string key, double defaultValue)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.Stats.TryGetValue(key, out var value) ? value : defaultValue;
    }

    private static List<(Assignment Assignment, decimal Share)> ResolveActorShares(
        GameState state,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs)
    {
        var effective = new List<Assignment>();
        foreach (var assignment in state.Assignments)
        {
            if (HasEnchantmentPrerequisite(assignment.NodeId, resolvedInputs))
            {
                effective.Add(assignment);
            }
        }

        var counts = new Dictionary<ActorId, int>();
        foreach (var assignment in effective)
        {
            counts[assignment.ActorId] = counts.GetValueOrDefault(assignment.ActorId) + 1;
        }

        var shares = new List<(Assignment, decimal)>();
        foreach (var assignment in effective)
        {
            if (!state.Actors.TryGetValue(assignment.ActorId, out var actor))
            {
                throw new InvalidOperationException(
                    $"Assignment references unknown actor '{assignment.ActorId}'.");
            }

            var count = counts[assignment.ActorId];
            if (count <= 0)
            {
                throw new InvalidOperationException(
                    $"Actor '{assignment.ActorId}' has non-positive assignment count.");
            }

            shares.Add((assignment, actor.Capacity / count));
        }

        return shares;
    }

    private static double ProgressGain(
        GameState state,
        NodeId nodeId,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        string statKey,
        double statDefault)
    {
        var shares = ResolveActorShares(state, resolvedInputs);
        double gain = 0;
        foreach (var (assignment, share) in shares)
        {
            if (assignment.NodeId != nodeId)
            {
                continue;
            }

            if (!state.Actors.TryGetValue(assignment.ActorId, out var actor))
            {
                throw new InvalidOperationException(
                    $"Assignment references unknown actor '{assignment.ActorId}'.");
            }

            gain += GetStat(actor, statKey, statDefault) * (double)share;
        }

        return gain;
    }

    private static bool HasEnchantmentPrerequisite(
        NodeId nodeId,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs)
    {
        var key = new PortKey(nodeId, MagicAgencySeed.EnchantmentPortId);
        return resolvedInputs.TryGetValue(key, out var value) && value is SignalValue.Enchantment;
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
        ImmutableArray<NodeIoRow> Rows,
        ImmutableDictionary<NodeId, double> NextProgress,
        double TreasuryDebits) ComputeOutputs(
        GameState state,
        IReadOnlyList<NodeId> reportOrder,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableDictionary<NodeId, decimal> effortByNode)
    {
        var treasuryAvailable = 0.0;
        var treasuryKey = new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId);
        if (resolvedInputs.TryGetValue(treasuryKey, out var treasurySignal) &&
            treasurySignal is SignalValue.Money moneyStock)
        {
            treasuryAvailable = moneyStock.Amount;
        }

        var drafts = new Dictionary<NodeId, NodeDraft>();
        foreach (var nodeId in OrderNodes(state.Graph.Nodes.Keys))
        {
            var node = state.Graph.Nodes[nodeId];
            var effort = effortByNode.GetValueOrDefault(nodeId, 0m);
            var progress = state.NodeProgress.GetValueOrDefault(nodeId, 0);

            if (node.Type == MagicAgencySeed.EnchantTypeId)
            {
                drafts[nodeId] = DraftEnchant(
                    state,
                    nodeId,
                    state.Catalog.Get(node.Type),
                    state.NodeConfigs.Enchant,
                    effort,
                    progress,
                    resolvedInputs);
            }
            else if (node.Type == MagicAgencySeed.SellTypeId)
            {
                drafts[nodeId] = DraftSell(
                    state,
                    nodeId,
                    state.Catalog.Get(node.Type),
                    state.NodeConfigs.Sell,
                    effort,
                    progress,
                    resolvedInputs);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported node type '{node.Type}'.");
            }
        }

        var remainingMoney = treasuryAvailable;
        var grantedByNode = new Dictionary<NodeId, int>();
        foreach (var nodeId in OrderNodes(drafts.Keys))
        {
            var draft = drafts[nodeId];
            var maxByMoney = draft.CostPerApplication <= 0
                ? draft.DesiredApplications
                : (int)Math.Floor(remainingMoney / draft.CostPerApplication);
            var granted = Math.Min(draft.DesiredApplications, Math.Max(0, maxByMoney));
            grantedByNode[nodeId] = granted;
            remainingMoney -= granted * draft.CostPerApplication;
        }

        var residuals = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();
        var outputs = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();
        var nextProgress = ImmutableDictionary.CreateBuilder<NodeId, double>();
        var rowByNode = new Dictionary<NodeId, NodeIoRow>();
        var treasuryDebits = 0.0;

        foreach (var nodeId in OrderNodes(drafts.Keys))
        {
            var draft = drafts[nodeId];
            var granted = grantedByNode[nodeId];
            var applied = ApplyDraft(draft, granted, state.NodeConfigs, residuals, outputs);
            rowByNode[nodeId] = applied.Row;
            nextProgress[nodeId] = applied.Progress;
            treasuryDebits += granted * draft.CostPerApplication;
        }

        foreach (var (id, value) in state.NodeProgress)
        {
            if (!nextProgress.ContainsKey(id))
            {
                nextProgress[id] = value;
            }
        }

        var rows = ImmutableArray.CreateBuilder<NodeIoRow>(reportOrder.Count);
        foreach (var nodeId in reportOrder)
        {
            rows.Add(rowByNode[nodeId]);
        }

        return (
            residuals.ToImmutable(),
            outputs.ToImmutable(),
            rows.ToImmutable(),
            nextProgress.ToImmutable(),
            treasuryDebits);
    }

    private sealed record NodeDraft(
        NodeId NodeId,
        bool IsEnchant,
        decimal AssignmentEffort,
        double ProgressAfterGain,
        double WorkEffort,
        double CostPerApplication,
        int DesiredApplications,
        SignalValue? Available,
        SignalValue? Money);

    private readonly record struct AppliedDraft(NodeIoRow Row, double Progress);

    private static NodeDraft DraftEnchant(
        GameState state,
        NodeId nodeId,
        NodeType nodeType,
        EnchantNodeConfig config,
        decimal assignmentEffort,
        double progress,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs)
    {
        var enchantmentPort = MagicAgencySeed.EnchantmentPortId;
        var moneyPort = MagicAgencySeed.MoneyPortId;

        if (!nodeType.Inputs.ContainsKey(enchantmentPort) ||
            !nodeType.Inputs.ContainsKey(moneyPort) ||
            !nodeType.Outputs.ContainsKey(enchantmentPort))
        {
            throw new InvalidOperationException(
                $"Node type '{nodeType.Id}' does not match enchant port layout.");
        }

        resolvedInputs.TryGetValue(new PortKey(nodeId, moneyPort), out var money);
        resolvedInputs.TryGetValue(new PortKey(nodeId, enchantmentPort), out var available);

        if (assignmentEffort <= 0m || available is not SignalValue.Enchantment)
        {
            return new NodeDraft(
                nodeId,
                IsEnchant: true,
                assignmentEffort,
                progress,
                config.Effort,
                config.Cost,
                DesiredApplications: 0,
                available,
                money);
        }

        var progressAfterGain = progress + ProgressGain(
            state,
            nodeId,
            resolvedInputs,
            ActorStatKeys.Enchanting,
            ActorStatKeys.DefaultEnchanting);

        var desired = config.Effort <= 0
            ? 0
            : (int)Math.Floor(progressAfterGain / config.Effort);

        return new NodeDraft(
            nodeId,
            IsEnchant: true,
            assignmentEffort,
            progressAfterGain,
            config.Effort,
            config.Cost,
            desired,
            available,
            money);
    }

    private static NodeDraft DraftSell(
        GameState state,
        NodeId nodeId,
        NodeType nodeType,
        SellNodeConfig config,
        decimal assignmentEffort,
        double progress,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs)
    {
        var enchantmentPort = MagicAgencySeed.EnchantmentPortId;
        var moneyPort = MagicAgencySeed.MoneyPortId;

        if (!nodeType.Inputs.ContainsKey(enchantmentPort) ||
            !nodeType.Outputs.ContainsKey(moneyPort))
        {
            throw new InvalidOperationException(
                $"Node type '{nodeType.Id}' does not match sell port layout.");
        }

        resolvedInputs.TryGetValue(new PortKey(nodeId, enchantmentPort), out var available);

        var progressAfterGain = progress;
        if (assignmentEffort > 0m && available is SignalValue.Enchantment)
        {
            progressAfterGain += ProgressGain(
                state,
                nodeId,
                resolvedInputs,
                ActorStatKeys.Sales,
                ActorStatKeys.DefaultSales);
        }

        var desired = assignmentEffort > 0m
            && available is SignalValue.Enchantment
            && config.Effort > 0
            && progressAfterGain >= config.Effort
            ? 1
            : 0;

        return new NodeDraft(
            nodeId,
            IsEnchant: false,
            assignmentEffort,
            progressAfterGain,
            config.Effort,
            config.Cost,
            desired,
            available,
            Money: null);
    }

    private static AppliedDraft ApplyDraft(
        NodeDraft draft,
        int granted,
        NodeTypeConfigs configs,
        ImmutableDictionary<PortKey, SignalValue>.Builder residuals,
        ImmutableDictionary<PortKey, SignalValue>.Builder outputs)
    {
        var enchantmentPort = MagicAgencySeed.EnchantmentPortId;
        var moneyPort = MagicAgencySeed.MoneyPortId;
        var progress = draft.ProgressAfterGain - (granted * draft.WorkEffort);

        if (draft.IsEnchant)
        {
            return ApplyEnchant(draft, granted, configs.Enchant, progress, residuals, outputs);
        }

        return ApplySell(draft, granted, configs.Sell, progress, residuals, outputs);
    }

    private static AppliedDraft ApplyEnchant(
        NodeDraft draft,
        int granted,
        EnchantNodeConfig config,
        double progress,
        ImmutableDictionary<PortKey, SignalValue>.Builder residuals,
        ImmutableDictionary<PortKey, SignalValue>.Builder outputs)
    {
        var enchantmentPort = MagicAgencySeed.EnchantmentPortId;
        var moneyPort = MagicAgencySeed.MoneyPortId;
        var enchantmentKey = new PortKey(draft.NodeId, enchantmentPort);
        var moneyKey = new PortKey(draft.NodeId, moneyPort);
        var outputKey = new PortKey(draft.NodeId, enchantmentPort);

        if (draft.AssignmentEffort <= 0m || draft.Available is not SignalValue.Enchantment starting)
        {
            if (draft.Money is not null)
            {
                residuals[moneyKey] = draft.Money;
            }

            if (draft.Available is not null)
            {
                residuals[enchantmentKey] = draft.Available;
            }

            return new AppliedDraft(
                new NodeIoRow(
                    draft.NodeId,
                    draft.AssignmentEffort,
                    enchantmentPort,
                    SignalTypes.Enchantment,
                    draft.Available,
                    Consumed: false,
                    draft.Available,
                    enchantmentPort,
                    SignalTypes.Enchantment,
                    Produced: null),
                draft.ProgressAfterGain);
        }

        var current = starting;
        for (var i = 0; i < granted; i++)
        {
            current = current.Mutate(config);
        }

        // Mutate or pass-through: consume and emit when assigned with input.
        outputs[outputKey] = current;
        if (draft.Money is not null)
        {
            residuals[moneyKey] = draft.Money;
        }

        return new AppliedDraft(
            new NodeIoRow(
                draft.NodeId,
                draft.AssignmentEffort,
                enchantmentPort,
                SignalTypes.Enchantment,
                draft.Available,
                Consumed: true,
                Residual: null,
                enchantmentPort,
                SignalTypes.Enchantment,
                current),
            progress);
    }

    private static AppliedDraft ApplySell(
        NodeDraft draft,
        int granted,
        SellNodeConfig config,
        double progress,
        ImmutableDictionary<PortKey, SignalValue>.Builder residuals,
        ImmutableDictionary<PortKey, SignalValue>.Builder outputs)
    {
        var enchantmentPort = MagicAgencySeed.EnchantmentPortId;
        var moneyPort = MagicAgencySeed.MoneyPortId;
        var inputKey = new PortKey(draft.NodeId, enchantmentPort);
        var outputKey = new PortKey(draft.NodeId, moneyPort);

        if (granted >= 1 && draft.Available is SignalValue.Enchantment toSell)
        {
            var produced = new SignalValue.Money(toSell.SellPayout(config));
            outputs[outputKey] = produced;
            return new AppliedDraft(
                new NodeIoRow(
                    draft.NodeId,
                    draft.AssignmentEffort,
                    enchantmentPort,
                    SignalTypes.Enchantment,
                    draft.Available,
                    Consumed: true,
                    Residual: null,
                    moneyPort,
                    SignalTypes.Money,
                    produced),
                progress);
        }

        if (draft.Available is not null)
        {
            residuals[inputKey] = draft.Available;
        }

        return new AppliedDraft(
            new NodeIoRow(
                draft.NodeId,
                draft.AssignmentEffort,
                enchantmentPort,
                SignalTypes.Enchantment,
                draft.Available,
                Consumed: false,
                draft.Available,
                moneyPort,
                SignalTypes.Money,
                Produced: null),
            draft.ProgressAfterGain);
    }

    private static ImmutableDictionary<PortKey, SignalValue> CommitSignals(
        GameState state,
        ImmutableDictionary<PortKey, SignalValue> residuals,
        ImmutableDictionary<PortKey, SignalValue> outputs,
        double treasuryDebits)
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

                switch (routed.Kind)
                {
                    case SignalKind.Resource:
                        next[toKey] = existing.AddResource(routed);
                        break;
                    case SignalKind.Information:
                        // Occupancy: destination still has stock — skip this edge's copy.
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unknown signal kind '{routed.Kind}'.");
                }
            }
            else
            {
                next[toKey] = routed;
            }
        }

        if (treasuryDebits != 0)
        {
            var treasuryKey = new PortKey(
                MagicAgencySeed.EnchantNodeId,
                MagicAgencySeed.MoneyPortId);
            if (next.TryGetValue(treasuryKey, out var stock) && stock is SignalValue.Money money)
            {
                next[treasuryKey] = money.WithAmount(money.Amount - treasuryDebits);
            }
            else if (treasuryDebits > 0)
            {
                throw new InvalidOperationException(
                    "Treasury debit requires money stock on enchant.money.");
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
