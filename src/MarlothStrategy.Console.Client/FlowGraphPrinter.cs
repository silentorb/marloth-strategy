using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

/// <summary>
/// ASCII flow graph: places MSAGL-laid-out nodes and rasterizes MSAGL port-routed edge polylines.
/// </summary>
public static class FlowGraphPrinter
{
    private const int BoxHeight = 3;
    private const int Margin = 1;

    public static IReadOnlyList<string> FormatLines(GameState state, int maxWidth = 60)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (maxWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), maxWidth, "maxWidth must be at least 1.");
        }

        var layout = FlowGraphLayout.Compute(state);
        if (layout.Nodes.Count == 0)
        {
            return [];
        }

        var boxWidths = layout.Nodes.ToDictionary(
            n => n.Id,
            n =>
            {
                var labelWidth = $" {n.Id.Value} ".Length + 2;
                // Keep enough width to reflect distinct MSAGL port spacing after quantization.
                var fromPorts = layout.Edges.Where(e => e.From == n.Id).Select(e => e.FromPort).Distinct().Count();
                var toPorts = layout.Edges.Where(e => e.To == n.Id).Select(e => e.ToPort).Distinct().Count();
                var portCount = Math.Max(fromPorts, toPorts);
                var portWidth = portCount <= 1 ? labelWidth : (portCount + 1) * 2 + 1;
                return Math.Max(labelWidth, portWidth);
            });

        var allPoints = layout.Nodes.Select(n => n.Center)
            .Concat(layout.Edges.SelectMany(e => e.Points))
            .ToArray();
        var minX = allPoints.Min(p => p.X);
        var maxX = allPoints.Max(p => p.X);
        var minY = allPoints.Min(p => p.Y);
        var maxY = allPoints.Max(p => p.Y);
        var spanX = Math.Max(1e-6, maxX - minX);
        var spanY = Math.Max(1e-6, maxY - minY);

        var maxBoxW = boxWidths.Values.Max();
        var pad = Margin + 1;
        var scaleX = spanX < 1e-3
            ? 1.0
            : Math.Max(0.01, (maxWidth - maxBoxW - 2 * pad) / spanX);
        // Compress Y: MSAGL layered graphs are tall for a character grid.
        var targetSpanY = Math.Max(BoxHeight + 2, (layout.Nodes.Count - 1) * (BoxHeight + 2));
        var scaleY = targetSpanY / spanY;

        (int X, int Y) Map(FlowGraphPoint p) => (
            (int)Math.Round((p.X - minX) * scaleX) + pad + maxBoxW / 2,
            (int)Math.Round((maxY - p.Y) * scaleY) + pad); // Y-up → Y-down

        var placed = new Dictionary<NodeId, (int X, int Y, int W, int H)>();
        foreach (var node in layout.Nodes)
        {
            var w = boxWidths[node.Id];
            var (cx, cy) = Map(node.Center);
            placed[node.Id] = (cx - w / 2, cy - BoxHeight / 2, w, BoxHeight);
        }

        // Normalize so content starts near the origin.
        var originX = placed.Values.Min(p => p.X);
        var originY = Math.Min(
            placed.Values.Min(p => p.Y),
            layout.Edges.SelectMany(e => e.Points).Select(p => Map(p).Y).DefaultIfEmpty(0).Min());
        if (originX != 0 || originY != 0)
        {
            placed = placed.ToDictionary(
                kv => kv.Key,
                kv => (kv.Value.X - originX + pad, kv.Value.Y - originY + pad, kv.Value.W, kv.Value.H));
        }

        (int X, int Y) MapShifted(FlowGraphPoint p)
        {
            var (x, y) = Map(p);
            return (x - originX + pad, y - originY + pad);
        }

        // Fit width by uniform X shrink if needed (keep MSAGL relative geometry).
        var rightMost = Math.Max(
            placed.Values.Max(p => p.X + p.W),
            layout.Edges.SelectMany(e => e.Points).Select(p => MapShifted(p).X).DefaultIfEmpty(0).Max() + 1);
        if (rightMost + pad > maxWidth)
        {
            var shrink = Math.Max(0.01, (maxWidth - 2 * pad) / (double)Math.Max(1, rightMost - pad));
            placed = placed.ToDictionary(
                kv => kv.Key,
                kv => (
                    pad + (int)Math.Round((kv.Value.X - pad) * shrink),
                    kv.Value.Y,
                    kv.Value.W,
                    kv.Value.H));
            placed = placed.ToDictionary(
                kv => kv.Key,
                kv => (
                    Math.Clamp(kv.Value.X, pad, Math.Max(pad, maxWidth - pad - kv.Value.W)),
                    kv.Value.Y,
                    kv.Value.W,
                    kv.Value.H));

            (int X, int Y) MapFinal(FlowGraphPoint p)
            {
                var (x, y) = MapShifted(p);
                return (pad + (int)Math.Round((x - pad) * shrink), y);
            }

            return Render(layout, placed, MapFinal, maxWidth, pad);
        }

        return Render(layout, placed, MapShifted, maxWidth, pad);
    }

    private static IReadOnlyList<string> Render(
        FlowGraphLayoutResult layout,
        Dictionary<NodeId, (int X, int Y, int W, int H)> placed,
        Func<FlowGraphPoint, (int X, int Y)> mapPoint,
        int maxWidth,
        int pad)
    {
        var edgePolylines = layout.Edges
            .Select(e => (Edge: e, Pts: e.Points.Select(mapPoint).ToList()))
            .ToArray();

        var contentRight = placed.Values.Max(p => p.X + p.W);
        var contentBottom = placed.Values.Max(p => p.Y + p.H);
        if (edgePolylines.Length > 0)
        {
            contentRight = Math.Max(contentRight, edgePolylines.Max(e => e.Pts.Max(p => p.X)) + 1);
            contentBottom = Math.Max(contentBottom, edgePolylines.Max(e => e.Pts.Max(p => p.Y)) + 1);
        }

        var width = Math.Clamp(Math.Max(contentRight + pad, 1), 1, maxWidth);
        var height = Math.Max(1, contentBottom + pad);
        var canvas = new AsciiCanvas(width, height);

        var wireMask = new Dictionary<(int X, int Y), WireDir>();
        var arrows = new Dictionary<(int X, int Y), WireDir>();

        foreach (var (edge, pts) in edgePolylines)
        {
            if (!placed.TryGetValue(edge.To, out var toBox) || pts.Count < 2)
            {
                continue;
            }

            StampPolyline(wireMask, pts);
            PlaceArrow(arrows, pts, toBox, width, height);
        }

        PaintWires(canvas, wireMask, arrows);

        foreach (var node in layout.Nodes)
        {
            var (x, y, w, _) = placed[node.Id];
            DrawNodeBox(canvas, x, y, node.Id.Value, node.HasSelfLoop, w);
        }

        return canvas.ToString().Replace("\r\n", "\n").Split('\n');
    }

    private static void StampPolyline(
        IDictionary<(int X, int Y), WireDir> mask,
        IReadOnlyList<(int X, int Y)> pts)
    {
        for (var i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            if (a.X == b.X || a.Y == b.Y)
            {
                FlowGraphWires.StampSegment(mask, a.X, a.Y, b.X, b.Y);
                continue;
            }

            // Diagonal sample → orthogonal elbow (vertical then horizontal).
            var mid = (a.X, b.Y);
            FlowGraphWires.StampSegment(mask, a.X, a.Y, mid.X, mid.Y);
            FlowGraphWires.StampSegment(mask, mid.X, mid.Y, b.X, b.Y);
        }
    }

    private static void PlaceArrow(
        IDictionary<(int X, int Y), WireDir> arrows,
        IReadOnlyList<(int X, int Y)> pts,
        (int X, int Y, int W, int H) toBox,
        int canvasWidth,
        int canvasHeight)
    {
        var tip = pts[^1];
        var intoBox = ArrowIntoBox(tip, toBox);
        if (intoBox == WireDir.None && pts.Count >= 2)
        {
            intoBox = FlowGraphWires.Toward(pts[^2].X, pts[^2].Y, tip.X, tip.Y);
        }

        if (intoBox == WireDir.None)
        {
            return;
        }

        foreach (var candidate in new[] { tip, StepAlong(tip, Opposite(intoBox)) })
        {
            var (ax, ay) = candidate;
            if (ax < 0 || ay < 0 || ax >= canvasWidth || ay >= canvasHeight)
            {
                continue;
            }

            if (InsideBox(toBox, ax, ay))
            {
                continue;
            }

            if (arrows.TryGetValue((ax, ay), out var existing) && existing != intoBox)
            {
                continue;
            }

            arrows[(ax, ay)] = intoBox;
            return;
        }
    }

    private static WireDir ArrowIntoBox((int X, int Y) tip, (int X, int Y, int W, int H) box)
    {
        if (tip.Y < box.Y)
        {
            return WireDir.S;
        }

        if (tip.Y >= box.Y + box.H)
        {
            return WireDir.N;
        }

        if (tip.X < box.X)
        {
            return WireDir.E;
        }

        if (tip.X >= box.X + box.W)
        {
            return WireDir.W;
        }

        return WireDir.None;
    }

    private static WireDir Opposite(WireDir dir) => dir switch
    {
        WireDir.N => WireDir.S,
        WireDir.S => WireDir.N,
        WireDir.E => WireDir.W,
        WireDir.W => WireDir.E,
        _ => WireDir.None,
    };

    private static (int X, int Y) StepAlong((int X, int Y) from, WireDir dir) => dir switch
    {
        WireDir.N => (from.X, from.Y - 1),
        WireDir.S => (from.X, from.Y + 1),
        WireDir.E => (from.X + 1, from.Y),
        WireDir.W => (from.X - 1, from.Y),
        _ => from,
    };

    private static bool InsideBox((int X, int Y, int W, int H) box, int x, int y) =>
        x >= box.X && x < box.X + box.W && y >= box.Y && y < box.Y + box.H;

    private static void DrawNodeBox(AsciiCanvas canvas, int x, int y, string label, bool hasSelfLoop, int boxWidth)
    {
        var inner = $" {label} ";
        var contentWidth = boxWidth - 2;
        if (inner.Length > contentWidth)
        {
            inner = inner[..contentWidth];
        }
        else if (inner.Length < contentWidth)
        {
            inner = inner.PadRight(contentWidth);
        }

        if (x >= 0 && y >= 0 && x + boxWidth <= canvas.Width && y + BoxHeight <= canvas.Height)
        {
            canvas.DrawSingleRect(x, y, boxWidth, BoxHeight);
            canvas.WriteText(x + 1, y + 1, inner, contentWidth);
        }

        if (hasSelfLoop && x + boxWidth + 3 <= canvas.Width && y + 1 < canvas.Height)
        {
            canvas.WriteText(x + boxWidth, y + 1, "──┐", 3);
            if (y + 2 < canvas.Height && x + boxWidth + 2 < canvas.Width)
            {
                canvas[x + boxWidth + 2, y + 2] = BoxDrawing.SingleVertical;
            }
        }
    }

    private static void PaintWires(
        AsciiCanvas canvas,
        IReadOnlyDictionary<(int X, int Y), WireDir> mask,
        IReadOnlyDictionary<(int X, int Y), WireDir> arrows)
    {
        foreach (var ((x, y), dirs) in mask)
        {
            if (x >= 0 && y >= 0 && x < canvas.Width && y < canvas.Height)
            {
                canvas[x, y] = FlowGraphWires.GlyphFor(dirs);
            }
        }

        foreach (var ((x, y), inbound) in arrows)
        {
            if (x >= 0 && y >= 0 && x < canvas.Width && y < canvas.Height)
            {
                canvas[x, y] = FlowGraphWires.ArrowFor(inbound);
            }
        }
    }
}
