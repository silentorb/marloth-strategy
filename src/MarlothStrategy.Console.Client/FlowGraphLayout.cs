using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using GeomEdge = Microsoft.Msagl.Core.Layout.Edge;
using GeomNode = Microsoft.Msagl.Core.Layout.Node;

namespace MarlothStrategy.Console.Client;

public readonly record struct FlowGraphPoint(double X, double Y);

public sealed record FlowGraphLaidOutNode(
    NodeId Id,
    FlowGraphPoint Center,
    double Width,
    double Height,
    bool HasSelfLoop);

public sealed record FlowGraphLaidOutEdge(
    NodeId From,
    PortId FromPort,
    NodeId To,
    PortId ToPort,
    IReadOnlyList<FlowGraphPoint> Points);

public sealed record FlowGraphLayoutResult(
    IReadOnlyList<FlowGraphLaidOutNode> Nodes,
    IReadOnlyList<FlowGraphLaidOutEdge> Edges);

/// <summary>
/// Sugiyama layout via MSAGL with <see cref="RelativeFloatingPort"/> anchors so each game port
/// gets a distinct attachment on its node; edge polylines come from MSAGL routing.
/// </summary>
public static class FlowGraphLayout
{
    private const double NodeHeight = 28;
    private const double NodeWidthPadding = 28;
    private const double NodeWidthPerChar = 8;
    private const double MinNodeWidthForPorts = 48;

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

        var portEdges = state.Graph.Edges.Values
            .Select(e => (From: e.From.Node, FromPort: e.From.Port, To: e.To.Node, ToPort: e.To.Port))
            .Distinct()
            .OrderBy(e => e.From.Value, StringComparer.Ordinal)
            .ThenBy(e => e.FromPort.Value, StringComparer.Ordinal)
            .ThenBy(e => e.To.Value, StringComparer.Ordinal)
            .ThenBy(e => e.ToPort.Value, StringComparer.Ordinal)
            .ToArray();

        var selfLoops = portEdges
            .Where(e => e.From == e.To)
            .Select(e => e.From)
            .ToHashSet();

        var outputsByNode = portEdges
            .Where(e => e.From != e.To)
            .GroupBy(e => e.From)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.FromPort).Distinct().OrderBy(p => p.Value, StringComparer.Ordinal).ToArray());

        var inputsByNode = portEdges
            .Where(e => e.From != e.To)
            .GroupBy(e => e.To)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ToPort).Distinct().OrderBy(p => p.Value, StringComparer.Ordinal).ToArray());

        var geometry = new GeometryGraph();
        var geomById = new Dictionary<NodeId, GeomNode>();

        foreach (var id in nodeIds)
        {
            var portCount = Math.Max(
                outputsByNode.GetValueOrDefault(id)?.Length ?? 0,
                inputsByNode.GetValueOrDefault(id)?.Length ?? 0);
            var width = Math.Max(
                MinNodeWidthForPorts,
                NodeWidthPadding + id.Value.Length * NodeWidthPerChar);
            if (portCount > 1)
            {
                width = Math.Max(width, portCount * 24.0);
            }

            var curve = CurveFactory.CreateRectangle(width, NodeHeight, new Point(0, 0));
            var geomNode = new GeomNode(curve) { UserData = id.Value };
            geometry.Nodes.Add(geomNode);
            geomById[id] = geomNode;
        }

        var geomEdges = new List<(GeomEdge Edge, NodeId From, PortId FromPort, NodeId To, PortId ToPort)>();
        foreach (var (from, fromPort, to, toPort) in portEdges)
        {
            if (from == to)
            {
                continue;
            }

            var source = geomById[from];
            var target = geomById[to];
            var geomEdge = new GeomEdge(source, target)
            {
                UserData = $"{from.Value}.{fromPort.Value}->{to.Value}.{toPort.Value}",
            };

            var outPorts = outputsByNode[from];
            var inPorts = inputsByNode[to];
            geomEdge.SourcePort = CreatePort(source, outPorts, fromPort, isOutput: true);
            geomEdge.TargetPort = CreatePort(target, inPorts, toPort, isOutput: false);
            geometry.Edges.Add(geomEdge);
            geomEdges.Add((geomEdge, from, fromPort, to, toPort));
        }

        var settings = new SugiyamaLayoutSettings();
        settings.EdgeRoutingSettings.EdgeRoutingMode = EdgeRoutingMode.Rectilinear;
        LayoutHelpers.CalculateLayout(geometry, settings, cancelToken: null);
        geometry.UpdateBoundingBox();

        if (double.IsNaN(geometry.BoundingBox.Width) || double.IsNaN(geometry.BoundingBox.Height))
        {
            throw new InvalidOperationException("MSAGL returned an unusable bounding box for the flow graph.");
        }

        var nodes = new List<FlowGraphLaidOutNode>(nodeIds.Length);
        foreach (var id in nodeIds)
        {
            var geomNode = geomById[id];
            nodes.Add(new FlowGraphLaidOutNode(
                id,
                new FlowGraphPoint(geomNode.Center.X, geomNode.Center.Y),
                geomNode.Width,
                geomNode.Height,
                selfLoops.Contains(id)));
        }

        var edges = new List<FlowGraphLaidOutEdge>(geomEdges.Count);
        foreach (var (geomEdge, from, fromPort, to, toPort) in geomEdges)
        {
            var curve = geomEdge.Curve
                ?? throw new InvalidOperationException(
                    $"MSAGL layout missing curve for edge '{from.Value}.{fromPort.Value}' → '{to.Value}.{toPort.Value}'.");

            var points = ExtractPolyline(curve, geomEdge.SourcePort.Location, geomEdge.TargetPort.Location);
            if (points.Count < 2)
            {
                throw new InvalidOperationException(
                    $"MSAGL edge '{from.Value}.{fromPort.Value}' → '{to.Value}.{toPort.Value}' produced no polyline.");
            }

            edges.Add(new FlowGraphLaidOutEdge(from, fromPort, to, toPort, points));
        }

        return new FlowGraphLayoutResult(nodes, edges);
    }

    private static RelativeFloatingPort CreatePort(
        GeomNode node,
        IReadOnlyList<PortId> portsOnSide,
        PortId port,
        bool isOutput)
    {
        var index = 0;
        for (var i = 0; i < portsOnSide.Count; i++)
        {
            if (portsOnSide[i] == port)
            {
                index = i;
                break;
            }
        }

        var count = Math.Max(1, portsOnSide.Count);
        // Evenly space across node width; Y offset to bottom (output) or top (input).
        var fraction = count == 1 ? 0.0 : ((index + 1) / (double)(count + 1)) - 0.5;
        var xOff = fraction * node.Width;
        var yOff = isOutput ? -node.Height / 2.0 : node.Height / 2.0;
        return new RelativeFloatingPort(
            () => node.BoundaryCurve,
            () => node.Center,
            new Point(xOff, yOff));
    }

    /// <summary>Prefer axis-aligned line segments from rectilinear routing; keep port endpoints.</summary>
    private static IReadOnlyList<FlowGraphPoint> ExtractPolyline(
        ICurve curve,
        Point sourcePort,
        Point targetPort)
    {
        var points = new List<FlowGraphPoint>();
        void Add(Point p)
        {
            var fp = new FlowGraphPoint(p.X, p.Y);
            if (points.Count == 0
                || Math.Abs(points[^1].X - fp.X) > 1e-6
                || Math.Abs(points[^1].Y - fp.Y) > 1e-6)
            {
                points.Add(fp);
            }
        }

        Add(sourcePort);

        if (curve is Curve compound)
        {
            foreach (var seg in compound.Segments)
            {
                if (seg is LineSegment line)
                {
                    Add(line.Start);
                    Add(line.End);
                }
                else
                {
                    // Skip fillet curves; the following line segment continues the route.
                    Add(seg.End);
                }
            }
        }
        else if (curve is LineSegment single)
        {
            Add(single.Start);
            Add(single.End);
        }
        else
        {
            const int samples = 12;
            for (var i = 0; i <= samples; i++)
            {
                var par = curve.ParStart + (curve.ParEnd - curve.ParStart) * i / samples;
                Add(curve[par]);
            }
        }

        Add(targetPort);
        return points;
    }
}
