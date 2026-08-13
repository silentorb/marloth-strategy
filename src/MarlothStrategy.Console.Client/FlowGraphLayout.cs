using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using DrawingGraph = Microsoft.Msagl.Drawing.Graph;
using GeomEdge = Microsoft.Msagl.Core.Layout.Edge;
using GeomNode = Microsoft.Msagl.Core.Layout.Node;

namespace MarlothStrategy.Console.Client;

public readonly record struct FlowGraphPoint(double X, double Y);

public sealed record FlowGraphLaidOutNode(NodeId Id, FlowGraphPoint Center, bool HasSelfLoop);

public sealed record FlowGraphLaidOutEdge(
    NodeId From,
    NodeId To,
    IReadOnlyList<FlowGraphPoint> Points);

public sealed record FlowGraphLayoutResult(
    IReadOnlyList<FlowGraphLaidOutNode> Nodes,
    IReadOnlyList<FlowGraphLaidOutEdge> Edges);

/// <summary>Sugiyama layout of the production flow graph via MSAGL (node→node edges).</summary>
public static class FlowGraphLayout
{
    private const int CurveSamples = 8;
    private const double NodeHeight = 24;
    private const double NodeWidthPadding = 24;
    private const double NodeWidthPerChar = 8;

    public static FlowGraphLayoutResult Compute(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var nodeIds = state.Graph.Nodes.Keys
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        if (nodeIds.Length == 0)
        {
            return new FlowGraphLayoutResult([], []);
        }

        var edgePairs = state.Graph.Edges.Values
            .Select(e => (From: e.From.Node, To: e.To.Node))
            .Distinct()
            .ToArray();

        var selfLoops = edgePairs
            .Where(e => e.From == e.To)
            .Select(e => e.From)
            .ToHashSet();

        var forwardEdges = edgePairs
            .Where(e => e.From != e.To)
            .OrderBy(e => e.From.Value, StringComparer.Ordinal)
            .ThenBy(e => e.To.Value, StringComparer.Ordinal)
            .ToArray();

        var drawing = new DrawingGraph("flow");
        drawing.Attr.LayerDirection = LayerDirection.TB;

        foreach (var id in nodeIds)
        {
            var node = drawing.AddNode(id.Value);
            node.Attr.Shape = Shape.Box;
            node.LabelText = id.Value;
        }

        foreach (var (from, to) in forwardEdges)
        {
            drawing.AddEdge(from.Value, to.Value);
        }

        var geometry = new GeometryGraph();
        var geomById = new Dictionary<string, GeomNode>(StringComparer.Ordinal);

        foreach (var drawingNode in drawing.Nodes)
        {
            var width = NodeWidthPadding + drawingNode.Id.Length * NodeWidthPerChar;
            var curve = CurveFactory.CreateRectangle(
                width,
                NodeHeight,
                new Point(0, 0));
            var geomNode = new GeomNode(curve, drawingNode);
            geometry.Nodes.Add(geomNode);
            geomById[drawingNode.Id] = geomNode;
            drawingNode.GeometryNode = geomNode;
        }

        foreach (var drawingEdge in drawing.Edges)
        {
            var geomEdge = new GeomEdge(geomById[drawingEdge.Source], geomById[drawingEdge.Target]);
            geometry.Edges.Add(geomEdge);
            drawingEdge.GeometryEdge = geomEdge;
        }

        drawing.GeometryGraph = geometry;

        var settings = new SugiyamaLayoutSettings();
        LayoutHelpers.CalculateLayout(geometry, settings, cancelToken: null);
        geometry.UpdateBoundingBox();

        if (double.IsNaN(geometry.BoundingBox.Width) || double.IsNaN(geometry.BoundingBox.Height))
        {
            throw new InvalidOperationException("MSAGL returned an unusable bounding box for the flow graph.");
        }

        var nodes = new List<FlowGraphLaidOutNode>(nodeIds.Length);
        foreach (var id in nodeIds)
        {
            if (!geomById.TryGetValue(id.Value, out var geomNode))
            {
                throw new InvalidOperationException($"MSAGL layout missing geometry for node '{id.Value}'.");
            }

            nodes.Add(new FlowGraphLaidOutNode(
                id,
                new FlowGraphPoint(geomNode.Center.X, geomNode.Center.Y),
                selfLoops.Contains(id)));
        }

        var edges = new List<FlowGraphLaidOutEdge>(forwardEdges.Length);
        foreach (var drawingEdge in drawing.Edges)
        {
            var curve = drawingEdge.GeometryEdge.Curve
                ?? throw new InvalidOperationException(
                    $"MSAGL layout missing curve for edge '{drawingEdge.Source}' → '{drawingEdge.Target}'.");

            var points = SampleCurve(curve, CurveSamples);
            edges.Add(new FlowGraphLaidOutEdge(
                new NodeId(drawingEdge.Source),
                new NodeId(drawingEdge.Target),
                points));
        }

        edges.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.From.Value, b.From.Value);
            return c != 0 ? c : string.CompareOrdinal(a.To.Value, b.To.Value);
        });

        return new FlowGraphLayoutResult(nodes, edges);
    }

    private static IReadOnlyList<FlowGraphPoint> SampleCurve(ICurve curve, int samples)
    {
        var points = new FlowGraphPoint[samples + 1];
        for (var i = 0; i <= samples; i++)
        {
            var par = curve.ParStart + (curve.ParEnd - curve.ParStart) * i / samples;
            var p = curve[par];
            points[i] = new FlowGraphPoint(p.X, p.Y);
        }

        return points;
    }
}
