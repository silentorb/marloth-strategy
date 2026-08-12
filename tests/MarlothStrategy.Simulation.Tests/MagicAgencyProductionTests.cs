using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Simulation.Tests;

public sealed class MagicAgencyProductionTests
{
    private static readonly NodeTypeConfigs DefaultConfigs = new(
        new EnchantNodeConfig(
            Cost: 20,
            Effort: 10,
            VolumeDelta: 10,
            DarknessDelta: 1,
            FallacyConstant: 1),
        new SellNodeConfig(Cost: 20, Effort: 10, PayoutFloor: 0));

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
    public void Seed_HasFanOutEnchantmentEdgesMoneyTreasuryAndDualAssignment()
    {
        var state = Seed();

        Assert.Equal(0, state.Tick);
        Assert.Single(state.Actors);
        Assert.True(state.Actors.ContainsKey(MagicAgencySeed.ActorId));
        Assert.Equal(1.0m, state.Actors[MagicAgencySeed.ActorId].Capacity);
        Assert.Equal(10, state.Actors[MagicAgencySeed.ActorId].Stats[ActorStatKeys.Enchanting]);
        Assert.Equal(10, state.Actors[MagicAgencySeed.ActorId].Stats[ActorStatKeys.Sales]);

        Assert.Equal(2, state.Graph.Nodes.Count);
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.EnchantNodeId));
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.SellNodeId));

        Assert.Equal(3, state.Graph.Edges.Count);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.From.Port == MagicAgencySeed.EnchantmentPortId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Port == MagicAgencySeed.EnchantmentPortId);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.From.Port == MagicAgencySeed.EnchantmentPortId
                 && e.To.Node == MagicAgencySeed.SellNodeId
                 && e.To.Port == MagicAgencySeed.EnchantmentPortId);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.SellNodeId
                 && e.From.Port == MagicAgencySeed.MoneyPortId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Port == MagicAgencySeed.MoneyPortId);

        Assert.Equal(2, state.Assignments.Length);
        Assert.Contains(
            state.Assignments,
            a => a.ActorId == MagicAgencySeed.ActorId && a.NodeId == MagicAgencySeed.EnchantNodeId);
        Assert.Contains(
            state.Assignments,
            a => a.ActorId == MagicAgencySeed.ActorId && a.NodeId == MagicAgencySeed.SellNodeId);

        Assert.Equal(
            new SignalValue.Enchantment(0, 0, 0),
            Signal(state, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(state, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.False(
            state.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId)));
        Assert.Equal(DefaultConfigs, state.NodeConfigs);
        Assert.Empty(state.NodeProgress);
    }

    [Fact]
    public void Seed_FirstTick_AssignsOnlyEnchantWhenSellHasNoInput()
    {
        var state = Seed();
        var effort = ProductionTick.ResolveEffortByNode(state);

        Assert.Equal(1.0m, effort[MagicAgencySeed.EnchantNodeId]);
        Assert.False(effort.ContainsKey(MagicAgencySeed.SellNodeId));
    }

    [Fact]
    public void FirstTick_EnchantMutatesWithFullEffort_ChargesCost_SellIdle()
    {
        var state = Seed();
        var result = ProductionTick.AdvanceTickWithReport(state);
        var next = result.State;

        Assert.Equal(1, next.Tick);
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Money(80),
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(0, next.NodeProgress.GetValueOrDefault(MagicAgencySeed.EnchantNodeId));

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.Equal(1.0m, enchant.Effort);
        Assert.Equal(new SignalValue.Enchantment(0, 0, 0), enchant.Available);
        Assert.True(enchant.Consumed);
        Assert.Null(enchant.Residual);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), enchant.Produced);

        var sell = Row(result, MagicAgencySeed.SellNodeId);
        Assert.Equal(0m, sell.Effort);
        Assert.Null(sell.Available);
        Assert.False(sell.Consumed);
        Assert.Null(sell.Produced);
    }

    [Fact]
    public void SecondTick_BothAssignedHalfEffort_PassThroughAndSellProgress()
    {
        var afterOne = ProductionTick.AdvanceTick(Seed());
        var result = ProductionTick.AdvanceTickWithReport(afterOne);
        var afterTwo = result.State;

        Assert.Equal(2, afterTwo.Tick);
        // Enchant: progress 0+5=5 < 10 → pass-through (10,1,1); sell holds residual so fan-out to sell skipped
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(afterTwo, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(afterTwo, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Money(80),
            Signal(afterTwo, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(5, afterTwo.NodeProgress[MagicAgencySeed.EnchantNodeId]);
        Assert.Equal(5, afterTwo.NodeProgress[MagicAgencySeed.SellNodeId]);

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.Equal(0.5m, enchant.Effort);
        Assert.True(enchant.Consumed);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), enchant.Produced);

        var sell = Row(result, MagicAgencySeed.SellNodeId);
        Assert.Equal(0.5m, sell.Effort);
        Assert.False(sell.Consumed);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), sell.Residual);
    }

    [Fact]
    public void ThirdTick_EnchantMutatesAgain_SellPaysAndChargesCost()
    {
        var state = Seed();
        var afterTwo = ProductionTick.AdvanceTick(ProductionTick.AdvanceTick(state));
        var result = ProductionTick.AdvanceTickWithReport(afterTwo);
        var afterThree = result.State;

        Assert.Equal(3, afterThree.Tick);
        Assert.Equal(
            new SignalValue.Enchantment(20, 2, 3),
            Signal(afterThree, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Enchantment(20, 2, 3),
            Signal(afterThree, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        // treasury 80 - 20 (enchant) - 20 (sell) + 9 (payout) = 49
        Assert.Equal(
            new SignalValue.Money(49),
            Signal(afterThree, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));

        var sell = Row(result, MagicAgencySeed.SellNodeId);
        Assert.True(sell.Consumed);
        Assert.Equal(new SignalValue.Money(9), sell.Produced);
        Assert.Equal(0, afterThree.NodeProgress[MagicAgencySeed.EnchantNodeId]);
        Assert.Equal(0, afterThree.NodeProgress[MagicAgencySeed.SellNodeId]);
    }

    [Fact]
    public void AdvanceTick_IsIndependentOfNodeIterationOrder()
    {
        var state = Seed();
        var forward = new[] { MagicAgencySeed.EnchantNodeId, MagicAgencySeed.SellNodeId };
        var reverse = new[] { MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantNodeId };

        var fromForward = ProductionTick.AdvanceTick(state, forward);
        var fromReverse = ProductionTick.AdvanceTick(state, reverse);

        Assert.Equal(fromForward.Tick, fromReverse.Tick);
        Assert.Equal(
            Signal(fromForward, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            Signal(fromReverse, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            Signal(fromForward, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
            Signal(fromReverse, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            Signal(fromForward, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId),
            Signal(fromReverse, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));

        var forward2 = ProductionTick.AdvanceTick(fromForward, forward);
        var reverse2 = ProductionTick.AdvanceTick(fromReverse, reverse);
        var forward3 = ProductionTick.AdvanceTick(forward2, forward);
        var reverse3 = ProductionTick.AdvanceTick(reverse2, reverse);

        Assert.Equal(
            Signal(forward3, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            Signal(reverse3, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            Signal(forward3, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
            Signal(reverse3, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            Signal(forward3, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId),
            Signal(reverse3, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
    }

    [Fact]
    public void Enchantment_MutateAndSellPayout_MatchDocumentedFormulas()
    {
        var start = new SignalValue.Enchantment(0, 0, 0);
        var once = start.Mutate(DefaultConfigs.Enchant);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), once);
        Assert.Equal(9, once.SellPayout(DefaultConfigs.Sell));

        var twice = once.Mutate(DefaultConfigs.Enchant);
        Assert.Equal(new SignalValue.Enchantment(20, 2, 3), twice);
        Assert.Equal(17, twice.SellPayout(DefaultConfigs.Sell));
    }

    [Fact]
    public void LoadFromBaseDirectory_MatchesCommittedJsonDefaults()
    {
        var loaded = NodeTypeConfigLoader.LoadFromBaseDirectory();

        Assert.Equal(20, loaded.Enchant.Cost);
        Assert.Equal(10, loaded.Enchant.Effort);
        Assert.Equal(10, loaded.Enchant.VolumeDelta);
        Assert.Equal(1, loaded.Enchant.DarknessDelta);
        Assert.Equal(1, loaded.Enchant.FallacyConstant);
        Assert.Equal(20, loaded.Sell.Cost);
        Assert.Equal(10, loaded.Sell.Effort);
        Assert.Equal(0, loaded.Sell.PayoutFloor);
    }

    [Fact]
    public void ActorConfigLoader_LoadsInternWithStats()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();

        Assert.True(actors.ContainsKey(MagicAgencySeed.ActorId));
        var intern = actors[MagicAgencySeed.ActorId];
        Assert.Equal(1.0m, intern.Capacity);
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
            ActorStatKeys.DefaultEnchanting,
            ProductionTick.GetStat(actor, ActorStatKeys.Enchanting, ActorStatKeys.DefaultEnchanting));
        Assert.Equal(
            ActorStatKeys.DefaultSales,
            ProductionTick.GetStat(actor, ActorStatKeys.Sales, ActorStatKeys.DefaultSales));
    }

    [Fact]
    public void MultipleEnchantApplications_WhenProgressIsMultipleOfEffort()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Enchanting, 30)));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, actors);
        var result = ProductionTick.AdvanceTickWithReport(state);
        var next = result.State;

        // 3 mutations: (0,0,0) → (10,1,1) → (20,2,3) → (30,3,6); cost 60; money 40
        Assert.Equal(
            new SignalValue.Enchantment(30, 3, 6),
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Money(40),
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(0, next.NodeProgress.GetValueOrDefault(MagicAgencySeed.EnchantNodeId));
    }

    [Fact]
    public void InsufficientTreasury_SkipsApplicationWithoutConsumingProgress()
    {
        var configs = new NodeTypeConfigs(
            new EnchantNodeConfig(
                Cost: 1000,
                Effort: 10,
                VolumeDelta: 10,
                DarknessDelta: 1,
                FallacyConstant: 1),
            DefaultConfigs.Sell);

        var state = Seed(configs);
        var result = ProductionTick.AdvanceTickWithReport(state);
        var next = result.State;

        Assert.Equal(
            new SignalValue.Enchantment(0, 0, 0),
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(10, next.NodeProgress[MagicAgencySeed.EnchantNodeId]);

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.True(enchant.Consumed);
        Assert.Equal(new SignalValue.Enchantment(0, 0, 0), enchant.Produced);
    }

    [Fact]
    public void Occupancy_SellResidualBlocksFanOutCopy()
    {
        var afterOne = ProductionTick.AdvanceTick(Seed());
        var afterTwo = ProductionTick.AdvanceTick(afterOne);

        // Sell still holds (10,1,1); enchant pass-through could not overwrite sell
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(afterTwo, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(afterTwo, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
    }

    [Fact]
    public void TweakedConfig_ChangesMutateAndSellPayout()
    {
        var configs = new NodeTypeConfigs(
            new EnchantNodeConfig(
                Cost: 0,
                Effort: 10,
                VolumeDelta: 5,
                DarknessDelta: 2,
                FallacyConstant: 0),
            new SellNodeConfig(Cost: 0, Effort: 10, PayoutFloor: 3));

        var state = Seed(configs);
        // tick1: mutate to (5,2,0); tick2: pass-through + sell progress 5; tick3: mutate (10,4,2) + sell (5,2,0)→5
        var afterThree = ProductionTick.AdvanceTick(
            ProductionTick.AdvanceTick(ProductionTick.AdvanceTick(state)));

        Assert.Equal(
            new SignalValue.Enchantment(10, 4, 2),
            Signal(afterThree, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Money(105),
            Signal(afterThree, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
    }

    private static SignalValue Signal(GameState state, NodeId node, PortId port) =>
        state.PortSignals[new PortKey(node, port)];

    private static NodeIoRow Row(ProductionTickResult result, NodeId node) =>
        result.Nodes.Single(r => r.NodeId == node);
}
