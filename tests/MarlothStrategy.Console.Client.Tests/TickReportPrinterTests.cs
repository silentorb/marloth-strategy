using System.Collections.Immutable;
using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class TickReportPrinterTests
{
    [Fact]
    public void FormatStartingStocks_IncludesTickAndPortQuantities()
    {
        var state = MagicAgencySeed.CreateInitialState();
        var text = TickReportPrinter.FormatStartingStocks(state);

        Assert.Contains("Starting stocks (tick 0)", text);
        Assert.Contains("enchant.money: 10", text);
        Assert.Contains("sell.enchantments: 0", text);
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
                    MagicAgencySeed.MoneyPortId,
                    SignalTypes.Money,
                    10m,
                    1m,
                    9m,
                    MagicAgencySeed.EnchantmentsPortId,
                    SignalTypes.Enchantments,
                    1m),
                new NodeIoRow(
                    MagicAgencySeed.SellNodeId,
                    0.5m,
                    MagicAgencySeed.EnchantmentsPortId,
                    SignalTypes.Enchantments,
                    0m,
                    0m,
                    0m,
                    MagicAgencySeed.MoneyPortId,
                    SignalTypes.Money,
                    0m)));

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
        Assert.Contains("money 10", text);
        Assert.Contains("enchantments 1", text);
        Assert.Contains("0.5", text);
    }
}
