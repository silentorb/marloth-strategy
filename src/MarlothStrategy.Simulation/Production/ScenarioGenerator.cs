using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Seeded random scenario: essential graph ± testing/merge, plus a subset of the actor pool.
/// </summary>
public static class ScenarioGenerator
{
    public const int MinActors = 2;
    public const int MaxActors = 4;

    public static ScenarioSpec Generate(int seed, ImmutableArray<ActorId> pool)
    {
        if (pool.Length < MinActors)
        {
            throw new InvalidOperationException(
                $"Actor pool must contain at least {MinActors} actors to generate a scenario.");
        }

        var random = new Random(seed);
        var includeTestingMerge = random.Next(2) == 1;
        var (graph, _) = GraphFactory.Create(includeTestingMerge);
        var nodes = graph.Nodes.Keys
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        var maxActors = Math.Min(MaxActors, pool.Length);
        var actorCount = random.Next(MinActors, maxActors + 1);
        var shuffledPool = pool.ToArray();
        Shuffle(shuffledPool, random);
        var actorIds = shuffledPool.Take(actorCount).ToArray();

        var assignments = BuildAssignments(actorIds, nodes, random);

        return new ScenarioSpec(
            includeTestingMerge,
            actorIds.ToImmutableArray(),
            assignments);
    }

    /// <summary>
    /// Every node gets at least one preferred assignment. Overlap (multiple actors on one node)
    /// is allowed but sparse, and more likely when actors are plentiful relative to nodes.
    /// </summary>
    public static ImmutableArray<Assignment> BuildAssignments(
        ActorId[] actorIds,
        NodeId[] nodes,
        Random random)
    {
        if (actorIds.Length == 0)
        {
            throw new InvalidOperationException("Cannot build assignments with an empty actor roster.");
        }

        if (nodes.Length == 0)
        {
            throw new InvalidOperationException("Cannot build assignments with an empty graph.");
        }

        var pairs = new HashSet<(ActorId ActorId, NodeId NodeId)>();
        var actorOrder = actorIds.ToArray();
        Shuffle(actorOrder, random);

        // Coverage pass: distribute every node across actors (round-robin after shuffle).
        var shuffledNodes = nodes.ToArray();
        Shuffle(shuffledNodes, random);
        for (var i = 0; i < shuffledNodes.Length; i++)
        {
            pairs.Add((actorOrder[i % actorOrder.Length], shuffledNodes[i]));
        }

        // Orphan actors (possible when actors > nodes) each pick a random node → intentional overlap.
        foreach (var actorId in actorIds)
        {
            if (pairs.Any(p => p.ActorId == actorId))
            {
                continue;
            }

            pairs.Add((actorId, nodes[random.Next(nodes.Length)]));
        }

        // Sparse extras: budget grows with actors / nodes so overlap is less common when nodes dominate.
        var maxExtras = Math.Max(1, (actorIds.Length * actorIds.Length) / nodes.Length);
        var extraCount = random.Next(0, maxExtras + 1);
        for (var e = 0; e < extraCount; e++)
        {
            var actorId = actorIds[random.Next(actorIds.Length)];
            var nodeId = nodes[random.Next(nodes.Length)];
            pairs.Add((actorId, nodeId));
        }

        return pairs
            .OrderBy(p => p.ActorId.Value, StringComparer.Ordinal)
            .ThenBy(p => p.NodeId.Value, StringComparer.Ordinal)
            .Select(p => new Assignment(p.ActorId, p.NodeId))
            .ToImmutableArray();
    }

    private static void Shuffle<T>(T[] items, Random random)
    {
        for (var i = items.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
