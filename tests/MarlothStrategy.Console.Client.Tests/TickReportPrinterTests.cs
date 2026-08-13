using System.Collections.Immutable;
using MarlothStrategy.Console.Client;
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
            FallacyConstant: 1),
        new TestingNodeConfig(Effort: 10, FallacyReduction: 5),
        new SellNodeConfig(Effort: 10, PayoutFloor: 0),
        new TreasuryNodeConfig(Effort: 2),
        new PayrollNodeConfig(DefaultWage: 10, Period: 5, Effort: 5));

    private static readonly ImmutableDictionary<ActorId, Actor> DefaultActors =
        ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty
                    .Add(ActorStatKeys.Enchanting, 10)
                    .Add(ActorStatKeys.Sales, 10)));

    [Fact]
    public void FormatScreen_Tick0_UsesPanelFrameAndNodeContent()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var text = TickReportPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);
        var normalized = text.Replace("\r\n", "\n");

        Assert.StartsWith($"{BoxDrawing.DoubleTopLeft}", normalized);
        Assert.Contains("Marloth Strategy", text);
        Assert.Contains("Tick 0", text);
        Assert.Contains("actors: intern", text);
        Assert.Contains("enchant:", text);
        Assert.Contains("  enchantment:", text);
        Assert.Contains("    volume: 0", text);
        Assert.Contains("    darkness: 0", text);
        Assert.Contains("    fallacy: 0", text);
        Assert.Contains("  progress: 0", text);
        Assert.Contains("testing:", text);
        Assert.Contains("payroll:", text);
        Assert.Contains("  timer: 5", text);
        Assert.Contains("  money: 0", text);
        Assert.Contains("sell:", text);
        Assert.Contains("  enchantment: 0", text);
        Assert.Contains("  money: 0", text);
        Assert.Contains("treasury:", text);
        Assert.Contains("  money: 100", text);
        Assert.Contains($"{BoxDrawing.MixedTeeLeft}", text);
        Assert.Contains($"{BoxDrawing.DoubleBottomLeft}", text);
        Assert.DoesNotContain('\u2192', text);
        Assert.DoesNotContain("enchantment: -", text);
        Assert.DoesNotContain("Effort", text);
        Assert.DoesNotContain("Consumed", text);
    }

    [Fact]
    public void FormatScreen_Tick1_MoneyShowsOwnedStockNotTransforms()
    {
        var previous = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var result = ProductionTick.AdvanceTickWithReport(previous);
        var text = TickReportPrinter.FormatScreen(result.State, previous, result, PanelLayout.DefaultWidth);
        var normalized = text.Replace("\r\n", "\n");

        Assert.Contains("Tick 1", normalized);
        Assert.Contains("actors: intern", normalized);
        Assert.Contains("volume: 0 \u2192 10", normalized);
        Assert.Contains("timer: 5 \u2192 4", normalized);
        Assert.Contains("treasury:", normalized);
        Assert.Contains("  money: 100", normalized);
        Assert.DoesNotContain("money: 100 \u2192 80", text);
        Assert.DoesNotContain("money: 0 \u2192 80", text);
        Assert.DoesNotContain("Effort", text);
        Assert.DoesNotContain("Consumed", text);
    }

    [Fact]
    public void FormatScreen_WithPrevious_AnnotatesChangedLeaves()
    {
        var previous = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var current = previous with
        {
            Tick = 1,
            PortSignals = previous.PortSignals
                .SetItem(
                    new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(80))
                .SetItem(
                    new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
                    new SignalValue.Enchantment(10, 1, 1)),
            NodeProgress = ImmutableDictionary<NodeId, double>.Empty
                .Add(MagicAgencySeed.SellNodeId, 5)
                .Add(MagicAgencySeed.TreasuryNodeId, 1)
                .Add(MagicAgencySeed.PayrollNodeId, 2),
            NodeTimers = previous.NodeTimers.SetItem(MagicAgencySeed.PayrollNodeId, 4),
            Actors = ImmutableDictionary<ActorId, Actor>.Empty,
            Assignments = ImmutableArray<Assignment>.Empty,
        };

        var text = TickReportPrinter.FormatScreen(current, previous, width: PanelLayout.DefaultWidth);
        var normalized = text.Replace("\r\n", "\n");

        Assert.Contains("actors: intern \u2192 0", normalized);
        Assert.Contains("money: 100 \u2192 80", normalized);
        Assert.Contains("progress: 0 \u2192 1", normalized);
        Assert.Contains("volume: 0 \u2192 10", text);
        Assert.Contains("darkness: 0 \u2192 1", text);
        Assert.Contains("fallacy: 0 \u2192 1", text);
        Assert.Contains("progress: 0 \u2192 5", text);
        Assert.Contains("timer: 5 \u2192 4", normalized);
        Assert.Contains("progress: 0 \u2192 2", normalized);
    }

    [Fact]
    public void FormatSignal_RoundsNonIntegerNumerics()
    {
        Assert.Equal("11", TickReportPrinter.FormatSignal(new SignalValue.Money(10.6)));
        Assert.Equal(
            "11/2/4",
            TickReportPrinter.FormatSignal(new SignalValue.Enchantment(10.5, 1.6, 3.5)));
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
