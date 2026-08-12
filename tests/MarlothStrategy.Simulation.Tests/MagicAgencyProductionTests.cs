using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Simulation.Tests;

public sealed class MagicAgencyProductionTests
{
    [Fact]
    public void Seed_HasFanOutEnchantmentEdgesMoneyTreasuryAndDualAssignment()
    {
        var state = MagicAgencySeed.CreateInitialState();

        Assert.Equal(0, state.Tick);
        Assert.Single(state.Actors);
        Assert.True(state.Actors.ContainsKey(MagicAgencySeed.ActorId));
        Assert.Equal(1.0m, state.Actors[MagicAgencySeed.ActorId].Capacity);

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
    }

    [Fact]
    public void Seed_SplitsEffortEquallyAcrossAssignments()
    {
        var state = MagicAgencySeed.CreateInitialState();
        var effort = ProductionTick.ResolveEffortByNode(state);

        Assert.Equal(0.5m, effort[MagicAgencySeed.EnchantNodeId]);
        Assert.Equal(0.5m, effort[MagicAgencySeed.SellNodeId]);
    }

    [Fact]
    public void FirstTick_EnchantMutatesAndFansOut_SellIdle_MoneyUnchanged()
    {
        var state = MagicAgencySeed.CreateInitialState();
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
            new SignalValue.Money(100),
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.Equal(0.5m, enchant.Effort);
        Assert.Equal(new SignalValue.Enchantment(0, 0, 0), enchant.Available);
        Assert.True(enchant.Consumed);
        Assert.Null(enchant.Residual);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), enchant.Produced);
        Assert.Equal(SignalTypes.Enchantment, enchant.InputType);
        Assert.Equal(SignalTypes.Enchantment, enchant.OutputType);

        var sell = Row(result, MagicAgencySeed.SellNodeId);
        Assert.Equal(0.5m, sell.Effort);
        Assert.Null(sell.Available);
        Assert.False(sell.Consumed);
        Assert.Null(sell.Residual);
        Assert.Null(sell.Produced);
    }

    [Fact]
    public void SecondTick_SellPaysVolumeMinusFallacy_EnchantMutatesAgain()
    {
        var state = MagicAgencySeed.CreateInitialState();
        var afterOne = ProductionTick.AdvanceTick(state);
        var result = ProductionTick.AdvanceTickWithReport(afterOne);
        var afterTwo = result.State;

        Assert.Equal(2, afterTwo.Tick);
        Assert.Equal(
            new SignalValue.Enchantment(20, 2, 3),
            Signal(afterTwo, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Enchantment(20, 2, 3),
            Signal(afterTwo, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        // sell pays max(0, 10 - 1) = 9 onto treasury 100
        Assert.Equal(
            new SignalValue.Money(109),
            Signal(afterTwo, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), enchant.Available);
        Assert.True(enchant.Consumed);
        Assert.Null(enchant.Residual);
        Assert.Equal(new SignalValue.Enchantment(20, 2, 3), enchant.Produced);

        var sell = Row(result, MagicAgencySeed.SellNodeId);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), sell.Available);
        Assert.True(sell.Consumed);
        Assert.Null(sell.Residual);
        Assert.Equal(new SignalValue.Money(9), sell.Produced);
    }

    [Fact]
    public void AdvanceTick_IsIndependentOfNodeIterationOrder()
    {
        var state = MagicAgencySeed.CreateInitialState();
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

        Assert.Equal(
            Signal(forward2, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
            Signal(reverse2, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            Signal(forward2, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId),
            Signal(reverse2, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            Signal(forward2, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId),
            Signal(reverse2, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
    }

    [Fact]
    public void Enchantment_MutateAndSellPayout_MatchDocumentedFormulas()
    {
        var start = new SignalValue.Enchantment(0, 0, 0);
        var once = start.Mutate();
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), once);
        Assert.Equal(9, once.SellPayout());

        var twice = once.Mutate();
        Assert.Equal(new SignalValue.Enchantment(20, 2, 3), twice);
        Assert.Equal(17, twice.SellPayout());
    }

    private static SignalValue Signal(GameState state, NodeId node, PortId port) =>
        state.PortSignals[new PortKey(node, port)];

    private static NodeIoRow Row(ProductionTickResult result, NodeId node) =>
        result.Nodes.Single(r => r.NodeId == node);
}
