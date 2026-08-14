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
    public void Compute_SeedGraph_UsesPortAnchorsWithDistinctMergeInputs()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var layout = FlowGraphLayout.Compute(state);

        var edgeKeys = layout.Edges
            .Select(e => $"{e.From.Value}.{e.FromPort.Value}->{e.To.Value}.{e.ToPort.Value}")
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "enchant.enchantment->merge.primary",
                "enchant.enchantment->testing.enchantment",
                "merge.enchantment->enchant.enchantment",
                "sell.money->treasury.money",
                "testing.enchantment->merge.secondary",
                "testing.enchantment->sell.enchantment",
                "treasury.money->payroll.money",
            ],
            edgeKeys);

        var intoMerge = layout.Edges
            .Where(e => e.To.Value == "merge")
            .OrderBy(e => e.ToPort.Value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, intoMerge.Length);
        Assert.Equal("primary", intoMerge[0].ToPort.Value);
        Assert.Equal("secondary", intoMerge[1].ToPort.Value);

        var primaryEnd = intoMerge[0].Points[^1];
        var secondaryEnd = intoMerge[1].Points[^1];
        Assert.True(
            Math.Abs(primaryEnd.X - secondaryEnd.X) > 1.0,
            $"Expected distinct MSAGL port X for merge inputs, got primary={primaryEnd.X}, secondary={secondaryEnd.X}");

        var mergeNode = layout.Nodes.Single(n => n.Id.Value == "merge");
        Assert.True(primaryEnd.Y > mergeNode.Center.Y, "Inputs should attach toward the top of merge (Y-up).");
        Assert.True(secondaryEnd.Y > mergeNode.Center.Y, "Inputs should attach toward the top of merge (Y-up).");

        var mergeOut = layout.Edges.Single(e => e.From.Value == "merge");
        Assert.True(
            mergeOut.Points[0].Y < mergeNode.Center.Y,
            "Merge output should leave toward the bottom (Y-up).");
    }
}
