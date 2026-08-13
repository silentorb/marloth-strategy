using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

/// <summary>Primitive ASCII flow graph: boxed node ids and directed connectors from edges.</summary>
public static class FlowGraphPrinter
{
    public static IReadOnlyList<string> FormatLines(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var nodes = state.Graph.Nodes.Keys
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        var edgePairs = state.Graph.Edges.Values
            .Select(e => (From: e.From.Node, To: e.To.Node))
            .Distinct()
            .ToArray();

        var selfLoops = edgePairs
            .Where(e => e.From == e.To)
            .Select(e => e.From)
            .ToHashSet();

        var forward = edgePairs
            .Where(e => e.From != e.To)
            .GroupBy(e => e.From)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.To).Distinct().OrderBy(n => n.Value, StringComparer.Ordinal).ToArray());

        var incomingCount = new Dictionary<NodeId, int>();
        foreach (var n in nodes)
        {
            incomingCount[n] = 0;
        }

        foreach (var e in edgePairs.Where(e => e.From != e.To))
        {
            incomingCount[e.To] = incomingCount.GetValueOrDefault(e.To) + 1;
        }

        // Longest simple path via greedy: start at roots, always follow first unused outgoing.
        var usedEdge = new HashSet<(NodeId From, NodeId To)>();
        var chains = new List<List<NodeId>>();
        var inChain = new HashSet<NodeId>();

        IEnumerable<NodeId> StartCandidates() =>
            nodes
                .Where(n => !inChain.Contains(n))
                .OrderBy(n => incomingCount.GetValueOrDefault(n))
                .ThenBy(n => n.Value, StringComparer.Ordinal);

        while (inChain.Count < nodes.Length)
        {
            var start = StartCandidates().First();
            var chain = new List<NodeId> { start };
            inChain.Add(start);
            var current = start;

            while (forward.TryGetValue(current, out var outs))
            {
                NodeId? chosen = null;
                foreach (var o in outs)
                {
                    if (usedEdge.Contains((current, o)) || inChain.Contains(o))
                    {
                        continue;
                    }

                    chosen = o;
                    break;
                }

                if (chosen is null)
                {
                    break;
                }

                usedEdge.Add((current, chosen.Value));
                chain.Add(chosen.Value);
                inChain.Add(chosen.Value);
                current = chosen.Value;
            }

            chains.Add(chain);
        }

        var lines = new List<string>();
        for (var c = 0; c < chains.Count; c++)
        {
            if (c > 0)
            {
                lines.Add(string.Empty);
            }

            var chain = chains[c];
            for (var i = 0; i < chain.Count; i++)
            {
                if (i > 0)
                {
                    lines.Add("    │");
                    lines.Add("    ▼");
                }

                AppendNodeBox(lines, chain[i], selfLoops.Contains(chain[i]));
            }
        }

        // Residual forward edges not consumed by chains (e.g. extra outs) — annotate after graph.
        var residual = edgePairs
            .Where(e => e.From != e.To && !usedEdge.Contains(e))
            .OrderBy(e => e.From.Value, StringComparer.Ordinal)
            .ThenBy(e => e.To.Value, StringComparer.Ordinal)
            .ToArray();

        if (residual.Length > 0)
        {
            lines.Add(string.Empty);
            foreach (var e in residual)
            {
                lines.Add($"  {e.From.Value} → {e.To.Value}");
            }
        }

        return lines;
    }

    private static void AppendNodeBox(List<string> lines, NodeId nodeId, bool hasSelfLoop)
    {
        var label = nodeId.Value;
        var inner = $" {label} ";
        var width = inner.Length;
        var top = BoxDrawing.SingleTopLeft + new string(BoxDrawing.SingleHorizontal, width) + BoxDrawing.SingleTopRight;
        var mid = BoxDrawing.SingleVertical + inner + BoxDrawing.SingleVertical;
        var bottom = BoxDrawing.SingleBottomLeft + new string(BoxDrawing.SingleHorizontal, width) + BoxDrawing.SingleBottomRight;

        if (hasSelfLoop)
        {
            lines.Add($"  {top}");
            lines.Add($"  {mid}──┐");
            lines.Add($"  {bottom}  │");
            lines.Add("    └───┘");
        }
        else
        {
            lines.Add($"  {top}");
            lines.Add($"  {mid}");
            lines.Add($"  {bottom}");
        }
    }
}
