using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;
using MarlothStrategy.Simulation.Time;

namespace MarlothStrategy.Simulation.Tests;

public sealed class TimePartitionProgressionTests
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
        new PayrollNodeConfig(Period: 5, BaseEffort: 1, PerActorEffort: 1),
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

    [Fact]
    public void AdvanceTicks_MatchesRepeatedAdvanceTick()
    {
        var start = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        Assert.Equal(7, start.TimePartitions.AdvanceTickCount);

        var single = start;
        for (var i = 0; i < 7; i++)
        {
            single = ProductionTick.AdvanceTick(single);
        }

        var batched = ProductionTick.AdvanceTicks(start, 7);

        Assert.Equal(7, batched.Tick);
        AssertSemanticStateEqual(single, batched);
    }

    [Fact]
    public void AdvanceTicks_FromPartialWeek_AdvancesFullDurationNotBoundarySnap()
    {
        var start = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var midWeek = ProductionTick.AdvanceTicks(start, 3);
        Assert.Equal(3, midWeek.Tick);

        var afterMacro = ProductionTick.AdvanceTicks(midWeek, midWeek.TimePartitions.AdvanceTickCount);
        Assert.Equal(10, afterMacro.Tick);

        var positions = afterMacro.TimePartitions.PositionsAt(afterMacro.Tick);
        Assert.Equal(4, positions[0].Index); // day 4/7 of week 2
        Assert.Equal(2, positions[1].Index);
    }

    [Fact]
    public void AdvanceTicks_RunsPayrollTimerEachContainedTick()
    {
        var start = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var after = ProductionTick.AdvanceTicks(start, 5);

        Assert.Equal(5, after.Tick);
        Assert.Equal(5, after.NodeTimers[MagicAgencySeed.PayrollNodeId]);
    }

    [Fact]
    public void AdvanceTicks_RejectsNonPositiveCount()
    {
        var start = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionTick.AdvanceTicks(start, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionTick.AdvanceTicks(start, -1));
    }

    [Fact]
    public void Bootstrap_AttachesCommittedTimePartitions()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        Assert.Equal("week", state.TimePartitions.AdvanceUnit);
        Assert.Equal(7, state.TimePartitions.AdvanceTickCount);
    }

    /// <summary>
    /// Compares simulation-relevant fields without relying on record equality for
    /// <see cref="EnchantmentBlock"/> values that share identical content but are
    /// distinct instances (ImmutableArray-in-record Equals can disagree with field equality).
    /// </summary>
    private static void AssertSemanticStateEqual(GameState expected, GameState actual)
    {
        Assert.Equal(expected.Tick, actual.Tick);
        Assert.Equal(expected.NextUnitId, actual.NextUnitId);
        Assert.True(expected.TimePartitions.Equals(actual.TimePartitions));
        Assert.Equal(expected.NodeConfigs, actual.NodeConfigs);
        Assert.Equal(
            expected.NodeTimers.OrderBy(kv => kv.Key.Value).ToArray(),
            actual.NodeTimers.OrderBy(kv => kv.Key.Value).ToArray());
        Assert.Equal(
            expected.NodeCycles.OrderBy(kv => kv.Key.Value).ToArray(),
            actual.NodeCycles.OrderBy(kv => kv.Key.Value).ToArray());
        Assert.Equal(
            expected.NodeProgress.OrderBy(kv => kv.Key.Value).ToArray(),
            actual.NodeProgress.OrderBy(kv => kv.Key.Value).ToArray());
        Assert.Equal(expected.PendingMoneyMoves.ToArray(), actual.PendingMoneyMoves.ToArray());
        Assert.Equal(
            expected.Actors.OrderBy(kv => kv.Key.Value).ToArray(),
            actual.Actors.OrderBy(kv => kv.Key.Value).ToArray());
        Assert.Equal(expected.Assignments.ToArray(), actual.Assignments.ToArray());

        Assert.Equal(
            expected.EnchantmentBlocks.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            actual.EnchantmentBlocks.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        foreach (var hash in expected.EnchantmentBlocks.Keys)
        {
            AssertEnchantmentBlockEqual(expected.EnchantmentBlocks[hash], actual.EnchantmentBlocks[hash]);
        }

        Assert.Equal(expected.PortSignals.Count, actual.PortSignals.Count);
        foreach (var (key, value) in expected.PortSignals)
        {
            Assert.True(actual.PortSignals.TryGetValue(key, out var other));
            AssertSignalEqual(value, other!);
        }
    }

    private static void AssertSignalEqual(SignalValue expected, SignalValue actual)
    {
        switch (expected, actual)
        {
            case (SignalValue.Money em, SignalValue.Money am):
                Assert.Equal(em.Amount, am.Amount);
                break;
            case (SignalValue.Enchantment ee, SignalValue.Enchantment ae):
                AssertEnchantmentBlockEqual(ee.Block, ae.Block);
                break;
            default:
                Assert.Fail($"Signal kinds differ: {expected.GetType().Name} vs {actual.GetType().Name}");
                break;
        }
    }

    private static void AssertEnchantmentBlockEqual(EnchantmentBlock expected, EnchantmentBlock actual)
    {
        Assert.Equal(expected.Hash, actual.Hash);
        Assert.Equal(expected.ParentHash, actual.ParentHash);
        Assert.Equal(expected.Darkness, actual.Darkness);
        Assert.Equal(expected.Fallacy, actual.Fallacy);
        Assert.Equal(expected.Volume.ToArray(), actual.Volume.ToArray());
        Assert.Equal(expected.Designs.ToArray(), actual.Designs.ToArray());
    }
}
