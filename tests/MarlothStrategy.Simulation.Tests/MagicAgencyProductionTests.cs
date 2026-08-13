using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Simulation.Tests;

public sealed class MagicAgencyProductionTests
{
    private static readonly NodeTypeConfigs DefaultConfigs = new(
        new EnchantNodeConfig(
            Effort: 10,
            VolumeDelta: 10,
            DarknessDelta: 1,
            FallacyConstant: 1),
        new TestingNodeConfig(Effort: 10, FallacyReduction: 5),
        new SellNodeConfig(Effort: 10, PayoutFloor: 0),
        new TreasuryNodeConfig(Effort: 2),
        new PayrollNodeConfig(DefaultWage: 10, Period: 5, Effort: 5));

    private static readonly ImmutableDictionary<ActorId, Actor> DefaultActors =
        ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty
                    .Add(ActorStatKeys.Enchanting, 10)
                    .Add(ActorStatKeys.Sales, 10)));

    private static GameState Seed(NodeTypeConfigs? configs = null) =>
        MagicAgencySeed.CreateInitialState(configs ?? DefaultConfigs, DefaultActors);

    [Fact]
    public void Seed_HasTestingBetweenEnchantAndSell_AndFullAssignments()
    {
        var state = Seed();

        Assert.Equal(0, state.Tick);
        Assert.Equal(5, state.Graph.Nodes.Count);
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.TestingNodeId));
        Assert.Empty(state.PendingMoneyMoves);

        Assert.Equal(4, state.Graph.Edges.Count);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.TestingNodeId);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.TestingNodeId
                 && e.To.Node == MagicAgencySeed.SellNodeId);
        Assert.DoesNotContain(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.SellNodeId);

        Assert.Equal(5, state.Assignments.Length);
        Assert.Contains(
            state.Assignments,
            a => a.NodeId == MagicAgencySeed.TestingNodeId);
        Assert.Contains(
            state.Assignments,
            a => a.NodeId == MagicAgencySeed.TreasuryNodeId);
        Assert.Contains(
            state.Assignments,
            a => a.NodeId == MagicAgencySeed.PayrollNodeId);

        Assert.Equal(5, state.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.Equal(DefaultConfigs, state.NodeConfigs);
    }

    [Fact]
    public void Seed_FirstTick_AssignsOnlyEnchantWhenDownstreamEmpty()
    {
        var effort = ProductionTick.ResolveEffortByNode(Seed());

        Assert.Equal(1.0m, effort[MagicAgencySeed.EnchantNodeId]);
        Assert.False(effort.ContainsKey(MagicAgencySeed.TestingNodeId));
        Assert.False(effort.ContainsKey(MagicAgencySeed.SellNodeId));
        Assert.False(effort.ContainsKey(MagicAgencySeed.TreasuryNodeId));
        Assert.False(effort.ContainsKey(MagicAgencySeed.PayrollNodeId));
    }

    [Fact]
    public void FirstTick_EnchantMutates_RoutesToTesting_TimerDecrements()
    {
        var result = ProductionTick.AdvanceTickWithReport(Seed());
        var next = result.State;

        Assert.Equal(1, next.Tick);
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(next, MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId)));
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(next, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Empty(next.PendingMoneyMoves);
        Assert.Equal(4, next.NodeTimers[MagicAgencySeed.PayrollNodeId]);

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.Equal(1.0m, enchant.Effort);
        Assert.True(enchant.Consumed);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), enchant.Produced);
    }

    [Fact]
    public void Enchant_RequiredEffortIncludesDarkness_PerMutation()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Enchanting, 30)));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, actors) with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId)),
        };

        var next = ProductionTick.AdvanceTick(state);

        // progress 30: cost 10 → (10,1,1); cost 11 → (20,2,3); remainder 9 (third needs 12)
        Assert.Equal(
            new SignalValue.Enchantment(20, 2, 3),
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(9, next.NodeProgress[MagicAgencySeed.EnchantNodeId]);
    }

    [Fact]
    public void Testing_ReducesFallacyByReductionTimesEffectiveActorCount()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Testing, 10)));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, actors) with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TestingNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty
                .Add(
                    new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
                    new SignalValue.Enchantment(20, 2, 12))
                .Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
        };

        var result = ProductionTick.AdvanceTickWithReport(state);
        var next = result.State;

        // one actor × fallacyReduction 5 → fallacy 12 - 5 = 7; forwarded to sell (no self-edge)
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId)));
        Assert.Equal(
            new SignalValue.Enchantment(20, 2, 7),
            Signal(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(0, next.NodeProgress[MagicAgencySeed.TestingNodeId]);

        var testing = Row(result, MagicAgencySeed.TestingNodeId);
        Assert.True(testing.Consumed);
        Assert.Equal(new SignalValue.Enchantment(20, 2, 7), testing.Produced);
    }

    [Fact]
    public void Testing_PassThroughWhenUnderEffort()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Testing, 4)));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, actors) with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TestingNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty
                .Add(
                    new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
                    new SignalValue.Enchantment(10, 1, 8))
                .Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId)));
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 8),
            Signal(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(4, next.NodeProgress[MagicAgencySeed.TestingNodeId]);
    }

    [Fact]
    public void SellDeposit_EnqueuesInbound_TreasuryAppliesWithEffort()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty
                    .Add(ActorStatKeys.Sales, 10)
                    .Add(ActorStatKeys.Treasury, 2)));

        var afterSell = MagicAgencySeed.CreateInitialState(DefaultConfigs, actors) with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.SellNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty
                .Add(
                    new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
                    new SignalValue.Enchantment(10, 1, 1))
                .Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
        };

        var pendingDeposit = ProductionTick.AdvanceTick(afterSell);

        Assert.Equal(
            new SignalValue.Money(100),
            Signal(pendingDeposit, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Single(pendingDeposit.PendingMoneyMoves);
        Assert.Equal(MoneyMoveDirection.In, pendingDeposit.PendingMoneyMoves[0].Direction);
        Assert.Equal(9, pendingDeposit.PendingMoneyMoves[0].Amount);

        var withTreasury = pendingDeposit with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TreasuryNodeId)),
        };
        var afterTreasury = ProductionTick.AdvanceTick(withTreasury);

        Assert.Empty(afterTreasury.PendingMoneyMoves);
        Assert.Equal(
            new SignalValue.Money(109),
            Signal(afterTreasury, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(0, afterTreasury.NodeProgress[MagicAgencySeed.TreasuryNodeId]);
    }

    [Fact]
    public void Payroll_TimerReachesDueWithoutActor_PaydayEnqueuesOut_TreasuryDebits()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty
                    .Add(ActorStatKeys.Payroll, 5)
                    .Add(ActorStatKeys.Treasury, 2)));

        var configs = DefaultConfigs with
        {
            Payroll = new PayrollNodeConfig(DefaultWage: 10, Period: 1, Effort: 5),
        };

        var state = MagicAgencySeed.CreateInitialState(configs, actors) with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            NodeTimers = ImmutableDictionary<NodeId, int>.Empty.Add(
                MagicAgencySeed.PayrollNodeId,
                1),
        };

        // Tick 1: timer 1 → 0 (not due at start)
        var afterCountdown = ProductionTick.AdvanceTick(state);
        Assert.Equal(0, afterCountdown.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.Empty(afterCountdown.PendingMoneyMoves);
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(afterCountdown, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));

        // Tick 2: payday due → enqueue out, reset timer to period
        var afterPayday = ProductionTick.AdvanceTick(afterCountdown);
        Assert.Equal(1, afterPayday.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.Single(afterPayday.PendingMoneyMoves);
        Assert.Equal(MoneyMoveDirection.Out, afterPayday.PendingMoneyMoves[0].Direction);
        Assert.Equal(10, afterPayday.PendingMoneyMoves[0].Amount);
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(afterPayday, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));

        // Tick 3: treasury applies out
        var withTreasury = afterPayday with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TreasuryNodeId)),
            NodeTimers = afterPayday.NodeTimers.SetItem(MagicAgencySeed.PayrollNodeId, 5),
        };
        var afterDebit = ProductionTick.AdvanceTick(withTreasury);

        Assert.Empty(afterDebit.PendingMoneyMoves);
        Assert.Equal(
            new SignalValue.Money(90),
            Signal(afterDebit, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Single(afterDebit.Actors);
    }

    [Fact]
    public void TreasuryOut_Shortfall_MassQuitsWithoutDebiting()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Treasury, 2)));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, actors) with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TreasuryNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(5)),
            PendingMoneyMoves = ImmutableArray.Create(
                new PendingMoneyMove(MoneyMoveDirection.Out, 10)),
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Empty(next.Actors);
        Assert.Empty(next.Assignments);
        Assert.Empty(next.PendingMoneyMoves);
        Assert.Equal(
            new SignalValue.Money(5),
            Signal(next, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
    }

    [Fact]
    public void Payday_UsesActorWageOverride_WhenEnqueuingOut()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 5),
                Wage: 7));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, actors) with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            NodeTimers = ImmutableDictionary<NodeId, int>.Empty.Add(
                MagicAgencySeed.PayrollNodeId,
                0),
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Single(next.PendingMoneyMoves);
        Assert.Equal(7, next.PendingMoneyMoves[0].Amount);
        Assert.Equal(5, next.NodeTimers[MagicAgencySeed.PayrollNodeId]);
    }

    [Fact]
    public void AdvanceTick_IsIndependentOfNodeIterationOrder()
    {
        var state = Seed();
        var forward = new[]
        {
            MagicAgencySeed.EnchantNodeId,
            MagicAgencySeed.PayrollNodeId,
            MagicAgencySeed.SellNodeId,
            MagicAgencySeed.TestingNodeId,
            MagicAgencySeed.TreasuryNodeId,
        };
        var reverse = forward.Reverse().ToArray();

        var fromForward = ProductionTick.AdvanceTick(state, forward);
        var fromReverse = ProductionTick.AdvanceTick(state, reverse);

        Assert.Equal(
            Signal(fromForward, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            Signal(fromReverse, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            Signal(fromForward, MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
            Signal(fromReverse, MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(fromForward.PendingMoneyMoves, fromReverse.PendingMoneyMoves);
    }

    [Fact]
    public void Enchantment_MutateSellAndReduceFallacy_MatchDocumentedFormulas()
    {
        var start = new SignalValue.Enchantment(0, 0, 0);
        var once = start.Mutate(DefaultConfigs.Enchant);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), once);
        Assert.Equal(9, once.SellPayout(DefaultConfigs.Sell));

        var reduced = once.ReduceFallacy(5);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 0), reduced);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 0), reduced.ReduceFallacy(99));
    }

    [Fact]
    public void LoadFromBaseDirectory_MatchesCommittedJsonDefaults()
    {
        var loaded = NodeTypeConfigLoader.LoadFromBaseDirectory();

        Assert.Equal(10, loaded.Enchant.Effort);
        Assert.Equal(10, loaded.Testing.Effort);
        Assert.Equal(5, loaded.Testing.FallacyReduction);
        Assert.Equal(10, loaded.Sell.Effort);
        Assert.Equal(2, loaded.Treasury.Effort);
        Assert.Equal(10, loaded.Payroll.DefaultWage);
        Assert.Equal(5, loaded.Payroll.Period);
        Assert.Equal(5, loaded.Payroll.Effort);
    }

    [Fact]
    public void ActorConfigLoader_LoadsInternWithStatsAndNoWage()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();
        var intern = actors[MagicAgencySeed.ActorId];
        Assert.Equal(1.0m, intern.Capacity);
        Assert.Null(intern.Wage);
        Assert.Equal(10, intern.Stats[ActorStatKeys.Enchanting]);
        Assert.Equal(10, intern.Stats[ActorStatKeys.Sales]);
    }

    [Fact]
    public void GetStat_UsesDefaultWhenMissing()
    {
        var actor = new Actor(
            new ActorId("temp"),
            Capacity: 1.0m,
            ImmutableDictionary<string, double>.Empty);

        Assert.Equal(
            ActorStatKeys.DefaultTesting,
            ProductionTick.GetStat(actor, ActorStatKeys.Testing, ActorStatKeys.DefaultTesting));
        Assert.Equal(
            ActorStatKeys.DefaultTreasury,
            ProductionTick.GetStat(actor, ActorStatKeys.Treasury, ActorStatKeys.DefaultTreasury));
    }

    [Fact]
    public void EffectiveWage_UsesActorOverrideOrPayrollDefault()
    {
        var withWage = new Actor(
            new ActorId("paid"),
            Capacity: 1.0m,
            ImmutableDictionary<string, double>.Empty,
            Wage: 25);
        var withoutWage = new Actor(
            new ActorId("defaulted"),
            Capacity: 1.0m,
            ImmutableDictionary<string, double>.Empty);

        Assert.Equal(25, ProductionTick.EffectiveWage(withWage, DefaultConfigs.Payroll));
        Assert.Equal(10, ProductionTick.EffectiveWage(withoutWage, DefaultConfigs.Payroll));
    }

    [Fact]
    public void Occupancy_SellResidualBlocksTestingCopy()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Testing, 4)));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, actors) with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TestingNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty
                .Add(
                    new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
                    new SignalValue.Enchantment(10, 1, 1))
                .Add(
                    new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
                    new SignalValue.Enchantment(9, 0, 0))
                .Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
        };

        var next = ProductionTick.AdvanceTick(state);

        // sell still occupied → testing emit skipped; residual sell stock unchanged
        Assert.Equal(
            new SignalValue.Enchantment(9, 0, 0),
            Signal(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
    }

    private static SignalValue Signal(GameState state, NodeId node, PortId port) =>
        state.PortSignals[new PortKey(node, port)];

    private static NodeIoRow Row(ProductionTickResult result, NodeId node) =>
        result.Nodes.Single(r => r.NodeId == node);
}
