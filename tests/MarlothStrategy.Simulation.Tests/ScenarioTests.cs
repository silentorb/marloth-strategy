using System.Collections.Immutable;
using MarlothStrategy.Simulation;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Simulation.Tests;

public sealed class ScenarioTests
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
        new PayrollNodeConfig(DefaultWage: 10, Period: 5, Effort: 1),
        new MergeNodeConfig(Effort: 1),
        new DesignNodeConfig(Effort: 3, DesignDelta: 1, DarknessReduction: 0.9));

    private static readonly ActorId ConsultantId = new("consultant");

    [Fact]
    public void Lab01_MatchesCommittedPresetTopologyAndAssignments()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();
        var state = ScenarioBootstrap.CreateFromPreset(
            ScenarioPresetLoader.Lab01Name,
            DefaultConfigs,
            actors);

        Assert.Equal(5, state.Graph.Nodes.Count);
        Assert.Equal(6, state.Graph.Edges.Count);
        Assert.True(state.Graph.Nodes.ContainsKey(MagicAgencySeed.TestingNodeId));
        Assert.False(state.Graph.Nodes.ContainsKey(MagicAgencySeed.MergeNodeId));
        Assert.False(state.Graph.Nodes.ContainsKey(MagicAgencySeed.DesignNodeId));
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.TestingNodeId);
        Assert.Contains(
            state.Graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId);

        Assert.Equal(2, state.Actors.Count);
        Assert.False(state.Actors.ContainsKey(ConsultantId));
        Assert.Equal(5, state.Assignments.Length);
        Assert.All(state.Assignments, a => Assert.Equal(1m, a.Weight));
        Assert.Equal(100, Assert.IsType<SignalValue.Money>(
            state.PortSignals[new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId)]).Amount);
        Assert.Equal(0, state.NodeTimers[MagicAgencySeed.PayrollNodeId]);
    }

    [Fact]
    public void MagicAgencySeed_LoadsLab01()
    {
        var fromSeed = MagicAgencySeed.CreateInitialState(DefaultConfigs, ActorConfigLoader.LoadFromBaseDirectory());
        var fromPreset = ScenarioBootstrap.CreateFromPreset(
            ScenarioPresetLoader.Lab01Name,
            DefaultConfigs,
            ActorConfigLoader.LoadFromBaseDirectory());

        Assert.Equal(fromPreset.Graph.Nodes.Count, fromSeed.Graph.Nodes.Count);
        Assert.Equal(fromPreset.Graph.Edges.Count, fromSeed.Graph.Edges.Count);
        Assert.Equal(
            fromPreset.Graph.Nodes.Keys.Select(id => id.Value).OrderBy(v => v, StringComparer.Ordinal),
            fromSeed.Graph.Nodes.Keys.Select(id => id.Value).OrderBy(v => v, StringComparer.Ordinal));
        Assert.Equal(
            fromPreset.Actors.Keys.Select(id => id.Value).OrderBy(v => v, StringComparer.Ordinal),
            fromSeed.Actors.Keys.Select(id => id.Value).OrderBy(v => v, StringComparer.Ordinal));
        Assert.Equal(fromPreset.Assignments.ToArray(), fromSeed.Assignments.ToArray());
    }

    [Fact]
    public void GraphFactory_Essential_HasEnchantSelfLoopAndSellWithoutTestingMerge()
    {
        var (graph, catalog) = GraphFactory.Create(includeTesting: false);

        Assert.Equal(4, graph.Nodes.Count);
        Assert.False(graph.Nodes.ContainsKey(MagicAgencySeed.TestingNodeId));
        Assert.False(graph.Nodes.ContainsKey(MagicAgencySeed.MergeNodeId));
        Assert.Equal(4, graph.Edges.Count);
        Assert.Contains(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId);
        Assert.Contains(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.SellNodeId);
        Assert.Contains(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.SellNodeId
                 && e.To.Node == MagicAgencySeed.TreasuryNodeId);
        Assert.Contains(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.TreasuryNodeId
                 && e.To.Node == MagicAgencySeed.PayrollNodeId);
        Assert.True(catalog.Types.ContainsKey(MagicAgencySeed.TestingTypeId));
        Assert.True(catalog.Types.ContainsKey(MagicAgencySeed.MergeTypeId));
        Assert.True(catalog.Types.ContainsKey(MagicAgencySeed.DesignTypeId));
        Assert.True(
            catalog.Get(MagicAgencySeed.EnchantTypeId).Inputs.ContainsKey(
                MagicAgencySeed.EnchantmentPortId));
        Assert.True(
            catalog.Get(MagicAgencySeed.DesignTypeId).Inputs.ContainsKey(
                MagicAgencySeed.EnchantmentPortId));
        Assert.True(
            catalog.Get(MagicAgencySeed.DesignTypeId).Outputs.ContainsKey(
                MagicAgencySeed.EnchantmentPortId));
    }

    [Fact]
    public void GraphFactory_Testing_KeepsEnchantSelfLoopAndAddsTestingFanIn()
    {
        var (graph, catalog) = GraphFactory.Create(includeTesting: true);

        Assert.Equal(5, graph.Nodes.Count);
        Assert.Equal(6, graph.Edges.Count);
        Assert.True(graph.Nodes.ContainsKey(MagicAgencySeed.TestingNodeId));
        Assert.False(graph.Nodes.ContainsKey(MagicAgencySeed.MergeNodeId));
        Assert.Contains(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId);
        Assert.Contains(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.TestingNodeId);
        Assert.Contains(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.TestingNodeId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId);
        Assert.DoesNotContain(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.SellNodeId);
        Assert.True(catalog.Types.ContainsKey(MagicAgencySeed.MergeTypeId));
        Assert.False(graph.Nodes.ContainsKey(MagicAgencySeed.DesignNodeId));
    }

    [Fact]
    public void GraphFactory_Design_RoutesLoopbackThroughDesign()
    {
        var (graph, catalog) = GraphFactory.Create(includeTesting: false, includeDesign: true);

        Assert.Equal(5, graph.Nodes.Count);
        Assert.Equal(5, graph.Edges.Count);
        Assert.True(graph.Nodes.ContainsKey(MagicAgencySeed.DesignNodeId));
        Assert.False(graph.Nodes.ContainsKey(MagicAgencySeed.TestingNodeId));
        Assert.DoesNotContain(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId);
        Assert.Contains(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.DesignNodeId
                 && e.From.Port == MagicAgencySeed.EnchantmentPortId
                 && e.To.Port == MagicAgencySeed.EnchantmentPortId);
        Assert.Contains(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.DesignNodeId
                 && e.To.Node == MagicAgencySeed.EnchantNodeId
                 && e.From.Port == MagicAgencySeed.EnchantmentPortId
                 && e.To.Port == MagicAgencySeed.EnchantmentPortId);
        Assert.Contains(
            graph.Edges.Values,
            e => e.From.Node == MagicAgencySeed.EnchantNodeId
                 && e.To.Node == MagicAgencySeed.SellNodeId);
        Assert.True(catalog.Types.ContainsKey(MagicAgencySeed.DesignTypeId));
    }

    [Fact]
    public void GraphFactory_TestingAndDesignTogether_FailsFast()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GraphFactory.Create(includeTesting: true, includeDesign: true));
        Assert.Contains("mutually exclusive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActorPool_IsExplicitSubsetOfOnDiskActors()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();
        var pool = ActorPoolLoader.LoadFromBaseDirectory();

        Assert.True(actors.ContainsKey(ConsultantId));
        Assert.DoesNotContain(ConsultantId, pool);
        Assert.Contains(MagicAgencySeed.ActorId, pool);
        Assert.Contains(MagicAgencySeed.BossActorId, pool);
        Assert.True(pool.Length >= 4);
        Assert.All(pool, id => Assert.True(actors.ContainsKey(id)));
    }

    [Fact]
    public void Generator_IsDeterministicForFixedSeed()
    {
        var pool = ActorPoolLoader.LoadFromBaseDirectory();
        var first = ScenarioGenerator.Generate(42, pool);
        var second = ScenarioGenerator.Generate(42, pool);

        Assert.Equal(first.IncludeTesting, second.IncludeTesting);
        Assert.Equal(first.IncludeDesign, second.IncludeDesign);
        Assert.False(first.IncludeTesting && first.IncludeDesign);
        Assert.Equal(first.ActorIds.ToArray(), second.ActorIds.ToArray());
        Assert.Equal(first.Assignments.ToArray(), second.Assignments.ToArray());
        Assert.InRange(first.ActorIds.Length, 2, 4);
        Assert.All(first.ActorIds, id => Assert.Contains(id, pool));
        Assert.DoesNotContain(ConsultantId, first.ActorIds);
        Assert.All(
            first.Assignments,
            a =>
            {
                Assert.Contains(a.ActorId, first.ActorIds);
                Assert.Equal(1m, a.Weight);
            });
        Assert.All(first.ActorIds, id => Assert.Contains(first.Assignments, a => a.ActorId == id));
        AssertEveryNodeCovered(first);
    }

    [Fact]
    public void Generator_CoversEveryNodeAcrossSeeds()
    {
        var pool = ActorPoolLoader.LoadFromBaseDirectory();
        for (var seed = 0; seed < 32; seed++)
        {
            AssertEveryNodeCovered(ScenarioGenerator.Generate(seed, pool));
        }
    }

    [Fact]
    public void Generator_CanProduceNodeOverlap()
    {
        var pool = ActorPoolLoader.LoadFromBaseDirectory();
        var foundOverlap = false;
        for (var seed = 0; seed < 128; seed++)
        {
            var spec = ScenarioGenerator.Generate(seed, pool);
            if (spec.Assignments.GroupBy(a => a.NodeId).Any(g => g.Count() > 1))
            {
                foundOverlap = true;
                break;
            }
        }

        Assert.True(foundOverlap);
    }

    [Fact]
    public void BuildAssignments_CoversAllNodesAndAllActors()
    {
        var actors = new[]
        {
            new ActorId("a"),
            new ActorId("b"),
            new ActorId("c"),
        };
        var nodes = new[]
        {
            MagicAgencySeed.EnchantNodeId,
            MagicAgencySeed.SellNodeId,
            MagicAgencySeed.TreasuryNodeId,
            MagicAgencySeed.PayrollNodeId,
        };

        var assignments = ScenarioGenerator.BuildAssignments(actors, nodes, new Random(3));
        Assert.All(nodes, n => Assert.Contains(assignments, a => a.NodeId == n));
        Assert.All(actors, id => Assert.Contains(assignments, a => a.ActorId == id));
    }

    [Fact]
    public void Generator_DifferentSeedsCanDiffer()
    {
        var pool = ActorPoolLoader.LoadFromBaseDirectory();
        var first = ScenarioGenerator.Generate(1, pool);
        var foundDifference = false;
        for (var seed = 2; seed < 64; seed++)
        {
            if (!SpecsEqual(first, ScenarioGenerator.Generate(seed, pool)))
            {
                foundDifference = true;
                break;
            }
        }

        Assert.True(foundDifference);
    }

    [Fact]
    public void Generator_ChoosesNoneTestingOrDesignEquallyExclusive()
    {
        var pool = ActorPoolLoader.LoadFromBaseDirectory();
        var none = 0;
        var testing = 0;
        var design = 0;
        for (var seed = 0; seed < 96; seed++)
        {
            var spec = ScenarioGenerator.Generate(seed, pool);
            Assert.False(spec.IncludeTesting && spec.IncludeDesign);
            if (spec.IncludeTesting)
            {
                testing++;
            }
            else if (spec.IncludeDesign)
            {
                design++;
            }
            else
            {
                none++;
            }
        }

        Assert.True(none > 0);
        Assert.True(testing > 0);
        Assert.True(design > 0);
    }

    [Fact]
    public void Bootstrap_UnsetPreset_UsesGenerator()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();
        var pool = ActorPoolLoader.LoadFromBaseDirectory();
        var config = new GameConfig { ScenarioPreset = null, ScenarioSeed = 7 };
        var state = ScenarioBootstrap.CreateInitialState(config, DefaultConfigs, actors, pool);
        var spec = ScenarioGenerator.Generate(7, pool);

        Assert.Equal(spec.IncludeTesting, state.Graph.Nodes.ContainsKey(MagicAgencySeed.TestingNodeId));
        Assert.Equal(spec.IncludeDesign, state.Graph.Nodes.ContainsKey(MagicAgencySeed.DesignNodeId));
        Assert.Equal(spec.ActorIds.Length, state.Actors.Count);
        Assert.Equal(spec.Assignments.ToArray(), state.Assignments.ToArray());
        Assert.DoesNotContain(ConsultantId, state.Actors.Keys);
    }

    [Fact]
    public void Bootstrap_PresetLab01_IgnoresSeedForGraph()
    {
        var actors = ActorConfigLoader.LoadFromBaseDirectory();
        var pool = ActorPoolLoader.LoadFromBaseDirectory();
        var config = new GameConfig { ScenarioPreset = "lab01", ScenarioSeed = 99 };
        var state = ScenarioBootstrap.CreateInitialState(config, DefaultConfigs, actors, pool);

        Assert.Equal(5, state.Graph.Nodes.Count);
        Assert.Equal(2, state.Actors.Count);
        Assert.Contains(state.Actors.Keys, id => id == MagicAgencySeed.ActorId);
        Assert.Contains(state.Actors.Keys, id => id == MagicAgencySeed.BossActorId);
    }

    [Fact]
    public void PresetLoader_UnknownName_FailsFast()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ScenarioPresetLoader.LoadFromBaseDirectory("no-such-preset"));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PresetLoader_PathLikeName_FailsFast()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ScenarioPresetLoader.LoadFromBaseDirectory("../actors/intern"));
        Assert.Contains("Invalid scenario preset name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_AssignmentToAbsentNode_FailsFast()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty));
        var spec = new ScenarioSpec(
            IncludeTesting: false,
            ImmutableArray.Create(MagicAgencySeed.ActorId),
            ImmutableArray.Create(new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.TestingNodeId)));

        var ex = Assert.Throws<InvalidOperationException>(
            () => ScenarioBootstrap.Materialize(spec, DefaultConfigs, actors));
        Assert.Contains("not in the graph", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_MissingActor_FailsFast()
    {
        var spec = new ScenarioSpec(
            IncludeTesting: false,
            ImmutableArray.Create(new ActorId("ghost")),
            ImmutableArray.Create(new Assignment(new ActorId("ghost"), MagicAgencySeed.EnchantNodeId)));

        var ex = Assert.Throws<InvalidOperationException>(
            () => ScenarioBootstrap.Materialize(
                spec,
                DefaultConfigs,
                ImmutableDictionary<ActorId, Actor>.Empty));
        Assert.Contains("ghost", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_TestingAndDesignTogether_FailsFast()
    {
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            MagicAgencySeed.ActorId,
            new Actor(
                MagicAgencySeed.ActorId,
                Capacity: 1.0m,
                ImmutableDictionary<string, double>.Empty));
        var spec = new ScenarioSpec(
            IncludeTesting: true,
            ImmutableArray.Create(MagicAgencySeed.ActorId),
            ImmutableArray.Create(new Assignment(MagicAgencySeed.ActorId, MagicAgencySeed.EnchantNodeId)),
            IncludeDesign: true);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ScenarioBootstrap.Materialize(spec, DefaultConfigs, actors));
        Assert.Contains("both testing and design", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActorPoolLoader_UnknownId_FailsFast()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"marloth-pool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, ActorPoolLoader.FileName),
                """{"actors":["intern","missing-actor"]}""");
            var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
                MagicAgencySeed.ActorId,
                new Actor(
                    MagicAgencySeed.ActorId,
                    Capacity: 1.0m,
                    ImmutableDictionary<string, double>.Empty));

            var ex = Assert.Throws<InvalidOperationException>(
                () => ActorPoolLoader.LoadFromDirectory(directory, actors));
            Assert.Contains("missing-actor", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertEveryNodeCovered(ScenarioSpec spec)
    {
        var (graph, _) = GraphFactory.Create(spec.IncludeTesting, spec.IncludeDesign);
        Assert.All(
            graph.Nodes.Keys,
            nodeId => Assert.Contains(spec.Assignments, a => a.NodeId == nodeId));
    }

    private static bool SpecsEqual(ScenarioSpec a, ScenarioSpec b) =>
        a.IncludeTesting == b.IncludeTesting
        && a.IncludeDesign == b.IncludeDesign
        && a.ActorIds.SequenceEqual(b.ActorIds)
        && a.Assignments.SequenceEqual(b.Assignments);
}
