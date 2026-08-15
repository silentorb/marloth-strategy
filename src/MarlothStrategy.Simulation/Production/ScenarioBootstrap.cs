using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Time;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Resolves a <see cref="GameConfig"/> into an initial <see cref="GameState"/>:
/// named preset, or a seeded random scenario.
/// </summary>
public static class ScenarioBootstrap
{
    public const double StartingTreasuryMoney = 100;

    public static GameState CreateInitialState(GameConfig config) =>
        CreateInitialState(
            config,
            NodeTypeConfigLoader.LoadFromBaseDirectory(),
            ActorConfigLoader.LoadFromBaseDirectory(),
            ActorPoolLoader.LoadFromBaseDirectory(),
            TimePartitionConfigLoader.LoadFromBaseDirectory());

    public static GameState CreateInitialState(
        GameConfig config,
        NodeTypeConfigs nodeConfigs,
        ImmutableDictionary<ActorId, Actor> actors,
        ImmutableArray<ActorId> actorPool) =>
        CreateInitialState(
            config,
            nodeConfigs,
            actors,
            actorPool,
            TimePartitionConfigLoader.LoadFromBaseDirectory());

    public static GameState CreateInitialState(
        GameConfig config,
        NodeTypeConfigs nodeConfigs,
        ImmutableDictionary<ActorId, Actor> actors,
        ImmutableArray<ActorId> actorPool,
        TimePartitionConfig timePartitions)
    {
        ArgumentNullException.ThrowIfNull(config);
        var spec = ResolveSpec(config, actorPool);
        return Materialize(spec, nodeConfigs, actors, timePartitions);
    }

    public static GameState CreateFromPreset(
        string name,
        NodeTypeConfigs nodeConfigs,
        ImmutableDictionary<ActorId, Actor> actors) =>
        CreateFromPreset(
            name,
            nodeConfigs,
            actors,
            TimePartitionConfigLoader.LoadFromBaseDirectory());

    public static GameState CreateFromPreset(
        string name,
        NodeTypeConfigs nodeConfigs,
        ImmutableDictionary<ActorId, Actor> actors,
        TimePartitionConfig timePartitions) =>
        Materialize(
            ScenarioPresetLoader.LoadFromBaseDirectory(name),
            nodeConfigs,
            actors,
            timePartitions);

    public static ScenarioSpec ResolveSpec(GameConfig config, ImmutableArray<ActorId> actorPool)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!string.IsNullOrWhiteSpace(config.ScenarioPreset))
        {
            return ScenarioPresetLoader.LoadFromBaseDirectory(config.ScenarioPreset.Trim());
        }

        return ScenarioGenerator.Generate(config.ScenarioSeed, actorPool);
    }

    public static GameState Materialize(
        ScenarioSpec spec,
        NodeTypeConfigs nodeConfigs,
        ImmutableDictionary<ActorId, Actor> actors) =>
        Materialize(spec, nodeConfigs, actors, TimePartitionConfigLoader.LoadFromBaseDirectory());

    public static GameState Materialize(
        ScenarioSpec spec,
        NodeTypeConfigs nodeConfigs,
        ImmutableDictionary<ActorId, Actor> actors,
        TimePartitionConfig timePartitions)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(nodeConfigs);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(timePartitions);

        if (nodeConfigs.Payroll.Period <= 0)
        {
            throw new InvalidOperationException(
                "Payroll period must be a positive integer.");
        }

        if (spec.IncludeTesting && spec.IncludeDesign)
        {
            throw new InvalidOperationException(
                "Scenario cannot include both testing and design.");
        }

        var (graph, catalog) = GraphFactory.Create(spec.IncludeTesting, spec.IncludeDesign);

        var roster = ImmutableDictionary.CreateBuilder<ActorId, Actor>();
        foreach (var actorId in spec.ActorIds)
        {
            if (!actors.TryGetValue(actorId, out var actor))
            {
                throw new InvalidOperationException(
                    $"Scenario requires actor '{actorId}'.");
            }

            if (!roster.TryAdd(actorId, actor))
            {
                throw new InvalidOperationException(
                    $"Scenario lists actor '{actorId}' more than once.");
            }
        }

        if (roster.Count == 0)
        {
            throw new InvalidOperationException("Scenario roster must contain at least one actor.");
        }

        var seenPairs = new HashSet<(ActorId ActorId, NodeId NodeId)>();
        foreach (var assignment in spec.Assignments)
        {
            if (!roster.ContainsKey(assignment.ActorId))
            {
                throw new InvalidOperationException(
                    $"Scenario assignment actor '{assignment.ActorId}' is not in the roster.");
            }

            if (!graph.Nodes.ContainsKey(assignment.NodeId))
            {
                throw new InvalidOperationException(
                    $"Scenario assignment node '{assignment.NodeId}' is not in the graph.");
            }

            if (assignment.Weight <= 0)
            {
                throw new InvalidOperationException(
                    "Scenario assignment weight must be positive.");
            }

            if (!seenPairs.Add((assignment.ActorId, assignment.NodeId)))
            {
                throw new InvalidOperationException(
                    $"Scenario has a duplicate assignment for '{assignment.ActorId}' → '{assignment.NodeId}'.");
            }
        }

        var genesis = EnchantmentBlock.CreateGenesis();
        var signals = ImmutableDictionary<PortKey, SignalValue>.Empty
            .Add(
                new PortKey(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
                new SignalValue.Enchantment(genesis))
            .Add(
                new PortKey(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                new SignalValue.Money(StartingTreasuryMoney));

        var timers = ImmutableDictionary<NodeId, int>.Empty
            .Add(MagicAgencySeed.PayrollNodeId, 0);

        var blocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
            .Add(genesis.Hash, genesis);

        return new GameState(
            graph,
            catalog,
            signals,
            roster.ToImmutable(),
            spec.Assignments,
            nodeConfigs,
            ImmutableDictionary<NodeId, double>.Empty,
            timers,
            ImmutableDictionary<NodeId, int>.Empty,
            ImmutableArray<PendingMoneyMove>.Empty,
            blocks,
            NextUnitId: 1,
            Tick: 0,
            timePartitions);
    }
}
