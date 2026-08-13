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
        new SellNodeConfig(Effort: 10, PayoutFloor: 0),
        new PayrollNodeConfig(DefaultWage: 10, Period: 5));

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
    public void Seed_HasFanOutEnchantmentEdgesTreasuryAndPayroll()
    {
        var state = Seed();

        Assert.Equal(0, state.Tick);
        Assert.Single(state.Actors);
        Assert.True(state.Actors.ContainsKey(MagicAgencySeed.ActorId));
        Assert.Equal(1.0m, state.Actors[MagicAgencySeed.ActorId].Capacity);
        Assert.Null(state.Actors[MagicAgencySeed.ActorId].Wage);
        Assert.Equal(10, state.Actors[MagicAgencySeed.ActorId].Stats[ActorStatKeys.Enchanting]);
        Assert.Equal(10, state.Actors[MagicAgencySeed.ActorId].Stats[ActorStatKeys.Sales]);

        Assert.Equal(4, state.Graph.Nodes.Count);
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.EnchantNodeId));
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.SellNodeId));
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.TreasuryNodeId));
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.PayrollNodeId));

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
                 && e.To.Node == MagicAgencySeed.TreasuryNodeId
                 && e.To.Port == MagicAgencySeed.MoneyPortId);

        var enchantType = state.Catalog.Get(MagicAgencySeed.EnchantTypeId);
        Assert.False(enchantType.Inputs.ContainsKey(MagicAgencySeed.MoneyPortId));
        Assert.False(enchantType.Outputs.ContainsKey(MagicAgencySeed.MoneyPortId));
        Assert.True(enchantType.Outputs.ContainsKey(MagicAgencySeed.EnchantmentPortId));

        var sellType = state.Catalog.Get(MagicAgencySeed.SellTypeId);
        Assert.False(sellType.Inputs.ContainsKey(MagicAgencySeed.MoneyPortId));
        Assert.True(sellType.Outputs.ContainsKey(MagicAgencySeed.MoneyPortId));

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
            Signal(state, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.False(
            state.PortSignals.ContainsKey(
                new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId)));
        Assert.Equal(5, state.NodeTimers[MagicAgencySeed.PayrollNodeId]);
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
    public void FirstTick_EnchantMutates_TreasuryUnchanged_TimerDecrements()
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
            new SignalValue.Money(100),
            Signal(next, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(0, next.NodeProgress.GetValueOrDefault(MagicAgencySeed.EnchantNodeId));
        Assert.Equal(4, next.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.Single(next.Actors);

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
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(afterTwo, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(afterTwo, MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(afterTwo, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(5, afterTwo.NodeProgress[MagicAgencySeed.EnchantNodeId]);
        Assert.Equal(5, afterTwo.NodeProgress[MagicAgencySeed.SellNodeId]);
        Assert.Equal(3, afterTwo.NodeTimers[MagicAgencySeed.PayrollNodeId]);

        var enchant = Row(result, MagicAgencySeed.EnchantNodeId);
        Assert.Equal(0.5m, enchant.Effort);
        Assert.True(enchant.Consumed);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), enchant.Produced);

        var sell = Row(result, MagicAgencySeed.SellNodeId);
        Assert.Equal(0.5m, sell.Effort);
        Assert.False(sell.Consumed);
        Assert.Equal(new SignalValue.Enchantment(10, 1, 1), sell.Residual);
        Assert.Null(sell.Produced);
    }

    [Fact]
    public void ThirdTick_EnchantMutatesAgain_SellAddsPayoutToTreasury()
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

        var sell = Row(result, MagicAgencySeed.SellNodeId);
        Assert.True(sell.Consumed);
        Assert.Equal(new SignalValue.Money(9), sell.Produced);

        // treasury 100 + payout 9
        Assert.Equal(
            new SignalValue.Money(109),
            Signal(afterThree, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(0, afterThree.NodeProgress[MagicAgencySeed.EnchantNodeId]);
        Assert.Equal(0, afterThree.NodeProgress[MagicAgencySeed.SellNodeId]);
        Assert.Equal(2, afterThree.NodeTimers[MagicAgencySeed.PayrollNodeId]);
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
            MagicAgencySeed.TreasuryNodeId,
        };
        var reverse = forward.Reverse().ToArray();

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
            Signal(fromForward, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
            Signal(fromReverse, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(
            fromForward.NodeTimers[MagicAgencySeed.PayrollNodeId],
            fromReverse.NodeTimers[MagicAgencySeed.PayrollNodeId]);

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
            Signal(forward3, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
            Signal(reverse3, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
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

        Assert.Equal(10, loaded.Enchant.Effort);
        Assert.Equal(10, loaded.Enchant.VolumeDelta);
        Assert.Equal(1, loaded.Enchant.DarknessDelta);
        Assert.Equal(1, loaded.Enchant.FallacyConstant);
        Assert.Equal(10, loaded.Sell.Effort);
        Assert.Equal(0, loaded.Sell.PayoutFloor);
        Assert.Equal(10, loaded.Payroll.DefaultWage);
        Assert.Equal(5, loaded.Payroll.Period);
    }

    [Fact]
    public void ActorConfigLoader_LoadsInternWithStatsAndNoWage()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();

        Assert.True(actors.ContainsKey(MagicAgencySeed.ActorId));
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
            ActorStatKeys.DefaultEnchanting,
            ProductionTick.GetStat(actor, ActorStatKeys.Enchanting, ActorStatKeys.DefaultEnchanting));
        Assert.Equal(
            ActorStatKeys.DefaultSales,
            ProductionTick.GetStat(actor, ActorStatKeys.Sales, ActorStatKeys.DefaultSales));
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

        // 3 mutations: (0,0,0) → (10,1,1) → (20,2,3) → (30,3,6); treasury unchanged
        Assert.Equal(
            new SignalValue.Enchantment(30, 3, 6),
            Signal(next, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(next, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(0, next.NodeProgress.GetValueOrDefault(MagicAgencySeed.EnchantNodeId));
    }

    [Fact]
    public void Occupancy_SellResidualBlocksFanOutCopy()
    {
        var afterOne = ProductionTick.AdvanceTick(Seed());
        var afterTwo = ProductionTick.AdvanceTick(afterOne);

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
                Effort: 10,
                VolumeDelta: 5,
                DarknessDelta: 2,
                FallacyConstant: 0),
            new SellNodeConfig(Effort: 10, PayoutFloor: 3),
            new PayrollNodeConfig(DefaultWage: 10, Period: 5));

        var state = Seed(configs);
        // tick1: mutate to (5,2,0); tick2: pass-through + sell progress 5; tick3: mutate (10,4,2) + sell (5,2,0)→5
        var afterThree = ProductionTick.AdvanceTick(
            ProductionTick.AdvanceTick(ProductionTick.AdvanceTick(state)));

        Assert.Equal(
            new SignalValue.Enchantment(10, 4, 2),
            Signal(afterThree, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Money(105),
            Signal(afterThree, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
    }

    [Fact]
    public void Payday_Success_DebitsTreasuryAndResetsTimer()
    {
        var state = Seed();
        GameState current = state;
        for (var i = 0; i < 5; i++)
        {
            current = ProductionTick.AdvanceTick(current);
        }

        Assert.Equal(5, current.Tick);
        Assert.Equal(5, current.NodeTimers[MagicAgencySeed.PayrollNodeId]);
        Assert.Single(current.Actors);
        Assert.Equal(2, current.Assignments.Length);
        // After 5 ticks: sales on tick 3 (+9) and tick 5 (+17) → 100+9+17=126, then wage -10 → 116
        Assert.Equal(
            new SignalValue.Money(116),
            Signal(current, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
    }

    [Fact]
    public void Payday_UsesActorWageOverride()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty
                    .Add(ActorStatKeys.Enchanting, 10)
                    .Add(ActorStatKeys.Sales, 10),
                Wage: 7));

        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, actors);
        // Avoid sales complicating the balance: only check wage debit on a quiet payday.
        // Set treasury high and advance 5 ticks — sales still happen, so compute expected:
        // tick3 +9, tick5 +17 before payday → 126; wage 7 → 119
        GameState current = state;
        for (var i = 0; i < 5; i++)
        {
            current = ProductionTick.AdvanceTick(current);
        }

        Assert.Equal(
            new SignalValue.Money(119),
            Signal(current, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Single(current.Actors);
    }

    [Fact]
    public void Payday_Failure_QuitsAllActorsWithoutDebiting()
    {
        var state = Seed();
        var broke = state with
        {
            PortSignals = state.PortSignals.SetItem(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(0)),
        };

        // Advance to payday with empty treasury and no sales income possible if we also clear
        // enchantment? Actually sales still run from seed flow. Better: set money to 0 right
        // before the payday tick after draining, or use a high wage and low treasury.
        var highWage = new NodeTypeConfigs(
            DefaultConfigs.Enchant,
            DefaultConfigs.Sell,
            new PayrollNodeConfig(DefaultWage: 10_000, Period: 1));

        var doomed = MagicAgencySeed.CreateInitialState(highWage, DefaultActors);
        var afterPayday = ProductionTick.AdvanceTick(doomed);

        Assert.Equal(1, afterPayday.Tick);
        Assert.Empty(afterPayday.Actors);
        Assert.Empty(afterPayday.Assignments);
        // No debit on failure — still starting 100 (no sale on tick 1)
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(afterPayday, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
        Assert.Equal(1, afterPayday.NodeTimers[MagicAgencySeed.PayrollNodeId]);

        // Later ticks idle with no actors; tick-1 mutation remains residual (no further mutate).
        var afterTwo = ProductionTick.AdvanceTick(afterPayday);
        Assert.Empty(afterTwo.Actors);
        Assert.Equal(
            new SignalValue.Enchantment(10, 1, 1),
            Signal(afterTwo, MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId));
        Assert.Equal(
            new SignalValue.Money(100),
            Signal(afterTwo, MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId));
    }

    private static SignalValue Signal(GameState state, NodeId node, PortId port) =>
        state.PortSignals[new PortKey(node, port)];

    private static NodeIoRow Row(ProductionTickResult result, NodeId node) =>
        result.Nodes.Single(r => r.NodeId == node);
}
