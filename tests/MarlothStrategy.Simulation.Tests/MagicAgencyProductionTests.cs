using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Simulation.Tests;

public sealed class MagicAgencyProductionTests
{
    [Fact]
    public void Seed_HasOneActorTwoNodesCycleAndDualAssignment()
    {
        var state = MagicAgencySeed.CreateInitialState();

        Assert.Equal(0, state.Tick);
        Assert.Single(state.Actors);
        Assert.True(state.Actors.ContainsKey(MagicAgencySeed.ActorId));
        Assert.Equal(1.0m, state.Actors[MagicAgencySeed.ActorId].Capacity);

        Assert.Equal(2, state.Graph.Nodes.Count);
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.EnchantNodeId));
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.SellNodeId));

        Assert.Equal(2, state.Graph.Edges.Count);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.From.Port == MagicAgencySeed.EnchantmentsPortId
                 && e.To.Node == MagicAgencySeed.SellNodeId
                 && e.To.Port == MagicAgencySeed.EnchantmentsPortId);
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
            100,
            Quantity(state, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(
            0,
            Quantity(state, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentsPortId));
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
    public void FirstTick_EnchantConvertsTenMoney_SellIdle()
    {
        var state = MagicAgencySeed.CreateInitialState();
        var result = ProductionTick.AdvanceTickWithReport(state);
        var next = result.State;

        Assert.Equal(1, next.Tick);
        Assert.Equal(
            90,
            Quantity(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(
            10,
            Quantity(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentsPortId));

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.Equal(0.5m, enchant.Effort);
        Assert.Equal(100, enchant.Available);
        Assert.Equal(10, enchant.Consumed);
        Assert.Equal(90, enchant.Residual);
        Assert.Equal(10, enchant.Produced);
        Assert.Equal(SignalTypes.Money, enchant.InputType);
        Assert.Equal(SignalTypes.Enchantments, enchant.OutputType);

        var sell = Row(result, MagicAgencySeed.SellNodeId);
        Assert.Equal(0.5m, sell.Effort);
        Assert.Equal(0, sell.Available);
        Assert.Equal(0, sell.Consumed);
        Assert.Equal(0, sell.Residual);
        Assert.Equal(0, sell.Produced);
    }

    [Fact]
    public void SecondTick_SellConvertsRoutedEnchantments_MoneyReturnsTowardEnchant()
    {
        var state = MagicAgencySeed.CreateInitialState();
        var afterOne = ProductionTick.AdvanceTick(state);
        var result = ProductionTick.AdvanceTickWithReport(afterOne);
        var afterTwo = result.State;

        Assert.Equal(2, afterTwo.Tick);
        // enchant: 90 available → process 10 → residual 80; sell routes +10 money → 90
        Assert.Equal(
            90,
            Quantity(afterTwo, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        // sell: 10 available → process 10 → residual 0; enchant routes +10 enchantments → 10
        Assert.Equal(
            10,
            Quantity(afterTwo, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentsPortId));

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.Equal(90, enchant.Available);
        Assert.Equal(10, enchant.Consumed);
        Assert.Equal(80, enchant.Residual);
        Assert.Equal(10, enchant.Produced);

        var sell = Row(result, MagicAgencySeed.SellNodeId);
        Assert.Equal(10, sell.Available);
        Assert.Equal(10, sell.Consumed);
        Assert.Equal(0, sell.Residual);
        Assert.Equal(10, sell.Produced);
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
            Quantity(fromForward, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId),
            Quantity(fromReverse, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(
            Quantity(fromForward, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentsPortId),
            Quantity(fromReverse, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentsPortId));

        var forward2 = ProductionTick.AdvanceTick(fromForward, forward);
        var reverse2 = ProductionTick.AdvanceTick(fromReverse, reverse);

        Assert.Equal(
            Quantity(forward2, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId),
            Quantity(reverse2, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(
            Quantity(forward2, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentsPortId),
            Quantity(reverse2, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentsPortId));
    }

    private static int Quantity(GameState state, NodeId node, PortId port) =>
        state.PortSignals[new PortKey(node, port)].Quantity;

    private static NodeIoRow Row(ProductionTickResult result, NodeId node) =>
        result.Nodes.Single(r => r.NodeId == node);
}
