using System.Collections.Immutable;
using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class TickReportPrinterTests
{
    private static readonly NodeTypeConfigs DefaultConfigs = new(
        new EnchantNodeConfig(
            Cost: 20,
            Effort: 10,
            VolumeDelta: 10,
            DarknessDelta: 1,
            FallacyConstant: 1),
        new SellNodeConfig(Effort: 10, PayoutFloor: 0));

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
    public void FormatStateSnapshot_Tick0_UsesHeadingBlankLinesAndYamlShape()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var text = TickReportPrinter.FormatStateSnapshot(state);

        Assert.StartsWith("## Tick 0\n\nenchant:", text.Replace("\r\n", "\n"));
        Assert.Contains("enchant:", text);
        Assert.Contains("  money: 100", text);
        Assert.Contains("  enchantment:", text);
        Assert.Contains("    volume: 0", text);
        Assert.Contains("    darkness: 0", text);
        Assert.Contains("    fallacy: 0", text);
        Assert.Contains("  progress: 0", text);
        Assert.Contains("\n\nsell:\n", text.Replace("\r\n", "\n"));
        Assert.Contains("  enchantment: 0", text);
        Assert.Contains("  money: 0", text);
        Assert.DoesNotContain('\u2192', text);
        Assert.DoesNotContain("enchantment: -", text);
        Assert.DoesNotContain("Effort", text);
        Assert.DoesNotContain("Consumed", text);
    }

    [Fact]
    public void FormatStateSnapshot_Tick1_MoneyShowsTransformsNotOwnership()
    {
        var previous = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var result = ProductionTick.AdvanceTickWithReport(previous);
        var text = TickReportPrinter.FormatStateSnapshot(result.State, previous, result);
        var normalized = text.Replace("\r\n", "\n");

        Assert.StartsWith("## Tick 1\n\n", normalized);
        // Enchant deducts cost; sell pass-throughs as bare 80 (not 0 → 80 ownership).
        Assert.Contains("enchant:\n  enchantment:\n    volume: 0 \u2192 10", normalized);
        Assert.Contains("  money: 100 \u2192 80\n", normalized);
        Assert.Contains("sell:\n  enchantment:\n    volume: 0 \u2192 10", normalized);
        Assert.Contains("  money: 80\n", normalized);
        Assert.DoesNotContain("money: 0 \u2192 80", text);
        Assert.DoesNotContain("money: 80 \u2192 80", text);
    }

    [Fact]
    public void FormatStateSnapshot_WithPrevious_AnnotatesChangedLeaves()
    {
        var previous = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var current = previous with
        {
            Tick = 1,
            PortSignals = previous.PortSignals
                .SetItem(
                    new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.MoneyPortId),
                    new SignalValue.Money(80))
                .SetItem(
                    new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
                    new SignalValue.Enchantment(10, 1, 1)),
            NodeProgress = ImmutableDictionary<NodeId, double>.Empty
                .Add(MagicAgencySeed.SellNodeId, 5),
        };

        var text = TickReportPrinter.FormatStateSnapshot(current, previous);
        var normalized = text.Replace("\r\n", "\n");

        Assert.StartsWith("## Tick 1\n\n", normalized);
        Assert.Contains("  money: 100 \u2192 80", text);
        Assert.Contains("    volume: 0 \u2192 10", text);
        Assert.Contains("    darkness: 0 \u2192 1", text);
        Assert.Contains("    fallacy: 0 \u2192 1", text);
        Assert.Contains("  progress: 0 \u2192 5", text);
        // Without a tick report, sell money falls back to stock diff (still empty).
        Assert.Contains("sell:\n  enchantment: 0\n  money: 0\n", normalized);
    }

    [Fact]
    public void FormatSignal_RoundsNonIntegerNumerics()
    {
        Assert.Equal("11", TickReportPrinter.FormatSignal(new SignalValue.Money(10.6)));
        Assert.Equal(
            "11/2/4",
            TickReportPrinter.FormatSignal(new SignalValue.Enchantment(10.5, 1.6, 3.5)));
    }
}
