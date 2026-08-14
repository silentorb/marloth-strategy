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
        new TreasuryNodeConfig(Effort: 1),
        new PayrollNodeConfig(DefaultWage: 10, Period: 5, Effort: 1),
        new MergeNodeConfig(Effort: 1),
        new DesignNodeConfig(Effort: 3));

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

    private static GameState MergeFixture()
    {
        var seed = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        return seed with
        {
            Graph = GraphFactory.CreateGraphWithMergeNode(),
            Assignments = seed.Assignments.Add(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.MergeNodeId)),
        };
    }

    [Fact]
    public void FormatLines_SeedGraph_ShowsBranchingSpatialLayout()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var lines = FlowGraphPrinter.FormatLines(state, maxWidth: 65);
        var text = string.Join('\n', lines);

        Assert.Contains("enchant", text);
        Assert.Contains("testing", text);
        Assert.DoesNotContain("merge", text);
        Assert.Contains("sell", text);
        Assert.Contains("treasury", text);
        Assert.Contains("payroll", text);

        Assert.True(
            text.Contains('▼') || text.Contains('▲') || text.Contains('►') || text.Contains('◄'),
            "Expected at least one directed arrow glyph in the drawn graph.");
    }

    [Fact]
    public void FormatLines_MergeFixture_MergeInputsArriveOnDistinctColumns()
    {
        var lines = FlowGraphPrinter.FormatLines(MergeFixture(), maxWidth: 65);
        var text = string.Join('\n', lines);

        Assert.Contains("merge", text);
        Assert.DoesNotContain("merge → enchant", text);

        var mergeLineIdx = lines
            .Select((l, i) => (l, i))
            .First(t => t.l.Contains("| merge |", StringComparison.Ordinal)
                || t.l.Contains("│ merge │", StringComparison.Ordinal)).i;

        var mergeLabel = lines[mergeLineIdx];
        var boxLeft = mergeLabel.IndexOf(BoxDrawing.SingleVertical);
        var boxRight = mergeLabel.LastIndexOf(BoxDrawing.SingleVertical);
        Assert.True(boxLeft >= 0 && boxRight > boxLeft);

        var inboundColumns = new HashSet<int>();
        for (var row = Math.Max(0, mergeLineIdx - 3); row < mergeLineIdx; row++)
        {
            var line = lines[row];
            for (var x = boxLeft; x <= boxRight && x < line.Length; x++)
            {
                if (line[x] is '▼' or '│' or '┌' or '┐' or '┬' or '┼' or '├' or '┤')
                {
                    inboundColumns.Add(x);
                }
            }
        }

        Assert.True(
            inboundColumns.Count >= 2,
            $"Expected distinct merge input columns from MSAGL ports, got [{string.Join(',', inboundColumns.OrderBy(c => c))}]");
    }

    [Fact]
    public void FormatLines_EssentialGraph_AnnotatesEnchantSelfLoop()
    {
        var spec = new ScenarioSpec(
            IncludeTesting: false,
            ImmutableArray.Create(MagicAgencySeed.ActorId),
            ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId)));
        var state = ScenarioBootstrap.Materialize(spec, DefaultConfigs, DefaultActors);
        var lines = FlowGraphPrinter.FormatLines(state, maxWidth: 65);
        var enchantLine = lines.First(l => l.Contains("enchant", StringComparison.Ordinal));

        Assert.Contains("──┐", enchantLine);
        Assert.DoesNotContain("testing", string.Join('\n', lines));
        Assert.DoesNotContain("merge", string.Join('\n', lines));
        Assert.DoesNotContain("design", string.Join('\n', lines));
    }
}
