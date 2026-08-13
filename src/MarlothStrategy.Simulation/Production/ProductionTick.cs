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
        var computed = ComputeOutputs(
            state,
            reportOrder,
            resolvedInputs,
            effortByNode);
        var (nextSignals, pendingAfterCommit) = CommitSignals(
            state,
            computed.Residuals,
            computed.Outputs,
            computed.PendingMoves);
        var nextTimers = AdvancePayrollTimer(state, computed.NextTimers);

        var nextState = state with
        {
            PortSignals = nextSignals,
            NodeProgress = computed.NextProgress,
            NodeTimers = nextTimers,
            PendingMoneyMoves = pendingAfterCommit,
            Actors = computed.NextActors,
            Assignments = computed.NextAssignments,
            Tick = state.Tick + 1,
        };

        return new ProductionTickResult(nextState, computed.Rows);
    }

    /// <summary>
    /// Splits each actor's capacity across preferred assignments whose prerequisites are met.
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
            if (MeetsPrerequisite(state, assignment.NodeId, resolvedInputs))
            {
                effective.Add(assignment);
            }
        }

        var byActor = effective.GroupBy(a => a.ActorId);
        var shares = new List<(Assignment, decimal)>();
        foreach (var group in byActor)
        {
            if (!state.Actors.TryGetValue(group.Key, out var actor))
            {
                throw new InvalidOperationException(
                    $"Assignment references unknown actor '{group.Key}'.");
            }

            var count = group.Count();
            var share = actor.Capacity / count;
            foreach (var assignment in group)
            {
                shares.Add((assignment, share));
            }
        }

        return shares;
    }

    private static int CountEffectiveActorsOnNode(
        GameState state,
        NodeId nodeId,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs)
    {
        return ResolveActorShares(state, resolvedInputs)
            .Where(s => s.Assignment.NodeId == nodeId && s.Share > 0m)
            .Select(s => s.Assignment.ActorId)
            .Distinct()
            .Count();
    }

    private static double ProgressGain(
        GameState state,
        NodeId nodeId,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        string statKey,
        double statDefault)
    {
        var gain = 0.0;
        foreach (var (assignment, share) in ResolveActorShares(state, resolvedInputs))
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

    private static bool MeetsPrerequisite(
        GameState state,
        NodeId nodeId,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs)
    {
        if (!state.Graph.Nodes.TryGetValue(nodeId, out var node))
        {
            return false;
        }

        if (node.Type == MagicAgencySeed.EnchantTypeId ||
            node.Type == MagicAgencySeed.TestingTypeId ||
            node.Type == MagicAgencySeed.SellTypeId)
        {
            var key = new PortKey(nodeId, MagicAgencySeed.EnchantmentPortId);
            return resolvedInputs.TryGetValue(key, out var value) && value is SignalValue.Enchantment;
        }

        if (node.Type == MagicAgencySeed.TreasuryTypeId)
        {
            return !state.PendingMoneyMoves.IsEmpty;
        }

        if (node.Type == MagicAgencySeed.PayrollTypeId)
        {
            return state.NodeTimers.GetValueOrDefault(nodeId, 0) == 0;
        }

        return false;
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

    private sealed record ComputeOutputsResult(
        ImmutableDictionary<PortKey, SignalValue> Residuals,
        ImmutableDictionary<PortKey, SignalValue> Outputs,
        ImmutableArray<NodeIoRow> Rows,
        ImmutableDictionary<NodeId, double> NextProgress,
        ImmutableDictionary<NodeId, int> NextTimers,
        ImmutableArray<PendingMoneyMove> PendingMoves,
        ImmutableDictionary<ActorId, Actor> NextActors,
        ImmutableArray<Assignment> NextAssignments);

    private static ComputeOutputsResult ComputeOutputs(
        GameState state,
        IReadOnlyList<NodeId> reportOrder,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableDictionary<NodeId, decimal> effortByNode)
    {
        var residuals = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();
        var outputs = ImmutableDictionary.CreateBuilder<PortKey, SignalValue>();
        var nextProgress = ImmutableDictionary.CreateBuilder<NodeId, double>();
        var rowByNode = new Dictionary<NodeId, NodeIoRow>();
        // Treasury drains only the start-of-tick queue; same-tick enqueues append after.
        var remainingStartPending = state.PendingMoneyMoves;
        var appendedPending = ImmutableArray<PendingMoneyMove>.Empty;
        var nextActors = state.Actors;
        var nextAssignments = state.Assignments;
        var nextTimers = state.NodeTimers;

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
            else if (node.Type == MagicAgencySeed.TestingTypeId)
            {
                var applied = ApplyTesting(
                    state,
                    nodeId,
                    state.Catalog.Get(node.Type),
                    state.NodeConfigs.Testing,
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
            else if (node.Type == MagicAgencySeed.TreasuryTypeId)
            {
                var applied = ApplyTreasury(
                    state,
                    nodeId,
                    state.NodeConfigs.Treasury,
                    effort,
                    progress,
                    resolvedInputs,
                    residuals,
                    outputs,
                    remainingStartPending,
                    nextActors,
                    nextAssignments);
                nextProgress[nodeId] = applied.Progress;
                remainingStartPending = applied.PendingMoves;
                nextActors = applied.Actors;
                nextAssignments = applied.Assignments;
            }
            else if (node.Type == MagicAgencySeed.PayrollTypeId)
            {
                var applied = ApplyPayroll(
                    state,
                    nodeId,
                    state.NodeConfigs.Payroll,
                    effort,
                    progress,
                    resolvedInputs,
                    appendedPending,
                    nextTimers);
                nextProgress[nodeId] = applied.Progress;
                appendedPending = applied.PendingMoves;
                nextTimers = applied.Timers;
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

        var pending = remainingStartPending.AddRange(appendedPending);
        return new ComputeOutputsResult(
            residuals.ToImmutable(),
            outputs.ToImmutable(),
            rows.ToImmutable(),
            nextProgress.ToImmutable(),
            nextTimers,
            pending,
            nextActors,
            nextAssignments);
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

        var nextProgress = progress + ProgressGain(
            state,
            nodeId,
            resolvedInputs,
            ActorStatKeys.Enchanting,
            ActorStatKeys.DefaultEnchanting);

        var current = starting;
        while (true)
        {
            var required = config.Effort + current.Darkness;
            if (required <= 0 || nextProgress < required)
            {
                break;
            }

            nextProgress -= required;
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

    private static AppliedDraft ApplyTesting(
        GameState state,
        NodeId nodeId,
        NodeType nodeType,
        TestingNodeConfig config,
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
                $"Node type '{nodeType.Id}' does not match testing port layout.");
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

        var nextProgress = progress + ProgressGain(
            state,
            nodeId,
            resolvedInputs,
            ActorStatKeys.Testing,
            ActorStatKeys.DefaultTesting);

        var actorCount = CountEffectiveActorsOnNode(state, nodeId, resolvedInputs);
        var reductionPerApplication = config.FallacyReduction * actorCount;
        var current = starting;
        while (config.Effort > 0 && nextProgress >= config.Effort)
        {
            nextProgress -= config.Effort;
            current = current.ReduceFallacy(reductionPerApplication);
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

    private sealed record TreasuryApplied(
        double Progress,
        ImmutableArray<PendingMoneyMove> PendingMoves,
        ImmutableDictionary<ActorId, Actor> Actors,
        ImmutableArray<Assignment> Assignments);

    private static TreasuryApplied ApplyTreasury(
        GameState state,
        NodeId nodeId,
        TreasuryNodeConfig config,
        decimal assignmentEffort,
        double progress,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableDictionary<PortKey, SignalValue>.Builder residuals,
        ImmutableDictionary<PortKey, SignalValue>.Builder outputs,
        ImmutableArray<PendingMoneyMove> pending,
        ImmutableDictionary<ActorId, Actor> actors,
        ImmutableArray<Assignment> assignments)
    {
        var moneyKey = new PortKey(nodeId, MagicAgencySeed.MoneyPortId);
        var pileAmount = 0.0;
        if (resolvedInputs.TryGetValue(moneyKey, out var money) && money is SignalValue.Money stock)
        {
            pileAmount = stock.Amount;
        }

        // Start-of-tick queue only; new enqueues this tick are appended after drain.
        var startQueue = pending;
        var nextPending = ImmutableArray<PendingMoneyMove>.Empty;
        var nextProgress = progress;
        var nextActors = actors;
        var nextAssignments = assignments;
        var emittedOut = 0.0;

        if (assignmentEffort > 0m && !startQueue.IsEmpty)
        {
            nextProgress += ProgressGain(
                state,
                nodeId,
                resolvedInputs,
                ActorStatKeys.Treasury,
                ActorStatKeys.DefaultTreasury);

            var queue = startQueue;
            while (config.Effort > 0 && nextProgress >= config.Effort && !queue.IsEmpty)
            {
                nextProgress -= config.Effort;
                var move = queue[0];
                queue = queue.RemoveAt(0);

                if (move.Direction == MoneyMoveDirection.In)
                {
                    pileAmount += move.Amount;
                }
                else
                {
                    if (pileAmount >= move.Amount)
                    {
                        pileAmount -= move.Amount;
                        emittedOut += move.Amount;
                    }
                    else
                    {
                        nextActors = ImmutableDictionary<ActorId, Actor>.Empty;
                        nextAssignments = ImmutableArray<Assignment>.Empty;
                    }
                }
            }

            nextPending = queue;
        }
        else
        {
            nextPending = startQueue;
        }

        residuals[moneyKey] = new SignalValue.Money(pileAmount);
        if (emittedOut > 0)
        {
            outputs[moneyKey] = new SignalValue.Money(emittedOut);
        }

        return new TreasuryApplied(nextProgress, nextPending, nextActors, nextAssignments);
    }

    private sealed record PayrollApplied(
        double Progress,
        ImmutableArray<PendingMoneyMove> PendingMoves,
        ImmutableDictionary<NodeId, int> Timers);

    private static PayrollApplied ApplyPayroll(
        GameState state,
        NodeId nodeId,
        PayrollNodeConfig config,
        decimal assignmentEffort,
        double progress,
        ImmutableDictionary<PortKey, SignalValue> resolvedInputs,
        ImmutableArray<PendingMoneyMove> appendedPending,
        ImmutableDictionary<NodeId, int> timers)
    {
        // Money on the payroll input is disbursed: never written to residuals.
        var remaining = state.NodeTimers.GetValueOrDefault(nodeId, config.Period);
        var nextProgress = progress;
        var nextPending = appendedPending;
        var nextTimers = timers;

        // Payday due only when start-of-tick remaining is 0.
        if (remaining == 0 && assignmentEffort > 0m)
        {
            nextProgress += ProgressGain(
                state,
                nodeId,
                resolvedInputs,
                ActorStatKeys.Payroll,
                ActorStatKeys.DefaultPayroll);

            if (config.Effort > 0 && nextProgress >= config.Effort)
            {
                nextProgress -= config.Effort;
                var wageTotal = 0.0;
                foreach (var actor in state.Actors.Values.OrderBy(a => a.Id.Value, StringComparer.Ordinal))
                {
                    wageTotal += EffectiveWage(actor, config);
                }

                if (wageTotal > 0)
                {
                    if (!HasTreasuryFundingEdge(state, nodeId))
                    {
                        throw new InvalidOperationException(
                            $"Payroll node '{nodeId.Value}' has wage total {wageTotal} but no funding edge " +
                            $"from a treasury money port to its money input.");
                    }

                    nextPending = nextPending.Add(new PendingMoneyMove(MoneyMoveDirection.Out, wageTotal));
                }

                nextTimers = nextTimers.SetItem(nodeId, config.Period);
            }
        }

        return new PayrollApplied(nextProgress, nextPending, nextTimers);
    }

    /// <summary>
    /// True when at least one edge routes from a treasury money port onto this payroll money input.
    /// </summary>
    private static bool HasTreasuryFundingEdge(GameState state, NodeId payrollNodeId)
    {
        foreach (var edge in state.Graph.Edges.Values)
        {
            if (edge.To.Node != payrollNodeId || edge.To.Port != MagicAgencySeed.MoneyPortId)
            {
                continue;
            }

            if (!state.Graph.Nodes.TryGetValue(edge.From.Node, out var fromNode))
            {
                continue;
            }

            if (fromNode.Type == MagicAgencySeed.TreasuryTypeId
                && edge.From.Port == MagicAgencySeed.MoneyPortId)
            {
                return true;
            }
        }

        return false;
    }

    private static (
        ImmutableDictionary<PortKey, SignalValue> NextSignals,
        ImmutableArray<PendingMoneyMove> NextPending)
        CommitSignals(
            GameState state,
            ImmutableDictionary<PortKey, SignalValue> residuals,
            ImmutableDictionary<PortKey, SignalValue> outputs,
            ImmutableArray<PendingMoneyMove> pending)
    {
        var next = residuals.ToBuilder();
        var nextPending = pending;

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

            if (routed is SignalValue.Money money
                && state.Graph.Nodes.TryGetValue(toKey.Node, out var toNode)
                && toNode.Type == MagicAgencySeed.TreasuryTypeId
                && toKey.Port == MagicAgencySeed.MoneyPortId)
            {
                nextPending = nextPending.Add(new PendingMoneyMove(MoneyMoveDirection.In, money.Amount));
                continue;
            }

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

        return (next.ToImmutable(), nextPending);
    }

    private static ImmutableDictionary<NodeId, int> AdvancePayrollTimer(
        GameState state,
        ImmutableDictionary<NodeId, int> timersAfterCompute)
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
        var period = state.NodeConfigs.Payroll.Period;
        if (period <= 0)
        {
            throw new InvalidOperationException("Payroll period must be a positive integer.");
        }

        var startRemaining = state.NodeTimers.GetValueOrDefault(payrollNodeId, period);
        if (startRemaining > 0)
        {
            // Countdown ticks without actors; payday reset (if any) is ignored when not due.
            return timersAfterCompute.SetItem(payrollNodeId, startRemaining - 1);
        }

        // Still due (0) or reset to period by payday application this tick.
        return timersAfterCompute;
    }

    private static void ValidateDestinationType(
        GameState state,
        PortKey toKey,
        SignalValue produced)
    {
        if (!state.Graph.Nodes.TryGetValue(toKey.Node, out var toNode))
        {
            throw new InvalidOperationException($"Unknown destination node '{toKey.Node}'.");
        }

        var nodeType = state.Catalog.Get(toNode.Type);
        if (!nodeType.Inputs.TryGetValue(toKey.Port, out var port))
        {
            throw new InvalidOperationException(
                $"Destination '{toKey}' is not an input port.");
        }

        if (port.Type.Id != produced.TypeId)
        {
            throw new InvalidOperationException(
                $"Signal type mismatch at '{toKey}': {produced.TypeId} vs {port.Type.Id}.");
        }
    }

    private static IReadOnlyList<NodeId> OrderNodes(IEnumerable<NodeId> nodes) =>
        nodes.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
}
