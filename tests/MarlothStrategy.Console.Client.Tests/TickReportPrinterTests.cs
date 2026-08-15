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
        Assert.Contains("screen: workflow", text);
        Assert.Contains("actors: boss, intern", text);
        Assert.Contains("  enchant  ", text);
        Assert.DoesNotContain("enchant:", text);
        Assert.Contains("intern 1", text);
        Assert.Contains("boss 1", text);
        Assert.Contains($"{BoxDrawing.SingleVertical}", text);

        // Nested enchantment leaves keep their indentation in the key column; values sit in their own cell.
        AssertValue(normalized, "  enchant  ", "  enchantment:", string.Empty);
        AssertValue(normalized, "  enchant  ", "    hash:", genesis.AbbreviatedHash);
        AssertValue(normalized, "  enchant  ", "    volume:", "0");
        AssertValue(normalized, "  enchant  ", "    designs:", "0");
        AssertValue(normalized, "  enchant  ", "    darkness:", "0");
        AssertValue(normalized, "  enchant  ", "    fallacy:", "0");
        AssertValue(normalized, "  enchant  ", "  cycles:", "0");

        Assert.Contains("  testing  ", text);
        Assert.DoesNotContain("merge:", text);
        Assert.DoesNotContain("  primary:", text);
        Assert.DoesNotContain("  secondary:", text);
        Assert.Contains("  payroll  ", text);
        Assert.DoesNotContain("  timer:", text);
        Assert.DoesNotContain("  progress:", text);
        AssertValue(normalized, "  payroll  ", "  money:", "0");
        AssertValue(normalized, "  sell  ", "  enchantment:", "0");
        AssertValue(normalized, "  sell  ", "  money:", "0");
        AssertValue(normalized, "  treasury  ", "  money:", "100");

        Assert.Contains($"{BoxDrawing.DoubleTeeLeft}", text);
        Assert.Contains($"{BoxDrawing.DoubleBottomLeft}", text);
        Assert.DoesNotContain("Effort", text);
        Assert.DoesNotContain("Consumed", text);
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

        var normalized = text.Replace("\r\n", "\n");
        Assert.Contains("  merge  ", text);
        Assert.DoesNotContain("merge:", text);
        AssertValue(normalized, "  merge  ", "  primary:", "0");
        AssertValue(normalized, "  merge  ", "  secondary:", "0");
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

        var normalized = text.Replace("\r\n", "\n");
        Assert.Contains("  design  ", text);
        // Design's own port is empty; the nested leaves come from enchant's genesis block.
        AssertValue(normalized, "  design  ", "  enchantment:", "0");
        AssertValue(normalized, "  design  ", "  cycles:", "0");
        AssertValue(normalized, "  enchant  ", "    designs:", "0");
        Assert.DoesNotContain("testing:", text);
        Assert.DoesNotContain("  testing  ", text);
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
        AssertValue(normalized, "  enchant  ", "    volume:", "0 \u2192 10");
        AssertValue(normalized, "  enchant  ", "  cycles:", "0 \u2192 1");
        Assert.DoesNotContain("timer:", normalized);
        Assert.DoesNotContain("progress:", normalized);
        // Treasury money is owned stock, unchanged by this tick's transforms.
        AssertValue(normalized, "  treasury  ", "  money:", "100");
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
        AssertValue(normalized, "  treasury  ", "  money:", "100 \u2192 80");
        AssertValue(normalized, "  treasury  ", "  cycles:", "0 \u2192 2");
        AssertValue(normalized, "  enchant  ", "    volume:", "0 \u2192 10");
        AssertValue(normalized, "  enchant  ", "    designs:", "0");
        AssertValue(normalized, "  enchant  ", "    darkness:", "0 \u2192 1");
        AssertValue(normalized, "  enchant  ", "    fallacy:", "0 \u2192 1");
        AssertValue(normalized, "  enchant  ", "  cycles:", "0 \u2192 1");
        Assert.DoesNotContain("timer:", normalized);
        Assert.DoesNotContain("progress:", normalized);
        AssertValue(
            normalized,
            "  enchant  ",
            "    hash:",
            $"{EnchantmentBlock.CreateGenesis().AbbreviatedHash} \u2192 {block.AbbreviatedHash}");
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
        AssertValue(normalized, "  treasury  ", "  money:", "100 \u2192 109");
        AssertValue(normalized, "  enchant  ", "  cycles:", "0 \u2192 1");

        // Four columns: name | value | Δ | assignments, separated by single verticals.
        var treasuryLine = RowAfterTitle(normalized, "  treasury  ", "  money:");
        Assert.Equal("+9", Cell(treasuryLine, 2));
        Assert.True(
            treasuryLine.Count(c => c == BoxDrawing.SingleVertical) >= 3,
            $"Expected at least three column separators on treasury money row: {treasuryLine}");
    }

    [Fact]
    public void FormatScreen_DeltaColumn_CountsThroughputOnPassThroughMoneyPorts()
    {
        var baseline = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var current = baseline with
        {
            Tick = 40,
            PortFlowTotals = baseline.PortFlowTotals
                .SetItem(
                    new PortKey(MagicAgencySeed.PayrollNodeId, MagicAgencySeed.MoneyPortId),
                    -15)
                .SetItem(
                    new PortKey(MagicAgencySeed.SellNodeId, MagicAgencySeed.MoneyPortId),
                    24),
        };

        var text = TickReportPrinter.FormatScreen(
            current,
            previous: baseline,
            width: PanelLayout.DefaultWidth,
            baseline: baseline);
        var normalized = text.Replace("\r\n", "\n");

        var payrollLine = RowAfterTitle(normalized, $"  {MagicAgencySeed.PayrollNodeId.Value}  ", "  money:");
        Assert.Equal("-15", Cell(payrollLine, 2));
        var sellLine = RowAfterTitle(normalized, $"  {MagicAgencySeed.SellNodeId.Value}  ", "  money:");
        Assert.Equal("+24", Cell(sellLine, 2));
    }

    [Fact]
    public void FormatScreen_NumericCells_PadSoDigitsAlignByMagnitude()
    {
        var baseline = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var current = baseline with
        {
            Tick = 2,
            PortSignals = baseline.PortSignals
                .SetItem(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(109)),
        };

        var normalized = TickReportPrinter
            .FormatScreen(current, previous: baseline, width: PanelLayout.DefaultWidth, baseline: baseline)
            .Replace("\r\n", "\n")
            .Replace("\r\n", "\n");

        // treasury money is three digits, payroll money one — the shorter value shifts right to line up.
        var treasury = RowAfterTitle(normalized, "  treasury  ", "  money:");
        var payroll = RowAfterTitle(normalized, "  payroll  ", "  money:");
        Assert.StartsWith("100", RawCell(treasury, 1));
        Assert.StartsWith("  0", RawCell(payroll, 1));

        // Same for the signed Δ column (+9 against a single-digit 0).
        Assert.StartsWith("+9", RawCell(treasury, 2));
        Assert.StartsWith(" 0", RawCell(payroll, 2));
    }

    [Fact]
    public void FormatScreen_NonNumericCells_StayFlushLeft()
    {
        var baseline = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var current = baseline with
        {
            Tick = 2,
            PortSignals = baseline.PortSignals.SetItem(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(109)),
        };

        var normalized = TickReportPrinter
            .FormatScreen(current, previous: baseline, width: PanelLayout.DefaultWidth, baseline: baseline)
            .Replace("\r\n", "\n");

        // A hash is not a number, so it is not shifted by the money column's magnitude.
        var hash = RawCell(RowAfterTitle(normalized, "  enchant  ", "    hash:"), 1);
        Assert.StartsWith(EnchantmentBlock.CreateGenesis().AbbreviatedHash, hash);
    }

    /// <summary>Asserts the value cell of <paramref name="key"/> inside the panel titled <paramref name="title"/>.</summary>
    private static void AssertValue(string normalized, string title, string key, string expectedValue) =>
        Assert.Equal(expectedValue, Cell(RowAfterTitle(normalized, title, key), 1));

    /// <summary>Column cell without trimming, so leading alignment padding stays visible.</summary>
    private static string RawCell(string row, int index)
    {
        var cells = row.Split(BoxDrawing.SingleVertical);
        Assert.True(cells.Length > index, $"Row has no column {index}: {row}");
        return cells[index];
    }

    /// <summary>Finds the row whose key column holds <paramref name="key"/>, searching after the panel title.</summary>
    private static string RowAfterTitle(string normalized, string title, string key)
    {
        var lines = normalized.Split('\n');
        var start = Array.FindIndex(lines, l => l.Contains(title, StringComparison.Ordinal));
        Assert.True(start >= 0, $"Missing subpanel title '{title}'.");

        // Key cell keeps its indentation behind the border and the one-cell panel margin.
        var expectedKeyCell = $"{BoxDrawing.DoubleVertical} {key}";
        var row = Array.FindIndex(lines, start, l => Cell(l, 0).TrimEnd() == expectedKeyCell);
        Assert.True(row >= 0, $"Missing row '{key}' after '{title}'.");
        return lines[row];
    }

    private static string Cell(string row, int index)
    {
        var cells = row.Split(BoxDrawing.SingleVertical);
        Assert.True(cells.Length > index, $"Row has no column {index}: {row}");
        return index == 0 ? cells[0] : cells[index].Trim();
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
