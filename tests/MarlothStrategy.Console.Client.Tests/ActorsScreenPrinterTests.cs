using System.Collections.Immutable;
using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class ActorsScreenPrinterTests
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
                        .Add(ActorStatKeys.Sales, 10),
                    Wage: 2))
            .Add(
                MagicAgencySeed.BossActorId,
                new Actor(
                    MagicAgencySeed.BossActorId,
                    Capacity: 1.0m,
                    ImmutableDictionary<string, double>.Empty
                        .Add(ActorStatKeys.Sales, 10)
                        .Add(ActorStatKeys.Payroll, 10)
                        .Add(ActorStatKeys.Treasury, 10),
                    Wage: 3));

    [Fact]
    public void FormatScreen_UsesPanelFrameHeaderAndActorContent()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var text = ActorsScreenPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);
        var normalized = text.Replace("\r\n", "\n");

        Assert.StartsWith($"{BoxDrawing.DoubleTopLeft}", normalized);
        Assert.Contains("Marloth Strategy", text);
        Assert.Contains("Tick 0", text);
        Assert.Contains("month 1, week 1/4, day 1/7", text);
        Assert.Contains("screen: actors", text);
        Assert.Contains("actors: boss, intern", text);
        Assert.Contains("intern:", text);
        Assert.Contains("boss:", text);
        Assert.Contains("capacity: 1", text);
        Assert.Contains("wage: 2", text);
        Assert.Contains("wage: 3", text);
        Assert.Contains("enchanting: 10", text);
        Assert.Contains("sales: 10", text);
        Assert.Contains("payroll: 10", text);
        Assert.Contains("treasury: 10", text);
        Assert.Contains("enchant 1", text);
        Assert.Contains("testing 1", text);
        Assert.Contains("payroll 1", text);
        Assert.Contains("sell 1", text);
        Assert.Contains("treasury 1", text);
        Assert.Contains($"{BoxDrawing.MixedTeeLeft}", normalized);
        Assert.Contains($"{BoxDrawing.MixedTeeRight}", normalized);
        Assert.DoesNotContain("screen: workflow", text);
    }

    [Fact]
    public void FormatScreen_UnpaidActor_ShowsWageNone()
    {
        var unpaidId = new ActorId("volunteer");
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            unpaidId,
            new Actor(
                unpaidId,
                Capacity: 0.5m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Enchanting, 1),
                Wage: null));
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray<Assignment>.Empty,
        };

        var text = ActorsScreenPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);

        Assert.Contains("volunteer:", text);
        Assert.Contains("wage: none", text);
        Assert.Contains("capacity: 0.5", text);
    }

    [Fact]
    public void FormatScreen_EmptyStats_ShowsStatsNone()
    {
        var id = new ActorId("blank");
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            id,
            new Actor(id, Capacity: 1m, ImmutableDictionary<string, double>.Empty, Wage: 1));
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray<Assignment>.Empty,
        };

        var text = ActorsScreenPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);

        Assert.Contains("stats: none", text);
    }

    [Fact]
    public void FormatScreen_EmptyRoster_ShowsActorsZeroSubpanel()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = ImmutableDictionary<ActorId, Actor>.Empty,
            Assignments = ImmutableArray<Assignment>.Empty,
        };

        var text = ActorsScreenPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);

        Assert.Contains("actors: 0", text);
        Assert.StartsWith($"{BoxDrawing.DoubleTopLeft}", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void FormatScreen_NarrowWidth_DoesNotThrow()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var text = ActorsScreenPrinter.FormatScreen(state, width: 40);
        Assert.Contains("screen: actors", text);
        Assert.Equal(40, text.Replace("\r\n", "\n").Split('\n')[0].Length);
    }
}
