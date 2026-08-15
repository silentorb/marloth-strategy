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
    public void Compute_SeedGraph_UsesPortAnchorsAndEnchantSelfLoop()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var layout = FlowGraphLayout.Compute(state);

        var edgeKeys = layout.Edges
            .Select(e => $"{e.From.Value}.{e.FromPort.Value}->{e.To.Value}.{e.ToPort.Value}")
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "enchant.enchantment->testing.enchantment",
                "sell.money->treasury.money",
                "testing.enchantment->enchant.enchantment",
                "testing.enchantment->sell.enchantment",
                "treasury.money->payroll.money",
            ],
            edgeKeys);

        Assert.True(layout.Nodes.Single(n => n.Id.Value == "enchant").HasSelfLoop);
        Assert.DoesNotContain(layout.Edges, e => e.From == e.To);
        Assert.DoesNotContain(layout.Nodes, n => n.Id.Value == "merge");
    }

    [Fact]
    public void Compute_MergeFixture_UsesDistinctMergeInputAnchors()
    {
        var layout = FlowGraphLayout.Compute(MergeFixture());

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

        Assert.False(layout.Nodes.Single(n => n.Id.Value == "enchant").HasSelfLoop);
    }

    [Fact]
    public void Compute_EssentialGraph_MarksEnchantSelfLoopAndOmitsItFromRoutedEdges()
    {
        var spec = new ScenarioSpec(
            IncludeTesting: false,
            ImmutableArray.Create(MagicAgencySeed.ActorId),
            ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId)));
        var state = ScenarioBootstrap.Materialize(spec, DefaultConfigs, DefaultActors);
        var layout = FlowGraphLayout.Compute(state);

        Assert.True(layout.Nodes.Single(n => n.Id.Value == "enchant").HasSelfLoop);
        Assert.DoesNotContain(layout.Edges, e => e.From == e.To);
        Assert.Contains(
            layout.Edges,
            e => e.From.Value == "enchant" && e.To.Value == "sell");
    }

    [Fact]
    public void Compute_DesignGraph_RoutesEnchantmentThroughDesign()
    {
        var spec = new ScenarioSpec(
            IncludeTesting: false,
            ImmutableArray.Create(MagicAgencySeed.ActorId),
            ImmutableArray.Create(
                new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.DesignNodeId)),
            IncludeDesign: true);
        var state = ScenarioBootstrap.Materialize(spec, DefaultConfigs, DefaultActors);
        var layout = FlowGraphLayout.Compute(state);

        Assert.Contains(layout.Nodes, n => n.Id.Value == "design");
        Assert.Contains(
            layout.Edges,
            e => e.From.Value == "enchant"
                 && e.FromPort.Value == "enchantment"
                 && e.To.Value == "design"
                 && e.ToPort.Value == "enchantment");
        Assert.Contains(
            layout.Edges,
            e => e.From.Value == "design"
                 && e.FromPort.Value == "enchantment"
                 && e.To.Value == "enchant"
                 && e.ToPort.Value == "enchantment");
        Assert.Contains(
            layout.Edges,
            e => e.From.Value == "enchant" && e.To.Value == "sell");
    }
}
