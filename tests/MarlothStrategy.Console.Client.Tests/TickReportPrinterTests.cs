using System.Collections.Immutable;
using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class TickReportPrinterTests
{
    private static readonly NodeTypeConfigs DefaultConfigs = new(
        new EnchantNodeConfig(
            BaseThroughput: 20,
            VolumeDelta: 10,
            DarknessDelta: 1,
            FallacyConstant: 1),
        new SellNodeConfig(BaseThroughput: 20, PayoutFloor: 0));

    [Fact]
    public void FormatStartingStocks_IncludesTickAndPortSignals()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs);
        var text = TickReportPrinter.FormatStartingStocks(state);

        Assert.Contains("Starting stocks (tick 0)", text);
        Assert.Contains("enchant.enchantment: 0/0/0", text);
        Assert.Contains("enchant.money: 100", text);
    }

    [Fact]
    public void FormatTickReport_IncludesHeaderAndNodeIoCells()
    {
        var result = new ProductionTickResult(
            MagicAgencySeed.CreateInitialState(DefaultConfigs) with { Tick = 1 },
            ImmutableArray.Create(
                new NodeIoRow(
                    MagicAgencySeed.EnchantNodeId,
                    0.5m,
                    MagicAgencySeed.EnchantmentPortId,
                    SignalTypes.Enchantment,
                    new SignalValue.Enchantment(0, 0, 0),
                    Consumed: true,
                    Residual: null,
                    MagicAgencySeed.EnchantmentPortId,
                    SignalTypes.Enchantment,
                    new SignalValue.Enchantment(10, 1, 1)),
                new NodeIoRow(
                    MagicAgencySeed.SellNodeId,
                    0.5m,
                    MagicAgencySeed.EnchantmentPortId,
                    SignalTypes.Enchantment,
                    Available: null,
                    Consumed: false,
                    Residual: null,
                    MagicAgencySeed.MoneyPortId,
                    SignalTypes.Money,
                    Produced: null)));

        var text = TickReportPrinter.FormatTickReport(result);

        Assert.Contains("Tick 1", text);
        Assert.Contains("Node", text);
        Assert.Contains("Effort", text);
        Assert.Contains("Input", text);
        Assert.Contains("Consumed", text);
        Assert.Contains("Residual", text);
        Assert.Contains("Output", text);
        Assert.Contains("enchant", text);
        Assert.Contains("sell", text);
        Assert.Contains("enchantment 0/0/0", text);
        Assert.Contains("enchantment 10/1/1", text);
        Assert.Contains("0.5", text);
        Assert.Contains("yes", text);
        Assert.Contains("no", text);
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
