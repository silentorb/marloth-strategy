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
        new TreasuryNodeConfig(Effort: 1),
        new PayrollNodeConfig(DefaultWage: 10, Period: 5, Effort: 1),
        new MergeNodeConfig(Effort: 1));

    private static readonly ImmutableDictionary<ActorId, Actor> DefaultActors =
        ImmutableDictionary<ActorId, Actor>.Empty
            .Add(
                MagicAgencySeed.ActorId,
                new Actor(
                    MagicAgencySeed.ActorId,
                    Capacity: 1.0m,
                    ImmutableDictionary<string, double>.Empty
                        .Add(ActorStatKeys.Enchanting, 10)
                        .Add(ActorStatKeys.Sales, 10)))
            .Add(
                MagicAgencySeed.BossActorId,
                new Actor(
                    MagicAgencySeed.BossActorId,
                    Capacity: 1.0m,
                    ImmutableDictionary<string, double>.Empty
                        .Add(ActorStatKeys.Sales, 10)
                        .Add(ActorStatKeys.Payroll, 10)
                        .Add(ActorStatKeys.Treasury, 10)));

    private static GameState Seed(NodeTypeConfigs? configs = null) =>
        MagicAgencySeed.CreateInitialState(configs ?? DefaultConfigs, DefaultActors);

    private static SignalValue.Enchantment Enchant(
        int volume,
        int darkness,
        int fallacy,
        ulong nextUnitId = 1,
        string? parentHash = null)
    {
        var (block, _) = EnchantmentOps.FromCounts(volume, darkness, fallacy, nextUnitId, parentHash);
        return new SignalValue.Enchantment(block);
    }

    private static GameState WithEnchantmentStock(
        GameState state,
        PortKey key,
        SignalValue.Enchantment enchantment,
        ulong? nextUnitId = null)
    {
        var blocks = state.EnchantmentBlocks.SetItem(enchantment.Hash, enchantment.Block);
        return state with
        {
            PortSignals = state.PortSignals.SetItem(key, enchantment),
            EnchantmentBlocks = blocks,
            NextUnitId = nextUnitId ?? state.NextUnitId,
        };
    }

    private static void AssertCounts(SignalValue? value, int volume, int darkness, int fallacy)
    {
        var e = Assert.IsType<SignalValue.Enchantment>(value);
        Assert.Equal(volume, e.Volume);
        Assert.Equal(darkness, e.Darkness);
        Assert.Equal(fallacy, e.Fallacy);
    }

    [Fact]
    public void Seed_HasMergeFeedbackAndTestingBetweenEnchantAndSell()
    {
        var state = Seed();

        Assert.Equal(0, state.Tick);
        Assert.Equal(6, state.Graph.Nodes.Count);
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.TestingNodeId));
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.MergeNodeId));
        Assert.Empty(state.PendingMoneyMoves);
        Assert.Single(state.EnchantmentBlocks);
        Assert.Equal(1UL, state.NextUnitId);

        Assert.Equal(7, state.Graph.Edges.Count);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.TestingNodeId);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.MergeNodeId
                 && e.To.Port == MagicAgencySeed.PrimaryPortId);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.TestingNodeId
                 && e.To.Node == MagicAgencySeed.MergeNodeId
                 && e.To.Port == MagicAgencySeed.SecondaryPortId);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.MergeNodeId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.TestingNodeId
                 && e.To.Node == MagicAgencySeed.SellNodeId);
        Assert.DoesNotContain(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId);

        Assert.Equal(6, state.Assignments.Length);
        Assert.Contains(
            state.Assignments,
            a => a.ActorId == MagicAgencySeed.ActorId && a.NodeId == MagicAgencySeed.EnchantNodeId);
        Assert.Contains(
            state.Assignments,
            a => a.ActorId == MagicAgencySeed.ActorId && a.NodeId == MagicAgencySeed.MergeNodeId);
        Assert.Contains(
            state.Assignments,
            a => a.ActorId == MagicAgencySeed.ActorId && a.NodeId == MagicAgencySeed.TestingNodeId);
        Assert.Contains(
            state.Assignments,
            a => a.ActorId == MagicAgencySeed.BossActorId && a.NodeId == MagicAgencySeed.PayrollNodeId);
        Assert.Contains(
            state.Assignments,
            a => a.ActorId == MagicAgencySeed.BossActorId && a.NodeId == MagicAgencySeed.SellNodeId);
        Assert.Contains(
            state.Assignments,
            a => a.ActorId == MagicAgencySeed.BossActorId && a.NodeId == MagicAgencySeed.TreasuryNodeId);
        Assert.All(state.Assignments, a => Assert.Equal(1m, a.Weight));
        Assert.Equal(2, state.Actors.Count);
        Assert.True(state.Actors.ContainsKey(MagicAgencySeed.BossActorId));
        Assert.Equal(0, state.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.Equal(DefaultConfigs, state.NodeConfigs);
        AssertCounts(
            Signal(state, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            0,
            0,
            0);
    }

    [Fact]
    public void Seed_FirstTick_AssignsOnlyEnchantWhenDownstreamEmpty()
    {
        var effort = ProductionTick.ResolveEffortByNode(Seed());

        Assert.Equal(1.0m, effort[MagicAgencySeed.EnchantNodeId]);
        Assert.False(effort.ContainsKey(MagicAgencySeed.TestingNodeId));
        Assert.False(effort.ContainsKey(MagicAgencySeed.SellNodeId));
        Assert.False(effort.ContainsKey(MagicAgencySeed.MergeNodeId));
        Assert.False(effort.ContainsKey(MagicAgencySeed.TreasuryNodeId));
        Assert.False(effort.ContainsKey(MagicAgencySeed.PayrollNodeId));
    }

    [Fact]
    public void FirstTick_EnchantMutates_RoutesToTestingAndMergePrimary()
    {
        var result = ProductionTick.AdvanceTickWithReport(Seed());
        var next = result.State;

        Assert.Equal(1, next.Tick);
        // Feedback is via merge, which needs secondary — enchant port clears after consume.
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId)));
        AssertCounts(
            Signal(next, MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            1,
            1);
        AssertCounts(
            Signal(next, MagicAgencySeed.MergeNodeId, MagicAgencySeed.PrimaryPortId),
            10,
            1,
            1);
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId)));
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.MergeNodeId, MagicAgencySeed.SecondaryPortId)));
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(next, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Empty(next.PendingMoneyMoves);
        Assert.Equal(1, next.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.True(next.EnchantmentBlocks.Count >= 2);

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.Equal(1.0m, enchant.Effort);
        Assert.True(enchant.Consumed);
        AssertCounts(enchant.Produced, 10, 1, 1);
    }

    [Fact]
    public void EssentialGraph_FirstTick_EnchantSelfLoopCopiesBackAndToSell()
    {
        var spec = new ScenarioSpec(
            IncludeTestingMerge: false,
            ImmutableArray.Create(MagicAgencySeed.ActorId),
            ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId)));
        var state = ScenarioBootstrap.Materialize(spec, DefaultConfigs, DefaultActors);
        var next = ProductionTick.AdvanceTick(state);

        AssertCounts(
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            1,
            1);
        AssertCounts(
            Signal(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            1,
            1);
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

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId)),
        };

        var result = ProductionTick.AdvanceTickWithReport(state);
        var next = result.State;

        // progress 30: cost 10 → (10,1,1); cost 11 → (20,2,3); remainder 9 (third needs 12)
        AssertCounts(Row(result, MagicAgencySeed.EnchantNodeId).Produced, 20, 2, 3);
        AssertCounts(
            Signal(next, MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
            20,
            2,
            3);
        Assert.Equal(9, next.NodeProgress[MagicAgencySeed.EnchantNodeId]);
    }

    [Fact]
    public void Testing_RemovesFallacyUnitsByReductionTimesEffectiveActorCount()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Testing, 10)));

        var stock = Enchant(20, 2, 12);
        var state = WithEnchantmentStock(
            MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
            {
                Actors = actors,
                Assignments = ImmutableArray.Create(
                    new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TestingNodeId)),
                PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
            },
            new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
            stock);

        var result = ProductionTick.AdvanceTickWithReport(state);
        var next = result.State;

        // one actor × fallacyReduction 5 → fallacy 12 - 5 = 7; forwarded to sell and merge secondary
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId)));
        AssertCounts(
            Signal(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
            20,
            2,
            7);
        AssertCounts(
            Signal(next, MagicAgencySeed.MergeNodeId, MagicAgencySeed.SecondaryPortId),
            20,
            2,
            7);
        Assert.Equal(0, next.NodeProgress[MagicAgencySeed.TestingNodeId]);

        var testing = Row(result, MagicAgencySeed.TestingNodeId);
        Assert.True(testing.Consumed);
        AssertCounts(testing.Produced, 20, 2, 7);
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

        var stock = Enchant(10, 1, 8);
        var state = WithEnchantmentStock(
            MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
            {
                Actors = actors,
                Assignments = ImmutableArray.Create(
                    new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TestingNodeId)),
                PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
            },
            new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
            stock);

        var next = ProductionTick.AdvanceTick(state);

        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId)));
        AssertCounts(
            Signal(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            1,
            8);
        Assert.Equal(4, next.NodeProgress[MagicAgencySeed.TestingNodeId]);
    }

    [Fact]
    public void Merge_FastForwardsWhenSecondaryIsDescendantOfPrimary()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Merging, 5)));

        var genesis = EnchantmentBlock.CreateGenesis();
        var (child, nextId) = EnchantmentOps.Mutate(genesis, DefaultConfigs.Enchant, 1);
        var blocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
            .Add(genesis.Hash, genesis)
            .Add(child.Hash, child);

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.MergeNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty
                .Add(
                    new PortKey(MagicAgencySeed.MergeNodeId, MagicAgencySeed.PrimaryPortId),
                    new SignalValue.Enchantment(genesis))
                .Add(
                    new PortKey(MagicAgencySeed.MergeNodeId, MagicAgencySeed.SecondaryPortId),
                    new SignalValue.Enchantment(child))
                .Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
            EnchantmentBlocks = blocks,
            NextUnitId = nextId,
        };

        var next = ProductionTick.AdvanceTick(state);
        AssertCounts(
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            1,
            1);
        Assert.Equal(
            child.Hash,
            ((SignalValue.Enchantment)Signal(
                next,
                MagicAgencySeed.EnchantNodeId,
                MagicAgencySeed.EnchantmentPortId)).Hash);
    }

    [Fact]
    public void Merge_IncompatibleBlocks_EmitsPrimary()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Merging, 5)));

        var primary = Enchant(3, 0, 0, nextUnitId: 1);
        var secondary = Enchant(4, 0, 0, nextUnitId: 100);
        var blocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
            .Add(primary.Hash, primary.Block)
            .Add(secondary.Hash, secondary.Block);

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.MergeNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty
                .Add(
                    new PortKey(MagicAgencySeed.MergeNodeId, MagicAgencySeed.PrimaryPortId),
                    primary)
                .Add(
                    new PortKey(MagicAgencySeed.MergeNodeId, MagicAgencySeed.SecondaryPortId),
                    secondary)
                .Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
            EnchantmentBlocks = blocks,
            NextUnitId = 200,
        };

        var next = ProductionTick.AdvanceTick(state);
        Assert.Equal(
            primary.Hash,
            ((SignalValue.Enchantment)Signal(
                next,
                MagicAgencySeed.EnchantNodeId,
                MagicAgencySeed.EnchantmentPortId)).Hash);
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
                    .Add(ActorStatKeys.Treasury, 1)));

        var stock = Enchant(10, 1, 1);
        var afterSell = WithEnchantmentStock(
            MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
            {
                Actors = actors,
                Assignments = ImmutableArray.Create(
                    new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.SellNodeId)),
                PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
            },
            new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
            stock);

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
            Payroll = new PayrollNodeConfig(DefaultWage: 10, Period: 1, Effort: 1),
        };

        var state = MagicAgencySeed.CreateInitialState(configs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            NodeTimers = ImmutableDictionary<NodeId, int>.Empty.Add(
                MagicAgencySeed.PayrollNodeId,
                0),
        };

        var afterCountdown = ProductionTick.AdvanceTick(state);
        Assert.Equal(1, afterCountdown.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.Empty(afterCountdown.PendingMoneyMoves);

        var afterPayday = ProductionTick.AdvanceTick(afterCountdown);
        Assert.Equal(0, afterPayday.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.Equal(0, afterPayday.NodeProgress.GetValueOrDefault(MagicAgencySeed.PayrollNodeId, 0));
        Assert.Single(afterPayday.PendingMoneyMoves);
        Assert.Equal(MoneyMoveDirection.Out, afterPayday.PendingMoneyMoves[0].Direction);
        Assert.Equal(10, afterPayday.PendingMoneyMoves[0].Amount);

        var withTreasury = afterPayday with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TreasuryNodeId)),
            NodeTimers = afterPayday.NodeTimers.SetItem(MagicAgencySeed.PayrollNodeId, 0),
        };
        var afterDebit = ProductionTick.AdvanceTick(withTreasury);

        Assert.Empty(afterDebit.PendingMoneyMoves);
        Assert.Equal(
            new SignalValue.Money(90),
            Signal(afterDebit, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(
            new SignalValue.Money(10),
            Signal(afterDebit, MagicAgencySeed.PayrollNodeId, MagicAgencySeed.MoneyPortId));

        var afterDisburse = ProductionTick.AdvanceTick(afterDebit);
        Assert.Null(
            afterDisburse.PortSignals.GetValueOrDefault(
                new PortKey(MagicAgencySeed.PayrollNodeId, MagicAgencySeed.MoneyPortId)));
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

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
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
    public void Payday_WithoutTreasuryFundingEdge_Throws()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 5)));

        var seeded = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var edgesWithoutFunding = seeded.Graph.Edges
            .Where(kv => kv.Key.Value != "treasury-to-payroll")
            .ToImmutableDictionary();

        var state = seeded with
        {
            Actors = actors,
            Graph = new NodeGraph(seeded.Graph.Nodes, edgesWithoutFunding),
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            NodeTimers = ImmutableDictionary<NodeId, int>.Empty.Add(
                MagicAgencySeed.PayrollNodeId,
                5),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionTick.AdvanceTick(state));
        Assert.Contains("funding edge", ex.Message, StringComparison.OrdinalIgnoreCase);
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

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            NodeTimers = ImmutableDictionary<NodeId, int>.Empty.Add(
                MagicAgencySeed.PayrollNodeId,
                5),
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Single(next.PendingMoneyMoves);
        Assert.Equal(7, next.PendingMoneyMoves[0].Amount);
        Assert.Equal(0, next.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.Equal(0, next.NodeProgress.GetValueOrDefault(MagicAgencySeed.PayrollNodeId, 0));
    }

    [Fact]
    public void Payday_ResetsProgressEvenWhenGainExceedsEffort()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 10)));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            NodeTimers = ImmutableDictionary<NodeId, int>.Empty.Add(
                MagicAgencySeed.PayrollNodeId,
                DefaultConfigs.Payroll.Period),
            NodeProgress = ImmutableDictionary<NodeId, double>.Empty.Add(
                MagicAgencySeed.PayrollNodeId,
                3),
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Single(next.PendingMoneyMoves);
        Assert.Equal(0, next.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.Equal(0, next.NodeProgress.GetValueOrDefault(MagicAgencySeed.PayrollNodeId, 0));
    }

    [Fact]
    public void AdvanceTick_IsIndependentOfNodeIterationOrder()
    {
        var state = Seed();
        var forward = new[]
        {
            MagicAgencySeed.EnchantNodeId,
            MagicAgencySeed.MergeNodeId,
            MagicAgencySeed.PayrollNodeId,
            MagicAgencySeed.SellNodeId,
            MagicAgencySeed.TestingNodeId,
            MagicAgencySeed.TreasuryNodeId,
        };
        var reverse = forward.Reverse().ToArray();

        var fromForward = ProductionTick.AdvanceTick(state, forward);
        var fromReverse = ProductionTick.AdvanceTick(state, reverse);

        Assert.Equal(
            ((SignalValue.Enchantment)Signal(
                fromForward,
                MagicAgencySeed.TestingNodeId,
                MagicAgencySeed.EnchantmentPortId)).Hash,
            ((SignalValue.Enchantment)Signal(
                fromReverse,
                MagicAgencySeed.TestingNodeId,
                MagicAgencySeed.EnchantmentPortId)).Hash);
        Assert.Equal(
            ((SignalValue.Enchantment)Signal(
                fromForward,
                MagicAgencySeed.MergeNodeId,
                MagicAgencySeed.PrimaryPortId)).Hash,
            ((SignalValue.Enchantment)Signal(
                fromReverse,
                MagicAgencySeed.MergeNodeId,
                MagicAgencySeed.PrimaryPortId)).Hash);
        Assert.Equal(fromForward.PendingMoneyMoves, fromReverse.PendingMoneyMoves);
        Assert.Equal(fromForward.NextUnitId, fromReverse.NextUnitId);
    }

    [Fact]
    public void Enchantment_MutateSellAndReduceFallacy_MatchDocumentedFormulas()
    {
        var genesis = EnchantmentBlock.CreateGenesis();
        var (once, next) = EnchantmentOps.Mutate(genesis, DefaultConfigs.Enchant, 1);
        Assert.Equal(10, once.VolumeCount);
        Assert.Equal(1, once.DarknessCount);
        Assert.Equal(1, once.FallacyCount);
        Assert.Equal(9, new SignalValue.Enchantment(once).SellPayout(DefaultConfigs.Sell));

        var reduced = EnchantmentOps.ReduceFallacy(once, 5);
        Assert.Equal(0, reduced.FallacyCount);
        Assert.Equal(0, EnchantmentOps.ReduceFallacy(reduced, 99).FallacyCount);
        Assert.Equal(13UL, next);
    }

    [Fact]
    public void LoadFromBaseDirectory_MatchesCommittedJsonDefaults()
    {
        var loaded = NodeTypeConfigLoader.LoadFromBaseDirectory();

        Assert.Equal(10, loaded.Enchant.Effort);
        Assert.Equal(10, loaded.Testing.Effort);
        Assert.Equal(5, loaded.Testing.FallacyReduction);
        Assert.Equal(1, loaded.Merge.Effort);
        Assert.Equal(10, loaded.Sell.Effort);
        Assert.Equal(1, loaded.Treasury.Effort);
        Assert.Equal(10, loaded.Payroll.DefaultWage);
        Assert.Equal(5, loaded.Payroll.Period);
        Assert.Equal(1, loaded.Payroll.Effort);
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
    public void ActorConfigLoader_LoadsBossWithStatsAndNoWage()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();
        var boss = actors[MagicAgencySeed.BossActorId];
        Assert.Equal(1.0m, boss.Capacity);
        Assert.Null(boss.Wage);
        Assert.Equal(10, boss.Stats[ActorStatKeys.Sales]);
        Assert.Equal(10, boss.Stats[ActorStatKeys.Payroll]);
        Assert.Equal(10, boss.Stats[ActorStatKeys.Treasury]);
    }

    [Fact]
    public void ResolveEffortByNode_SplitsCapacityByRelativeWeights()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty
                    .Add(ActorStatKeys.Enchanting, 1)
                    .Add(ActorStatKeys.Testing, 1)));

        var testingStock = Enchant(1, 0, 0);
        var state = WithEnchantmentStock(
            MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
            {
                Actors = actors,
                Assignments = ImmutableArray.Create(
                    new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId, Weight: 1m),
                    new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TestingNodeId, Weight: 3m)),
            },
            new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
            testingStock);

        var effort = ProductionTick.ResolveEffortByNode(state);

        Assert.Equal(0.25m, effort[MagicAgencySeed.EnchantNodeId]);
        Assert.Equal(0.75m, effort[MagicAgencySeed.TestingNodeId]);
    }

    [Fact]
    public void ResolveEffortByNode_EvenWeightsSplitCapacityEqually()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty
                    .Add(ActorStatKeys.Enchanting, 1)
                    .Add(ActorStatKeys.Testing, 1)));

        var testingStock = Enchant(1, 0, 0);
        var state = WithEnchantmentStock(
            MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
            {
                Actors = actors,
                Assignments = ImmutableArray.Create(
                    new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId, Weight: 1m),
                    new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TestingNodeId, Weight: 1m)),
            },
            new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
            testingStock);

        var effort = ProductionTick.ResolveEffortByNode(state);

        Assert.Equal(0.5m, effort[MagicAgencySeed.EnchantNodeId]);
        Assert.Equal(0.5m, effort[MagicAgencySeed.TestingNodeId]);
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
            ActorStatKeys.DefaultMerging,
            ProductionTick.GetStat(actor, ActorStatKeys.Merging, ActorStatKeys.DefaultMerging));
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

        var testingStock = Enchant(10, 1, 1, nextUnitId: 1);
        var sellStock = Enchant(9, 0, 0, nextUnitId: 100);
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TestingNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty
                .Add(
                    new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
                    testingStock)
                .Add(
                    new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
                    sellStock)
                .Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
            EnchantmentBlocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
                .Add(testingStock.Hash, testingStock.Block)
                .Add(sellStock.Hash, sellStock.Block),
            NextUnitId = 200,
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Equal(
            sellStock.Hash,
            ((SignalValue.Enchantment)Signal(
                next,
                MagicAgencySeed.SellNodeId,
                MagicAgencySeed.EnchantmentPortId)).Hash);
    }

    private static SignalValue Signal(GameState state, NodeId node, PortId port) =>
        state.PortSignals[new PortKey(node, port)];

    private static NodeIoRow Row(ProductionTickResult result, NodeId node) =>
        result.Nodes.Single(r => r.NodeId == node);
}
