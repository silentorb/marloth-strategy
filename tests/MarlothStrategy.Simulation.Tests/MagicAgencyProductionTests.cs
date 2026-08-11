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
            10m,
            Quantity(state, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(
            0m,
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
    public void FirstTick_EnchantConvertsOneMoney_SellIdle()
    {
        var state = MagicAgencySeed.CreateInitialState();
        var next = ProductionTick.AdvanceTick(state);

        Assert.Equal(1, next.Tick);
        Assert.Equal(
            9m,
            Quantity(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(
            1m,
            Quantity(next, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentsPortId));
    }

    [Fact]
    public void SecondTick_SellConvertsRoutedEnchantment_MoneyReturnsTowardEnchant()
    {
        var state = MagicAgencySeed.CreateInitialState();
        var afterOne = ProductionTick.AdvanceTick(state);
        var afterTwo = ProductionTick.AdvanceTick(afterOne);

        Assert.Equal(2, afterTwo.Tick);
        // enchant: 9 available → process 1 → residual 8; sell routes +1 money → 9
        Assert.Equal(
            9m,
            Quantity(afterTwo, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId));
        // sell: 1 available → process 1 → residual 0; enchant routes +1 enchantment → 1
        Assert.Equal(
            1m,
            Quantity(afterTwo, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentsPortId));
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

    private static decimal Quantity(GameState state, NodeId node, PortId port) =>
        state.PortSignals[new PortKey(node, port)].Quantity;
}
