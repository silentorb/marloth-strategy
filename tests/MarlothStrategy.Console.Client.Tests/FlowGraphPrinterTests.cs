using System.Collections.Immutable;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class FlowGraphPrinterTests
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
        new PayrollNodeConfig(DefaultWage: 10, Period: 5, Effort: 5),
        new MergeNodeConfig(Effort: 5));

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
    public void FormatLines_SeedGraph_ShowsBranchingSpatialLayout()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var lines = FlowGraphPrinter.FormatLines(state, maxWidth: 65);
        var text = string.Join('\n', lines);

        Assert.Contains("enchant", text);
        Assert.Contains("testing", text);
        Assert.Contains("merge", text);
        Assert.Contains("sell", text);
        Assert.Contains("treasury", text);
        Assert.Contains("payroll", text);

        Assert.DoesNotContain("merge → enchant", text);
        Assert.DoesNotContain("enchant → testing", text);
        Assert.True(
            text.Contains('▼') || text.Contains('▲') || text.Contains('►') || text.Contains('◄'),
            "Expected at least one directed arrow glyph in the drawn graph.");

        var cornerCount = text.Count(static c =>
            c is BoxDrawing.SingleTopLeft
                or BoxDrawing.SingleTopRight
                or BoxDrawing.SingleBottomLeft
                or BoxDrawing.SingleBottomRight);
        Assert.True(
            cornerCount > 6 * 4
            || text.Contains(BoxDrawing.SingleTeeLeft)
            || text.Contains(BoxDrawing.SingleTeeRight)
            || text.Contains(BoxDrawing.SingleTeeTop)
            || text.Contains(BoxDrawing.SingleTeeBottom)
            || text.Contains(BoxDrawing.SingleCross),
            $"Expected wire corners/tees beyond node boxes (cornerCount={cornerCount}).");

        var testingLine = lines.First(l => l.Contains("testing", StringComparison.Ordinal));
        var mergeLine = lines.First(l => l.Contains("merge", StringComparison.Ordinal));
        Assert.NotEqual(
            testingLine.IndexOf("testing", StringComparison.Ordinal),
            mergeLine.IndexOf("merge", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatLines_SeedGraph_MergeEnchantEdgesHaveArrows()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var lines = FlowGraphPrinter.FormatLines(state, maxWidth: 65);

        var enchantIdx = lines.Select((l, i) => (l, i)).First(t => t.l.Contains("enchant", StringComparison.Ordinal)).i;
        var mergeIdx = lines.Select((l, i) => (l, i)).First(t => t.l.Contains("merge", StringComparison.Ordinal)).i;
        var lo = Math.Min(enchantIdx, mergeIdx);
        var hi = Math.Max(enchantIdx, mergeIdx);
        var band = string.Join('\n', lines.Skip(lo).Take(hi - lo + 1));

        Assert.True(band.Contains('▼'), "Expected downward arrow for enchant → merge.");
        Assert.True(band.Contains('▲'), "Expected upward arrow for merge → enchant.");
    }
}
