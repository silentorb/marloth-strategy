using System.Collections.Immutable;
using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class TickReportPrinterTests
{
    [Fact]
    public void FormatStartingStocks_IncludesTickAndPortSignals()
    {
        var state = MagicAgencySeed.CreateInitialState();
        var text = TickReportPrinter.FormatStartingStocks(state);

        Assert.Contains("Starting stocks (tick 0)", text);
        Assert.Contains("enchant.enchantment: 0/0/0", text);
        Assert.Contains("enchant.money: 100", text);
    }

    [Fact]
    public void FormatTickReport_IncludesHeaderAndNodeIoCells()
    {
        var result = new ProductionTickResult(
            MagicAgencySeed.CreateInitialState() with { Tick = 1 },
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
}
