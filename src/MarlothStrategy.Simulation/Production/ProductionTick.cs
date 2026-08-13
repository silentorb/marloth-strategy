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
        var (residuals, outputs, rows, nextProgress) = ComputeOutputs(
            state,
            reportOrder,
            resolvedInputs,
            effortByNode);
        var nextSignals = CommitSignals(state, residuals, outputs);
        var (nextTimers, nextActors, nextAssignments, finalSignals) = AdvancePayroll(
            state,
            nextSignals);

        var nextState = state with
        {
            PortSignals = finalSignals,
            NodeProgress = nextProgress,
            NodeTimers = nextTimers,
            Actors = nextActors,
            Assignments = nextAssignments,
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

    public static double EffectiveWage(Actor actor, PayrollNodeConfig payroll)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(payroll);
        return actor.Wage ?? payroll.DefaultWage;
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
        ImmutableDictionary<NodeId, double> NextProgress) ComputeOutputs(
        GameState state,
        IReadOnlyList<NodeId> reportOrder,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableDictionary<NodeId, decimal> effortByNode)
    {
        var residuals = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();
        var outputs = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();
        var nextProgress = ImmutableDictionary.CreateBuilder<NodeId, double>();
        var rowByNode = new Dictionary<NodeId, NodeIoRow>();

        // Residual treasury money so payroll/sell deposits build on the committed pile.
        foreach (var nodeId in OrderNodes(state.Graph.Nodes.Keys))
        {
            var node = state.Graph.Nodes[nodeId];
            if (node.Type != MagicAgencySeed.TreasuryTypeId)
            {
                continue;
            }

            var moneyKey = new PortKey(nodeId, MagicAgencySeed.MoneyPortId);
            if (resolvedInputs.TryGetValue(moneyKey, out var money) && money is not null)
            {
                residuals[moneyKey] = money;
            }
        }

        foreach (var nodeId in OrderNodes(state.Graph.Nodes.Keys))
        {
            var node = state.Graph.Nodes[nodeId];
            var effort = effortByNode.GetValueOrDefault(nodeId, 0m);
            var progress = state.NodeProgress.GetValueOrDefault(nodeId, 0);

            if (node.Type == MagicAgencySeed.EnchantTypeId)
            {
                var applied = ApplyEnchant(
                    state,
                    nodeId,
                    state.Catalog.Get(node.Type),
                    state.NodeConfigs.Enchant,
                    effort,
                    progress,
                    resolvedInputs,
                    residuals,
                    outputs);
                rowByNode[nodeId] = applied.Row;
                nextProgress[nodeId] = applied.Progress;
            }
            else if (node.Type == MagicAgencySeed.SellTypeId)
            {
                var applied = ApplySell(
                    state,
                    nodeId,
                    state.Catalog.Get(node.Type),
                    state.NodeConfigs.Sell,
                    effort,
                    progress,
                    resolvedInputs,
                    residuals,
                    outputs);
                rowByNode[nodeId] = applied.Row;
                nextProgress[nodeId] = applied.Progress;
            }
            else if (node.Type == MagicAgencySeed.TreasuryTypeId ||
                     node.Type == MagicAgencySeed.PayrollTypeId)
            {
                // No process I/O rows; treasury residual already applied; payroll is timer-driven.
            }
            else
            {
                throw new InvalidOperationException($"Unsupported node type '{node.Type}'.");
            }
        }

        foreach (var (id, value) in state.NodeProgress)
        {
            if (!nextProgress.ContainsKey(id))
            {
                nextProgress[id] = value;
            }
        }

        var rows = ImmutableArray.CreateBuilder<NodeIoRow>();
        foreach (var nodeId in reportOrder)
        {
            if (rowByNode.TryGetValue(nodeId, out var row))
            {
                rows.Add(row);
            }
        }

        return (
            residuals.ToImmutable(),
            outputs.ToImmutable(),
            rows.ToImmutable(),
            nextProgress.ToImmutable());
    }

    private readonly record struct AppliedDraft(NodeIoRow Row, double Progress);

    private static AppliedDraft ApplyEnchant(
        GameState state,
        NodeId nodeId,
        NodeType nodeType,
        EnchantNodeConfig config,
        decimal assignmentEffort,
        double progress,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableDictionary<PortKey, SignalValue>.Builder residuals,
        ImmutableDictionary<PortKey, SignalValue>.Builder outputs)
    {
        var enchantmentPort = MagicAgencySeed.EnchantmentPortId;

        if (!nodeType.Inputs.ContainsKey(enchantmentPort) ||
            !nodeType.Outputs.ContainsKey(enchantmentPort))
        {
            throw new InvalidOperationException(
                $"Node type '{nodeType.Id}' does not match enchant port layout.");
        }

        resolvedInputs.TryGetValue(new PortKey(nodeId, enchantmentPort), out var available);
        var enchantmentKey = new PortKey(nodeId, enchantmentPort);
        var outputKey = new PortKey(nodeId, enchantmentPort);

        if (assignmentEffort <= 0m || available is not SignalValue.Enchantment starting)
        {
            if (available is not null)
            {
                residuals[enchantmentKey] = available;
            }

            return new AppliedDraft(
                new NodeIoRow(
                    nodeId,
                    assignmentEffort,
                    enchantmentPort,
                    SignalTypes.Enchantment,
                    available,
                    Consumed: false,
                    available,
                    enchantmentPort,
                    SignalTypes.Enchantment,
                    Produced: null),
                progress);
        }

        var progressAfterGain = progress + ProgressGain(
            state,
            nodeId,
            resolvedInputs,
            ActorStatKeys.Enchanting,
            ActorStatKeys.DefaultEnchanting);

        var granted = config.Effort <= 0
            ? 0
            : (int)Math.Floor(progressAfterGain / config.Effort);
        var nextProgress = progressAfterGain - (granted * config.Effort);

        var current = starting;
        for (var i = 0; i < granted; i++)
        {
            current = current.Mutate(config);
        }

        outputs[outputKey] = current;

        return new AppliedDraft(
            new NodeIoRow(
                nodeId,
                assignmentEffort,
                enchantmentPort,
                SignalTypes.Enchantment,
                available,
                Consumed: true,
                Residual: null,
                enchantmentPort,
                SignalTypes.Enchantment,
                current),
            nextProgress);
    }

    private static AppliedDraft ApplySell(
        GameState state,
        NodeId nodeId,
        NodeType nodeType,
        SellNodeConfig config,
        decimal assignmentEffort,
        double progress,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableDictionary<PortKey, SignalValue>.Builder residuals,
        ImmutableDictionary<PortKey, SignalValue>.Builder outputs)
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
        var inputKey = new PortKey(nodeId, enchantmentPort);
        var moneyOutputKey = new PortKey(nodeId, moneyPort);

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

        var granted = assignmentEffort > 0m
            && available is SignalValue.Enchantment
            && config.Effort > 0
            && progressAfterGain >= config.Effort
            ? 1
            : 0;

        if (granted >= 1 && available is SignalValue.Enchantment toSell)
        {
            var produced = new SignalValue.Money(toSell.SellPayout(config));
            outputs[moneyOutputKey] = produced;
            return new AppliedDraft(
                new NodeIoRow(
                    nodeId,
                    assignmentEffort,
                    enchantmentPort,
                    SignalTypes.Enchantment,
                    available,
                    Consumed: true,
                    Residual: null,
                    moneyPort,
                    SignalTypes.Money,
                    produced),
                progressAfterGain - config.Effort);
        }

        if (available is not null)
        {
            residuals[inputKey] = available;
        }

        return new AppliedDraft(
            new NodeIoRow(
                nodeId,
                assignmentEffort,
                enchantmentPort,
                SignalTypes.Enchantment,
                available,
                Consumed: false,
                available,
                moneyPort,
                SignalTypes.Money,
                Produced: null),
            progressAfterGain);
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

        return next.ToImmutable();
    }

    private static (
        ImmutableDictionary<NodeId, int> NextTimers,
        ImmutableDictionary<ActorId, Actor> NextActors,
        ImmutableArray<Assignment> NextAssignments,
        ImmutableDictionary<PortKey, SignalValue> NextSignals)
        AdvancePayroll(
            GameState state,
            ImmutableDictionary<PortKey, SignalValue> signalsAfterCommit)
    {
        var payrollNodes = state.Graph.Nodes
            .Where(kv => kv.Value.Type == MagicAgencySeed.PayrollTypeId)
            .Select(kv => kv.Key)
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();
        var treasuryNodes = state.Graph.Nodes
            .Where(kv => kv.Value.Type == MagicAgencySeed.TreasuryTypeId)
            .Select(kv => kv.Key)
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        if (payrollNodes.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one payroll node, found {payrollNodes.Length}.");
        }

        if (treasuryNodes.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one treasury node, found {treasuryNodes.Length}.");
        }

        var payrollNodeId = payrollNodes[0];
        var treasuryNodeId = treasuryNodes[0];
        var period = state.NodeConfigs.Payroll.Period;
        if (period <= 0)
        {
            throw new InvalidOperationException("Payroll period must be a positive integer.");
        }

        var remaining = state.NodeTimers.GetValueOrDefault(payrollNodeId, period);
        remaining -= 1;

        var nextActors = state.Actors;
        var nextAssignments = state.Assignments;
        var nextSignals = signalsAfterCommit;
        var nextTimers = state.NodeTimers;

        if (remaining <= 0)
        {
            remaining = period;
            var wageTotal = 0.0;
            foreach (var actor in state.Actors.Values.OrderBy(a => a.Id.Value, StringComparer.Ordinal))
            {
                wageTotal += EffectiveWage(actor, state.NodeConfigs.Payroll);
            }

            if (wageTotal > 0)
            {
                var treasuryKey = new PortKey(treasuryNodeId, MagicAgencySeed.MoneyPortId);
                var treasuryAmount = 0.0;
                if (nextSignals.TryGetValue(treasuryKey, out var stock) &&
                    stock is SignalValue.Money money)
                {
                    treasuryAmount = money.Amount;
                }

                if (treasuryAmount >= wageTotal)
                {
                    nextSignals = nextSignals.SetItem(
                        treasuryKey,
                        new SignalValue.Money(treasuryAmount - wageTotal));
                }
                else
                {
                    nextActors = ImmutableDictionary<ActorId, Actor>.Empty;
                    nextAssignments = ImmutableArray<Assignment>.Empty;
                }
            }
        }

        nextTimers = nextTimers.SetItem(payrollNodeId, remaining);

        return (nextTimers, nextActors, nextAssignments, nextSignals);
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
