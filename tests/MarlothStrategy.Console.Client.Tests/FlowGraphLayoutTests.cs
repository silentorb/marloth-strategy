using System.Collections.Immutable;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class FlowGraphLayoutTests
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
    public void Compute_SeedGraph_IncludesEveryNodeAndCollapsedEdge()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var layout = FlowGraphLayout.Compute(state);

        var nodeIds = layout.Nodes.Select(n => n.Id.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            ["enchant", "merge", "payroll", "sell", "testing", "treasury"],
            nodeIds);

        var edgeKeys = layout.Edges
            .Select(e => $"{e.From.Value}->{e.To.Value}")
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "enchant->merge",
                "enchant->testing",
                "merge->enchant",
                "sell->treasury",
                "testing->merge",
                "testing->sell",
                "treasury->payroll",
            ],
            edgeKeys);

        foreach (var edge in layout.Edges)
        {
            Assert.NotEmpty(edge.Points);
        }
    }
}
