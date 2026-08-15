using System.Collections.Immutable;
using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class TickReportPrinterTests
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
    public void FormatScreen_Tick0_UsesPanelFrameAndNodeContent()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var text = TickReportPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);
        var normalized = text.Replace("\r\n", "\n");
        var genesis = EnchantmentBlock.CreateGenesis();

        Assert.StartsWith($"{BoxDrawing.DoubleTopLeft}", normalized);
        Assert.Contains("Marloth Strategy", text);
        Assert.Contains("Tick 0", text);
        Assert.Contains("month 1, week 1/4, day 1/7", text);
        Assert.Contains("actors: boss, intern", text);
        Assert.Contains("enchant:", text);
        Assert.Contains("intern 1", text);
        Assert.Contains("boss 1", text);
        Assert.Contains($"{BoxDrawing.SingleVertical}", text);
        Assert.Contains("  enchantment:", text);
        Assert.Contains($"    hash: {genesis.AbbreviatedHash}", text);
        Assert.Contains("    volume: 0", text);
        Assert.Contains("    designs: 0", text);
        Assert.Contains("    darkness: 0", text);
        Assert.Contains("    fallacy: 0", text);
        Assert.Contains("  cycles: 0", text);
        Assert.Contains("testing:", text);
        Assert.DoesNotContain("merge:", text);
        Assert.DoesNotContain("  primary: 0", text);
        Assert.DoesNotContain("  secondary: 0", text);
        Assert.Contains("payroll:", text);
        Assert.DoesNotContain("  timer:", text);
        Assert.DoesNotContain("  progress:", text);
        Assert.Contains("  money: 0", text);
        Assert.Contains("sell:", text);
        Assert.Contains("  enchantment: 0", text);
        Assert.Contains("  money: 0", text);
        Assert.Contains("treasury:", text);
        Assert.Contains("  money: 100", text);
        Assert.Contains($"{BoxDrawing.MixedTeeLeft}", text);
        Assert.Contains($"{BoxDrawing.DoubleBottomLeft}", text);
        Assert.DoesNotContain("enchantment: -", text);
        Assert.DoesNotContain("Effort", text);
        Assert.DoesNotContain("Consumed", text);
        Assert.DoesNotContain("volume: 0 \u2192", text);
    }

    [Fact]
    public void FormatScreen_MergeFixture_ShowsPrimaryAndSecondaryPorts()
    {
        var seed = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var state = seed with
        {
            Graph = GraphFactory.CreateGraphWithMergeNode(),
            Assignments = seed.Assignments.Add(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.MergeNodeId)),
        };
        var text = TickReportPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);

        Assert.Contains("merge:", text);
        Assert.Contains("  primary: 0", text);
        Assert.Contains("  secondary: 0", text);
    }

    [Fact]
    public void FormatScreen_DesignGraph_ShowsDesignEnchantmentPorts()
    {
        var spec = new ScenarioSpec(
            IncludeTesting: false,
            ImmutableArray.Create(MagicAgencySeed.ActorId),
            ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.DesignNodeId)),
            IncludeDesign: true);
        var state = ScenarioBootstrap.Materialize(spec, DefaultConfigs, DefaultActors);
        var text = TickReportPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);

        Assert.Contains("design:", text);
        Assert.Contains("  enchantment:", text);
        Assert.Contains("    designs: 0", text);
        Assert.Contains("  cycles: 0", text);
        Assert.DoesNotContain("testing:", text);
    }

    [Fact]
    public void FormatScreen_WithGameConfig_IncludesScenarioAndSeed()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var lab01 = TickReportPrinter.FormatScreen(
            state,
            width: PanelLayout.DefaultWidth,
            config: new GameConfig { ScenarioPreset = "lab01", ScenarioSeed = 42 });
        var random = TickReportPrinter.FormatScreen(
            state,
            width: PanelLayout.DefaultWidth,
            config: new GameConfig { ScenarioPreset = null, ScenarioSeed = 7 });

        Assert.Contains("scenario: lab01 seed 42", lab01);
        Assert.Contains("scenario: random seed 7", random);
        Assert.DoesNotContain("scenario:", TickReportPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth));
    }

    [Fact]
    public void FormatCalendarLine_WeekBoundary_ShowsRolledPositions()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        Assert.Equal(
            "month 1, week 1/4, day 1/7",
            TickReportPrinter.FormatCalendarLine(state.TimePartitions, 0));
        Assert.Equal(
            "month 1, week 2/4, day 1/7",
            TickReportPrinter.FormatCalendarLine(state.TimePartitions, 7));
        Assert.Equal(
            "month 2, week 1/4, day 1/7",
            TickReportPrinter.FormatCalendarLine(state.TimePartitions, 28));
    }

    [Fact]
    public void FormatScreen_Tick1_MoneyShowsOwnedStockNotTransforms()
    {
        var previous = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var result = ProductionTick.AdvanceTickWithReport(previous);
        var text = TickReportPrinter.FormatScreen(result.State, previous, result, PanelLayout.DefaultWidth);
        var normalized = text.Replace("\r\n", "\n");

        Assert.Contains("Tick 1", normalized);
        Assert.Contains("month 1, week 1/4, day 2/7", normalized);
        Assert.Contains("actors: boss, intern", normalized);
        Assert.Contains("volume: 0 \u2192 10", normalized);
        Assert.Contains("cycles: 0 \u2192 1", normalized);
        Assert.DoesNotContain("timer:", normalized);
        Assert.DoesNotContain("progress:", normalized);
        Assert.Contains("treasury:", normalized);
        Assert.Contains("  money: 100", normalized);
        Assert.DoesNotContain("money: 100 \u2192 80", text);
        Assert.DoesNotContain("money: 0 \u2192 80", text);
        Assert.DoesNotContain("Effort", text);
        Assert.DoesNotContain("Consumed", text);
        Assert.Contains("hash:", normalized);
    }

    [Fact]
    public void FormatScreen_WithPrevious_AnnotatesChangedLeaves()
    {
        var previous = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var (block, _) = EnchantmentOps.FromCounts(
            volume: 10,
            designs: 0,
            darkness: 1,
            fallacy: 1,
            nextUnitId: 1);
        var current = previous with
        {
            Tick = 1,
            PortSignals = previous.PortSignals
                .SetItem(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(80))
                .SetItem(
                    new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
                    new SignalValue.Enchantment(block)),
            EnchantmentBlocks = previous.EnchantmentBlocks.SetItem(block.Hash, block),
            NodeProgress = ImmutableDictionary<NodeId, double>.Empty
                .Add(MagicAgencySeed.SellNodeId, 5)
                .Add(MagicAgencySeed.TreasuryNodeId, 1)
                .Add(MagicAgencySeed.PayrollNodeId, 2),
            NodeTimers = previous.NodeTimers.SetItem(MagicAgencySeed.PayrollNodeId, 1),
            NodeCycles = ImmutableDictionary<NodeId, int>.Empty
                .Add(MagicAgencySeed.SellNodeId, 1)
                .Add(MagicAgencySeed.TreasuryNodeId, 2)
                .Add(MagicAgencySeed.PayrollNodeId, 1)
                .Add(MagicAgencySeed.EnchantNodeId, 1),
            Actors = ImmutableDictionary<ActorId, Actor>.Empty,
            Assignments = ImmutableArray<Assignment>.Empty,
        };

        var text = TickReportPrinter.FormatScreen(current, previous, width: PanelLayout.DefaultWidth);
        var normalized = text.Replace("\r\n", "\n");

        Assert.Contains("actors: boss, intern \u2192 0", normalized);
        Assert.Contains("money: 100 \u2192 80", normalized);
        Assert.Contains("cycles: 0 \u2192 2", normalized);
        Assert.Contains("volume: 0 \u2192 10", text);
        Assert.Contains("designs: 0", text);
        Assert.Contains("darkness: 0 \u2192 1", text);
        Assert.Contains("fallacy: 0 \u2192 1", text);
        Assert.Contains("cycles: 0 \u2192 1", text);
        Assert.DoesNotContain("timer:", normalized);
        Assert.DoesNotContain("progress:", normalized);
        Assert.Contains($"hash: {EnchantmentBlock.CreateGenesis().AbbreviatedHash} \u2192 {block.AbbreviatedHash}", text);
    }

    [Fact]
    public void FormatScreen_WithBaseline_ShowsSignedDeltaColumn()
    {
        var baseline = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var current = baseline with
        {
            Tick = 2,
            PortSignals = baseline.PortSignals
                .SetItem(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(109)),
            NodeCycles = ImmutableDictionary<NodeId, int>.Empty
                .Add(MagicAgencySeed.EnchantNodeId, 1),
        };

        var text = TickReportPrinter.FormatScreen(
            current,
            previous: baseline,
            width: PanelLayout.DefaultWidth,
            baseline: baseline);
        var normalized = text.Replace("\r\n", "\n");

        Assert.Contains(DeltaCaptionInPanel(), normalized);
        Assert.Contains("+9", normalized);
        Assert.Contains("money: 100 \u2192 109", normalized);
        Assert.Contains("cycles: 0 \u2192 1", normalized);
        // Three columns: state | Δ | assignments separated by single verticals inside left subpanels.
        Assert.Contains($"{BoxDrawing.SingleVertical}", normalized);
        var treasuryLine = normalized.Split('\n').First(l => l.Contains("money: 100 \u2192 109"));
        Assert.Contains("+9", treasuryLine);
        Assert.True(
            treasuryLine.Count(c => c == BoxDrawing.SingleVertical) >= 2,
            $"Expected at least two column separators on treasury money row: {treasuryLine}");
    }

    private static string DeltaCaptionInPanel() => "\u0394";

    [Fact]
    public void FormatSignal_IncludesAbbreviatedHashAndCounts()
    {
        Assert.Equal("11", TickReportPrinter.FormatSignal(new SignalValue.Money(10.6)));
        var (block, _) = EnchantmentOps.FromCounts(
            volume: 11,
            designs: 3,
            darkness: 2.5,
            fallacy: 4,
            nextUnitId: 1);
        Assert.Equal(
            $"{block.AbbreviatedHash} 11/3/2.5/4",
            TickReportPrinter.FormatSignal(new SignalValue.Enchantment(block)));
    }

    [Fact]
    public void FormatStateSnapshot_DelegatesToFormatScreen()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        Assert.Equal(
            TickReportPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth),
            TickReportPrinter.FormatStateSnapshot(state));
    }
}
