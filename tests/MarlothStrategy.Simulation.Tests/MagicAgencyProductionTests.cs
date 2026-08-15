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
            FallacyConstant: 1,
            DesignDarknessDelta: 0.3),
        new TestingNodeConfig(Effort: 10, FallacyReduction: 5),
        new SellNodeConfig(Effort: 10, PayoutFloor: 0),
        new TreasuryNodeConfig(Effort: 1),
        new PayrollNodeConfig(new PayrollScheduleConfig("month", "day", 0, 10), BaseEffort: 1, PerActorEffort: 1),
        new MergeNodeConfig(Effort: 1),
        new DesignNodeConfig(Effort: 3, DesignDelta: 1, DarknessReduction: 0.9));

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
        double darkness,
        double fallacy,
        int designs = 0,
        ulong nextUnitId = 1,
        string? parentHash = null)
    {
        var (block, _) = EnchantmentOps.FromCounts(
            volume,
            designs,
            darkness,
            fallacy,
            nextUnitId,
            parentHash);
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

    private static void AssertCounts(
        SignalValue? value,
        int volume,
        double darkness,
        double fallacy,
        int designs = 0)
    {
        var e = Assert.IsType<SignalValue.Enchantment>(value);
        Assert.Equal(volume, e.Volume);
        Assert.Equal(designs, e.Designs);
        Assert.Equal(darkness, e.Darkness, 6);
        Assert.Equal(fallacy, e.Fallacy, 6);
    }

    private static GameState WithMergeGraph(GameState state) =>
        state with { Graph = GraphFactory.CreateGraphWithMergeNode() };

    private static GameState DesignScenario(
        ImmutableDictionary<ActorId, Actor> actors,
        ActorId assignedActor)
    {
        var spec = new ScenarioSpec(
            IncludeTesting: false,
            ImmutableArray.Create(assignedActor),
            ImmutableArray.Create(new Assignment(assignedActor, MagicAgencySeed.DesignNodeId)),
            IncludeDesign: true);
        return ScenarioBootstrap.Materialize(spec, DefaultConfigs, actors);
    }

    private static GameState WithDesignInput(
        GameState state,
        SignalValue.Enchantment enchantment,
        ulong? nextUnitId = null)
    {
        // Isolated design tests should not leave genesis residual on enchant,
        // or commit would treat design output as incompatible and empty the port.
        var cleared = state with
        {
            PortSignals = state.PortSignals.Remove(
                new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId)),
        };
        return WithEnchantmentStock(
            cleared,
            new PortKey(MagicAgencySeed.DesignNodeId, MagicAgencySeed.EnchantmentPortId),
            enchantment,
            nextUnitId);
    }

    [Fact]
    public void Seed_HasTestingFanInAndEnchantSelfLoop()
    {
        var state = Seed();

        Assert.Equal(0, state.Tick);
        Assert.Equal(5, state.Graph.Nodes.Count);
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.TestingNodeId));
        Assert.False(state.Graph.Nodes.ContainsKey(MagicAgencySeed.MergeNodeId));
        Assert.Empty(state.PendingMoneyMoves);
        Assert.Single(state.EnchantmentBlocks);
        Assert.Equal(1UL, state.NextUnitId);

        Assert.Equal(6, state.Graph.Edges.Count);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.TestingNodeId);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.TestingNodeId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId);
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
            a => a.ActorId == MagicAgencySeed.ActorId && a.NodeId == MagicAgencySeed.EnchantNodeId);
        Assert.DoesNotContain(
            state.Assignments,
            a => a.NodeId == MagicAgencySeed.MergeNodeId);
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
        Assert.Null(state.ActivePayrollRun);
        Assert.Empty(state.NodeTimers);
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
        Assert.False(effort.ContainsKey(MagicAgencySeed.TreasuryNodeId));
        Assert.False(effort.ContainsKey(MagicAgencySeed.PayrollNodeId));
    }

    [Fact]
    public void FirstTick_EnchantMutates_RoutesToTestingAndSelfLoop()
    {
        var result = ProductionTick.AdvanceTickWithReport(Seed());
        var next = result.State;

        Assert.Equal(1, next.Tick);
        AssertCounts(
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            10,
            1);
        AssertCounts(
            Signal(next, MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            10,
            1);
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId)));
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(next, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Empty(next.PendingMoneyMoves);
        Assert.Null(next.ActivePayrollRun);
        Assert.True(next.EnchantmentBlocks.Count >= 2);

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.Equal(1.0m, enchant.Effort);
        Assert.True(enchant.Consumed);
        AssertCounts(enchant.Produced, 10, 10, 1);
    }

    [Fact]
    public void EssentialGraph_FirstTick_EnchantSelfLoopCopiesBackAndToSell()
    {
        var spec = new ScenarioSpec(
            IncludeTesting: false,
            ImmutableArray.Create(MagicAgencySeed.ActorId),
            ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId)));
        var state = ScenarioBootstrap.Materialize(spec, DefaultConfigs, DefaultActors);
        var next = ProductionTick.AdvanceTick(state);

        AssertCounts(
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            10,
            1);
        AssertCounts(
            Signal(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            10,
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

        // progress 30: cost 10 → (10,10,1); cost 20 → (20,20,12); remainder 0
        AssertCounts(Row(result, MagicAgencySeed.EnchantNodeId).Produced, 20, 20, 12);
        AssertCounts(
            Signal(next, MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
            20,
            20,
            12);
        Assert.Equal(0, next.NodeProgress[MagicAgencySeed.EnchantNodeId]);
    }

    [Fact]
    public void Design_AddsDesignUnitsAndReducesDarknessPerApplication()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Designing, 3)));

        var stock = Enchant(volume: 0, darkness: 2.0, fallacy: 1.0);
        var state = WithDesignInput(DesignScenario(actors, MagicAgencySeed.ActorId), stock);
        var result = ProductionTick.AdvanceTickWithReport(state);
        var next = result.State;

        AssertCounts(
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            volume: 0,
            darkness: 1.1,
            fallacy: 1.0,
            designs: 1);
        Assert.Equal(0, next.NodeProgress[MagicAgencySeed.DesignNodeId]);
        Assert.Equal(1, next.NodeCycles.GetValueOrDefault(MagicAgencySeed.DesignNodeId, 0));
        Assert.Equal(0, next.NodeCycles.GetValueOrDefault(MagicAgencySeed.SellNodeId, 0));

        var design = Row(result, MagicAgencySeed.DesignNodeId);
        Assert.Equal(1.0m, design.Effort);
        Assert.True(design.Consumed);
        AssertCounts(design.Produced, volume: 0, darkness: 1.1, fallacy: 1.0, designs: 1);
    }

    [Fact]
    public void Design_TwoApplicationsInOneTick_IncrementsCyclesByTwo()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Designing, 6)));

        var stock = Enchant(volume: 0, darkness: 2.0, fallacy: 0);
        var state = WithDesignInput(DesignScenario(actors, MagicAgencySeed.ActorId), stock);
        var next = ProductionTick.AdvanceTick(state);

        AssertCounts(
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            volume: 0,
            darkness: 0.2,
            fallacy: 0,
            designs: 2);
        Assert.Equal(2, next.NodeCycles.GetValueOrDefault(MagicAgencySeed.DesignNodeId, 0));
    }

    [Fact]
    public void Design_PassThroughWhenUnderEffort()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Designing, 2)));

        var stock = Enchant(volume: 5, darkness: 1.0, fallacy: 0);
        var state = WithDesignInput(DesignScenario(actors, MagicAgencySeed.ActorId), stock);
        var next = ProductionTick.AdvanceTick(state);

        AssertCounts(
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            volume: 5,
            darkness: 1.0,
            fallacy: 0,
            designs: 0);
        Assert.Equal(2, next.NodeProgress[MagicAgencySeed.DesignNodeId]);
        Assert.Equal(0, next.NodeCycles.GetValueOrDefault(MagicAgencySeed.DesignNodeId, 0));
    }

    [Fact]
    public void Enchant_UsesDesignUnitsAtReducedDarkness()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Enchanting, 10)));

        var genesis = EnchantmentBlock.CreateGenesis();
        var (designed, nextId) = EnchantmentOps.ApplyDesign(
            genesis,
            DefaultConfigs.Design,
            nextUnitId: 1,
            applications: 4);
        var stock = new SignalValue.Enchantment(designed);
        var state = WithEnchantmentStock(
            MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
            {
                Actors = actors,
                Assignments = ImmutableArray.Create(
                    new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId)),
                PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
            },
            new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            stock,
            nextId);

        var next = ProductionTick.AdvanceTick(state);

        AssertCounts(
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            volume: 10,
            darkness: 4 * 0.3 + 6 * 1.0,
            fallacy: 1.0,
            designs: 4);
    }

    [Fact]
    public void DesignGraph_RoutesEnchantmentLoopThroughDesign()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty
                    .Add(ActorStatKeys.Enchanting, 10)
                    .Add(ActorStatKeys.Designing, 3)));
        var spec = new ScenarioSpec(
            IncludeTesting: false,
            ImmutableArray.Create(MagicAgencySeed.ActorId),
            ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId)),
            IncludeDesign: true);
        var state = ScenarioBootstrap.Materialize(spec, DefaultConfigs, actors);

        var afterEnchant = ProductionTick.AdvanceTick(state);
        AssertCounts(
            Signal(afterEnchant, MagicAgencySeed.DesignNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            10,
            1);
        Assert.False(
            afterEnchant.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId)));

        var designAssigned = afterEnchant with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.DesignNodeId)),
        };
        var afterDesign = ProductionTick.AdvanceTick(designAssigned);
        AssertCounts(
            Signal(afterDesign, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            volume: 10,
            darkness: 9.1,
            fallacy: 1.0,
            designs: 1);
    }

    [Fact]
    public void SignalValue_TryCombine_AddsMoney()
    {
        var blocks = ImmutableDictionary<string, EnchantmentBlock>.Empty;
        Assert.True(
            SignalValue.TryCombine(
                [new SignalValue.Money(2), new SignalValue.Money(3)],
                blocks,
                out var combined));
        Assert.Equal(new SignalValue.Money(5), combined);
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

        // one actor × fallacyReduction 5 → fallacy 12 - 5 = 7; forwarded to sell and enchant
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId)));
        AssertCounts(
            Signal(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
            20,
            2,
            7);
        AssertCounts(
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
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

        var state = WithMergeGraph(MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
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
        });

        var next = ProductionTick.AdvanceTick(state);
        AssertCounts(
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            10,
            10,
            1);
        Assert.Equal(
            child.Hash,
            ((SignalValue.Enchantment)Signal(
                next,
                MagicAgencySeed.EnchantNodeId,
                MagicAgencySeed.EnchantmentPortId)).Hash);
    }

    [Fact]
    public void Merge_IncompatibleBlocks_EmitsNothing()
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

        var state = WithMergeGraph(MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
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
        });

        var next = ProductionTick.AdvanceTick(state);
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId)));
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.MergeNodeId, MagicAgencySeed.PrimaryPortId)));
        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.MergeNodeId, MagicAgencySeed.SecondaryPortId)));
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
    public void PortFlowTotals_RecordSaleIncomeAndPayrollDisbursement()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty
                    .Add(ActorStatKeys.Sales, 10)
                    .Add(ActorStatKeys.Payroll, 5)
                    .Add(ActorStatKeys.Treasury, 2),
                Wage: 10));

        var sellMoneyKey = new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.MoneyPortId);
        var payrollMoneyKey = new PortKey(MagicAgencySeed.PayrollNodeId, MagicAgencySeed.MoneyPortId);

        var beforeSale = WithEnchantmentStock(
            MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
            {
                Tick = 27,
                Actors = actors,
                Assignments = ImmutableArray.Create(
                    new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.SellNodeId)),
                PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
            },
            new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
            Enchant(10, 1, 1));
        Assert.Empty(beforeSale.PortFlowTotals);

        // Sale money never rests on the sell port, so the payout only shows as lifetime flow.
        var afterSale = ProductionTick.AdvanceTick(beforeSale);
        Assert.Equal(9, afterSale.PortFlowTotals.GetValueOrDefault(sellMoneyKey));
        Assert.Null(afterSale.PortSignals.GetValueOrDefault(sellMoneyKey));

        var payday = ProductionTick.AdvanceTick(afterSale with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
        });
        var delivered = ProductionTick.AdvanceTick(payday with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TreasuryNodeId)),
        });
        Assert.Equal(
            new SignalValue.Money(10),
            Signal(delivered, MagicAgencySeed.PayrollNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(0, delivered.PortFlowTotals.GetValueOrDefault(payrollMoneyKey));

        var disbursed = ProductionTick.AdvanceTick(delivered);
        Assert.Null(disbursed.PortSignals.GetValueOrDefault(payrollMoneyKey));
        Assert.Equal(-10, disbursed.PortFlowTotals.GetValueOrDefault(payrollMoneyKey));
        Assert.Equal(9, disbursed.PortFlowTotals.GetValueOrDefault(sellMoneyKey));
    }

    [Fact]
    public void Payroll_OpensOnLastDay_PaydayEnqueuesOut_TreasuryDebits()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty
                    .Add(ActorStatKeys.Payroll, 5)
                    .Add(ActorStatKeys.Treasury, 2),
                Wage: 10));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Tick = 27,
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
        };

        var afterPayday = ProductionTick.AdvanceTick(state);
        Assert.NotNull(afterPayday.ActivePayrollRun);
        Assert.Equal(0, afterPayday.ActivePayrollRun!.PeriodIndex);
        Assert.True(afterPayday.ActivePayrollRun.AttemptSubmitted);
        Assert.Equal(0, afterPayday.NodeProgress.GetValueOrDefault(MagicAgencySeed.PayrollNodeId, 0));
        Assert.Equal(1, afterPayday.NodeCycles.GetValueOrDefault(MagicAgencySeed.PayrollNodeId, 0));
        Assert.Single(afterPayday.PendingMoneyMoves);
        Assert.Equal(MoneyMoveDirection.Out, afterPayday.PendingMoneyMoves[0].Direction);
        Assert.Equal(10, afterPayday.PendingMoneyMoves[0].Amount);
        Assert.Equal(0, afterPayday.PendingMoneyMoves[0].PayrollRunPeriodIndex);
        Assert.Equal(28, afterPayday.Tick);

        var midMonth = ProductionTick.AdvanceTicks(state with { Tick = 10 }, 1);
        Assert.Null(midMonth.ActivePayrollRun);
        Assert.Empty(midMonth.PendingMoneyMoves);

        var withTreasury = afterPayday with
        {
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TreasuryNodeId)),
        };
        var afterDebit = ProductionTick.AdvanceTick(withTreasury);

        Assert.Empty(afterDebit.PendingMoneyMoves);
        Assert.Contains(MagicAgencySeed.ActorId, afterDebit.ActivePayrollRun!.PaidActorIds);
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
    public void TreasuryOut_Shortfall_PaysWholeActorsDeterministicallyWithoutImmediateQuit()
    {
        var a = new ActorId("a");
        var b = new ActorId("b");
        var c = new ActorId("c");
        var actors = ImmutableDictionary<ActorId, Actor>.Empty
            .Add(a, new Actor(a, 1.0m, ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Treasury, 5), Wage: 10))
            .Add(b, new Actor(b, 1.0m, ImmutableDictionary<string, double>.Empty, Wage: 10))
            .Add(c, new Actor(c, 1.0m, ImmutableDictionary<string, double>.Empty, Wage: 10));

        var obligations = ImmutableArray.Create(
            new PayrollObligation(a, 10),
            new PayrollObligation(b, 10),
            new PayrollObligation(c, 10));
        var run = new PayrollRun(0, obligations, ImmutableHashSet<ActorId>.Empty, AttemptSubmitted: true);

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Tick = 30, // before due-day close so shortfall does not immediately remove actors
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(a, MagicAgencySeed.TreasuryNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(25)),
            PendingMoneyMoves = ImmutableArray.Create(
                new PendingMoneyMove(MoneyMoveDirection.Out, 30, 0, obligations)),
            ActivePayrollRun = run,
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Equal(3, next.Actors.Count);
        Assert.Single(next.Assignments);
        Assert.Empty(next.PendingMoneyMoves);
        Assert.Equal(2, next.ActivePayrollRun!.PaidActorIds.Count);
        Assert.Equal(
            new SignalValue.Money(5),
            Signal(next, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));

        var again = ProductionTick.AdvanceTick(state);
        Assert.Equal(next.ActivePayrollRun.PaidActorIds, again.ActivePayrollRun!.PaidActorIds);
    }

    [Fact]
    public void Payroll_PastDue_RemovesOnlyUnpaidActorsAndCancelsStaleRequest()
    {
        var paid = new ActorId("paid");
        var unpaid = new ActorId("unpaid");
        var unwaged = new ActorId("unwaged");
        var actors = ImmutableDictionary<ActorId, Actor>.Empty
            .Add(paid, new Actor(paid, 1.0m, ImmutableDictionary<string, double>.Empty, Wage: 10))
            .Add(unpaid, new Actor(unpaid, 1.0m, ImmutableDictionary<string, double>.Empty, Wage: 10))
            .Add(unwaged, new Actor(unwaged, 1.0m, ImmutableDictionary<string, double>.Empty));

        var obligations = ImmutableArray.Create(
            new PayrollObligation(paid, 10),
            new PayrollObligation(unpaid, 10));
        var run = new PayrollRun(
            0,
            obligations,
            ImmutableHashSet.Create(paid),
            AttemptSubmitted: true);

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Tick = 37, // day 10 of month 2; next tick is day 11 → past due
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(paid, MagicAgencySeed.EnchantNodeId),
                new Assignment(unpaid, MagicAgencySeed.SellNodeId),
                new Assignment(unwaged, MagicAgencySeed.TestingNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(0)),
            PendingMoneyMoves = ImmutableArray.Create(
                new PendingMoneyMove(MoneyMoveDirection.Out, 20, 0, obligations)),
            ActivePayrollRun = run,
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Null(next.ActivePayrollRun);
        Assert.Equal(38, next.Tick);
        Assert.False(next.Actors.ContainsKey(unpaid));
        Assert.True(next.Actors.ContainsKey(paid));
        Assert.True(next.Actors.ContainsKey(unwaged));
        Assert.DoesNotContain(next.Assignments, a => a.ActorId == unpaid);
        Assert.Contains(next.Assignments, a => a.ActorId == paid);
        Assert.Contains(next.Assignments, a => a.ActorId == unwaged);
        Assert.Empty(next.PendingMoneyMoves);
    }

    [Fact]
    public void Payroll_DueDaySubmission_IsLateWithoutTreasuryDelivery()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 5),
                Wage: 10));

        var obligations = ImmutableArray.Create(new PayrollObligation(MagicAgencySeed.ActorId, 10));
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Tick = 37, // due day; treasury cannot deliver until the next tick
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            ActivePayrollRun = new PayrollRun(
                0,
                obligations,
                ImmutableHashSet<ActorId>.Empty,
                AttemptSubmitted: false),
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Null(next.ActivePayrollRun);
        Assert.False(next.Actors.ContainsKey(MagicAgencySeed.ActorId));
        Assert.Empty(next.PendingMoneyMoves);
    }

    [Fact]
    public void Payday_WithoutTreasuryFundingEdge_Throws()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 5),
                Wage: 10));

        var seeded = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var edgesWithoutFunding = seeded.Graph.Edges
            .Where(kv => kv.Key.Value != "treasury-to-payroll")
            .ToImmutableDictionary();

        var obligations = ImmutableArray.Create(new PayrollObligation(MagicAgencySeed.ActorId, 10));
        var state = seeded with
        {
            Actors = actors,
            Graph = new NodeGraph(seeded.Graph.Nodes, edgesWithoutFunding),
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            ActivePayrollRun = new PayrollRun(
                0,
                obligations,
                ImmutableHashSet<ActorId>.Empty,
                AttemptSubmitted: false),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ProductionTick.AdvanceTick(state));
        Assert.Contains("funding edge", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Payday_UsesActorWage_WhenEnqueuingOut()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 5),
                Wage: 7));

        var obligations = ImmutableArray.Create(new PayrollObligation(MagicAgencySeed.ActorId, 7));
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            ActivePayrollRun = new PayrollRun(
                0,
                obligations,
                ImmutableHashSet<ActorId>.Empty,
                AttemptSubmitted: false),
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Single(next.PendingMoneyMoves);
        Assert.Equal(7, next.PendingMoneyMoves[0].Amount);
        Assert.True(next.ActivePayrollRun!.AttemptSubmitted);
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
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 10),
                Wage: 10));

        var obligations = ImmutableArray.Create(new PayrollObligation(MagicAgencySeed.ActorId, 10));
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            ActivePayrollRun = new PayrollRun(
                0,
                obligations,
                ImmutableHashSet<ActorId>.Empty,
                AttemptSubmitted: false),
            NodeProgress = ImmutableDictionary<NodeId, double>.Empty.Add(
                MagicAgencySeed.PayrollNodeId,
                3),
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Single(next.PendingMoneyMoves);
        Assert.True(next.ActivePayrollRun!.AttemptSubmitted);
        Assert.Equal(0, next.NodeProgress.GetValueOrDefault(MagicAgencySeed.PayrollNodeId, 0));
    }

    [Fact]
    public void Payday_UnwagedActors_ResetWithoutEnqueue()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 5)));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Tick = 27,
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Empty(next.PendingMoneyMoves);
        Assert.NotNull(next.ActivePayrollRun);
        Assert.Empty(next.ActivePayrollRun!.Obligations);
        Assert.True(next.ActivePayrollRun.AttemptSubmitted);
        Assert.Equal(0, next.NodeProgress.GetValueOrDefault(MagicAgencySeed.PayrollNodeId, 0));
        Assert.Equal(1, next.NodeCycles.GetValueOrDefault(MagicAgencySeed.PayrollNodeId, 0));
    }

    [Fact]
    public void Payday_EffectiveEffortScalesWithObligationCount()
    {
        var workerId = MagicAgencySeed.ActorId;
        var bossId = MagicAgencySeed.BossActorId;
        var unpaidId = new ActorId("unpaid");

        // baseEffort 1 + perActorEffort 1 × 2 obligations = 3 required; progress start 0 + gain 2.5 → under effort.
        var actors = ImmutableDictionary<ActorId, Actor>.Empty
            .Add(
                workerId,
                new Actor(
                    workerId,
                    Capacity: 1.0m,
                    ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 2.5),
                    Wage: 7))
            .Add(
                bossId,
                new Actor(
                    bossId,
                    Capacity: 1.0m,
                    ImmutableDictionary<string, double>.Empty,
                    Wage: 3))
            .Add(
                unpaidId,
                new Actor(
                    unpaidId,
                    Capacity: 1.0m,
                    ImmutableDictionary<string, double>.Empty));

        var obligations = ImmutableArray.Create(
            new PayrollObligation(workerId, 7),
            new PayrollObligation(bossId, 3));

        var underEffort = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(workerId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            ActivePayrollRun = new PayrollRun(
                0,
                obligations,
                ImmutableHashSet<ActorId>.Empty,
                AttemptSubmitted: false),
        };

        var afterUnder = ProductionTick.AdvanceTick(underEffort);
        Assert.Empty(afterUnder.PendingMoneyMoves);
        Assert.Equal(2.5, afterUnder.NodeProgress[MagicAgencySeed.PayrollNodeId]);
        Assert.False(afterUnder.ActivePayrollRun!.AttemptSubmitted);

        // Same roster, higher payroll stat so gain covers effective effort 3.
        var paidEnough = underEffort with
        {
            Actors = actors.SetItem(
                workerId,
                new Actor(
                    workerId,
                    Capacity: 1.0m,
                    ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 5),
                    Wage: 7)),
        };

        var afterPayday = ProductionTick.AdvanceTick(paidEnough);
        Assert.Single(afterPayday.PendingMoneyMoves);
        Assert.Equal(10, afterPayday.PendingMoneyMoves[0].Amount);
        Assert.True(afterPayday.ActivePayrollRun!.AttemptSubmitted);
        Assert.Equal(0, afterPayday.NodeProgress.GetValueOrDefault(MagicAgencySeed.PayrollNodeId, 0));
    }

    [Fact]
    public void Payday_OneAttemptOnly_SecondTickDoesNotReenqueue()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Payroll, 5),
                Wage: 10));

        var obligations = ImmutableArray.Create(new PayrollObligation(MagicAgencySeed.ActorId, 10));
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Tick = 27,
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.PayrollNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty.Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(100)),
            ActivePayrollRun = new PayrollRun(
                0,
                obligations,
                ImmutableHashSet<ActorId>.Empty,
                AttemptSubmitted: false),
        };

        var afterFirst = ProductionTick.AdvanceTick(state);
        Assert.Single(afterFirst.PendingMoneyMoves);

        var afterSecond = ProductionTick.AdvanceTick(afterFirst);
        Assert.Single(afterSecond.PendingMoneyMoves);
        Assert.True(afterSecond.ActivePayrollRun!.AttemptSubmitted);
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
                MagicAgencySeed.EnchantNodeId,
                MagicAgencySeed.EnchantmentPortId)).Hash,
            ((SignalValue.Enchantment)Signal(
                fromReverse,
                MagicAgencySeed.EnchantNodeId,
                MagicAgencySeed.EnchantmentPortId)).Hash);
        Assert.Equal(fromForward.PendingMoneyMoves, fromReverse.PendingMoneyMoves);
        Assert.Equal(fromForward.NextUnitId, fromReverse.NextUnitId);
    }

    [Fact]
    public void Enchantment_MutateSellAndReduceFallacy_MatchDocumentedFormulas()
    {
        var genesis = EnchantmentBlock.CreateGenesis();
        var (once, next) = EnchantmentOps.Mutate(genesis, DefaultConfigs.Enchant, 1);
        Assert.Equal(10, once.VolumeCount);
        Assert.Equal(10.0, once.Darkness);
        Assert.Equal(1.0, once.Fallacy);
        Assert.Equal(9, new SignalValue.Enchantment(once).SellPayout(DefaultConfigs.Sell));

        var reduced = EnchantmentOps.ReduceFallacy(once, 5);
        Assert.Equal(0, reduced.Fallacy);
        Assert.Equal(0, EnchantmentOps.ReduceFallacy(reduced, 99).Fallacy);
        Assert.Equal(11UL, next);
    }

    [Fact]
    public void LoadFromBaseDirectory_MatchesCommittedJsonDefaults()
    {
        var loaded = NodeTypeConfigLoader.LoadFromBaseDirectory();

        Assert.Equal(10, loaded.Enchant.Effort);
        Assert.Equal(0.3, loaded.Enchant.DesignDarknessDelta);
        Assert.Equal(10, loaded.Testing.Effort);
        Assert.Equal(5, loaded.Testing.FallacyReduction);
        Assert.Equal(3, loaded.Design.Effort);
        Assert.Equal(1, loaded.Design.DesignDelta);
        Assert.Equal(0.9, loaded.Design.DarknessReduction);
        Assert.Equal(1, loaded.Merge.Effort);
        Assert.Equal(10, loaded.Sell.Effort);
        Assert.Equal(1, loaded.Treasury.Effort);
        Assert.Equal("month", loaded.Payroll.Schedule.PeriodUnit);
        Assert.Equal("day", loaded.Payroll.Schedule.PositionUnit);
        Assert.Equal(0, loaded.Payroll.Schedule.StartLead);
        Assert.Equal(10, loaded.Payroll.Schedule.DueDay);
        Assert.Equal(1, loaded.Payroll.BaseEffort);
        Assert.Equal(1, loaded.Payroll.PerActorEffort);
    }

    [Fact]
    public void ActorConfigLoader_LoadsInternWithStatsAndWage()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();
        var intern = actors[MagicAgencySeed.ActorId];
        Assert.Equal(1.0m, intern.Capacity);
        Assert.Equal(2, intern.Wage);
        Assert.Equal(10, intern.Stats[ActorStatKeys.Enchanting]);
        Assert.Equal(10, intern.Stats[ActorStatKeys.Sales]);
    }

    [Fact]
    public void ActorConfigLoader_LoadsBossWithStatsAndWage()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();
        var boss = actors[MagicAgencySeed.BossActorId];
        Assert.Equal(1.0m, boss.Capacity);
        Assert.Equal(3, boss.Wage);
        Assert.Equal(10, boss.Stats[ActorStatKeys.Sales]);
        Assert.Equal(10, boss.Stats[ActorStatKeys.Payroll]);
        Assert.Equal(10, boss.Stats[ActorStatKeys.Treasury]);
    }

    [Fact]
    public void ActorConfigLoader_EveryPoolActorHasAWage()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();
        var pool = ActorPoolLoader.LoadFromBaseDirectory();

        Assert.NotEmpty(pool);
        foreach (var actorId in pool)
        {
            Assert.True(
                actors[actorId].Wage is > 0,
                $"Actor '{actorId.Value}' must define a positive wage to participate in payroll.");
        }
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
    public void PaidActors_AndEffectivePayrollEffort_ExcludeUnwaged()
    {
        var paid = new Actor(
            new ActorId("paid"),
            Capacity: 1.0m,
            ImmutableDictionary<string, double>.Empty,
            Wage: 25);
        var unpaid = new Actor(
            new ActorId("unpaid"),
            Capacity: 1.0m,
            ImmutableDictionary<string, double>.Empty);

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = ImmutableDictionary<ActorId, Actor>.Empty
                .Add(paid.Id, paid)
                .Add(unpaid.Id, unpaid),
        };

        var paidActors = ProductionTick.PaidActors(state).ToList();
        Assert.Single(paidActors);
        Assert.Equal(paid.Id, paidActors[0].Id);
        Assert.Equal(25, paidActors[0].Wage);
        Assert.Equal(2, ProductionTick.EffectivePayrollEffort(DefaultConfigs.Payroll, paidActorCount: 1));
        Assert.Equal(3, ProductionTick.EffectivePayrollEffort(DefaultConfigs.Payroll, paidActorCount: 2));
    }

    [Fact]
    public void FanIn_RelatedHistories_CombinesIntoDestination()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Testing, 4)));

        var genesis = EnchantmentBlock.CreateGenesis();
        var (child, nextId) = EnchantmentOps.Mutate(genesis, DefaultConfigs.Enchant, 1);
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TestingNodeId)),
            PortSignals = ImmutableDictionary<PortKey, SignalValue>.Empty
                .Add(
                    new PortKey(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
                    new SignalValue.Enchantment(child))
                .Add(
                    new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
                    new SignalValue.Enchantment(genesis))
                .Add(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(100)),
            EnchantmentBlocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
                .Add(genesis.Hash, genesis)
                .Add(child.Hash, child),
            NextUnitId = nextId,
        };

        var next = ProductionTick.AdvanceTick(state);

        Assert.Equal(
            child.Hash,
            ((SignalValue.Enchantment)Signal(
                next,
                MagicAgencySeed.SellNodeId,
                MagicAgencySeed.EnchantmentPortId)).Hash);
    }

    [Fact]
    public void FanIn_IncompatibleHistories_EmptiesDestination()
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

        Assert.False(
            next.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId)));
    }

    private static SignalValue Signal(GameState state, NodeId node, PortId port) =>
        state.PortSignals[new PortKey(node, port)];

    private static NodeIoRow Row(ProductionTickResult result, NodeId node) =>
        result.Nodes.Single(r => r.NodeId == node);
}
