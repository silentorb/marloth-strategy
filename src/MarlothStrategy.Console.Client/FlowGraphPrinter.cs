using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

/// <summary>ASCII flow graph: MSAGL layout quantized onto a character grid with boxed nodes and connectors.</summary>
public static class FlowGraphPrinter
{
    private const int BoxHeight = 3;
    private const int MinCenterGapY = BoxHeight + 2;
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
            n => $" {n.Id.Value} ".Length + 2);

        var minX = layout.Nodes.Min(n => n.Center.X);
        var maxX = layout.Nodes.Max(n => n.Center.X);
        var minY = layout.Nodes.Min(n => n.Center.Y);
        var maxY = layout.Nodes.Max(n => n.Center.Y);
        var spanX = Math.Max(1e-6, maxX - minX);
        var spanY = Math.Max(1e-6, maxY - minY);

        var maxBoxW = boxWidths.Values.Max();
        var pad = Margin + 1;

        // Fit X to the panel; compress Y so layers stay compact (MSAGL aspect is too tall for ASCII).
        var scaleX = maxX - minX < 1e-3
            ? 1.0
            : Math.Max(0.01, (maxWidth - maxBoxW - 2 * pad) / spanX);
        var targetSpanY = Math.Max(MinCenterGapY, (layout.Nodes.Count - 1) * MinCenterGapY);
        var scaleY = targetSpanY / spanY;

        var placed = new Dictionary<NodeId, (int X, int Y, int W, int H)>();
        foreach (var node in layout.Nodes)
        {
            var w = boxWidths[node.Id];
            var cx = (node.Center.X - minX) * scaleX;
            var cy = (maxY - node.Center.Y) * scaleY; // Y-up → Y-down
            placed[node.Id] = (
                (int)Math.Round(cx - w / 2.0),
                (int)Math.Round(cy - BoxHeight / 2.0),
                w,
                BoxHeight);
        }

        NudgeOverlaps(placed);

        var originX = placed.Values.Min(p => p.X);
        var originY = placed.Values.Min(p => p.Y);
        placed = placed.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.X - originX + pad, kv.Value.Y - originY + pad, kv.Value.W, kv.Value.H));

        // Shrink horizontally if nudge/placement still exceeds maxWidth.
        var rightMost = placed.Values.Max(p => p.X + p.W);
        if (rightMost + pad > maxWidth)
        {
            var available = Math.Max(1, maxWidth - 2 * pad);
            var span = Math.Max(1, rightMost - pad);
            var shrink = available / (double)span;
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
        }

        var contentRight = placed.Values.Max(p => p.X + p.W);
        var contentBottom = placed.Values.Max(p => p.Y + p.H);
        var width = Math.Clamp(Math.Max(contentRight + pad, 1), 1, maxWidth);
        var height = Math.Max(1, contentBottom + pad);
        var canvas = new AsciiCanvas(width, height);

        var wireMask = new Dictionary<(int X, int Y), WireDir>();
        var arrows = new Dictionary<(int X, int Y), WireDir>();

        foreach (var edge in layout.Edges)
        {
            if (!placed.TryGetValue(edge.From, out var fromBox) || !placed.TryGetValue(edge.To, out var toBox))
            {
                continue;
            }

            StampOrthogonalEdge(
                wireMask,
                arrows,
                fromBox,
                toBox,
                edge.From.Value,
                edge.To.Value,
                width,
                height);
        }

        PaintWires(canvas, wireMask, arrows);

        foreach (var node in layout.Nodes)
        {
            var (x, y, w, _) = placed[node.Id];
            DrawNodeBox(canvas, x, y, node.Id.Value, node.HasSelfLoop, w);
        }

        return canvas.ToString().Replace("\r\n", "\n").Split('\n');
    }

    private static void NudgeOverlaps(Dictionary<NodeId, (int X, int Y, int W, int H)> placed)
    {
        var ids = placed.Keys.OrderBy(id => placed[id].Y).ThenBy(id => placed[id].X).ToArray();
        const int gap = 1;
        for (var pass = 0; pass < ids.Length; pass++)
        {
            for (var i = 0; i < ids.Length; i++)
            {
                for (var j = i + 1; j < ids.Length; j++)
                {
                    var a = placed[ids[i]];
                    var b = placed[ids[j]];
                    if (!Overlaps(a, b, gap))
                    {
                        continue;
                    }

                    var newY = a.Y + a.H + gap;
                    if (b.Y < newY)
                    {
                        placed[ids[j]] = (b.X, newY, b.W, b.H);
                    }
                }
            }
        }
    }

    private static bool Overlaps(
        (int X, int Y, int W, int H) a,
        (int X, int Y, int W, int H) b,
        int gap) =>
        a.X < b.X + b.W + gap
        && a.X + a.W + gap > b.X
        && a.Y < b.Y + b.H + gap
        && a.Y + a.H + gap > b.Y;

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
        else
        {
            TryWriteBoxClipped(canvas, x, y, inner, boxWidth);
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

    private static void TryWriteBoxClipped(AsciiCanvas canvas, int x, int y, string inner, int boxWidth)
    {
        for (var row = 0; row < BoxHeight; row++)
        {
            var cy = y + row;
            if (cy < 0 || cy >= canvas.Height)
            {
                continue;
            }

            for (var col = 0; col < boxWidth; col++)
            {
                var cx = x + col;
                if (cx < 0 || cx >= canvas.Width)
                {
                    continue;
                }

                char ch;
                if (row == 0)
                {
                    ch = col == 0
                        ? BoxDrawing.SingleTopLeft
                        : col == boxWidth - 1
                            ? BoxDrawing.SingleTopRight
                            : BoxDrawing.SingleHorizontal;
                }
                else if (row == 2)
                {
                    ch = col == 0
                        ? BoxDrawing.SingleBottomLeft
                        : col == boxWidth - 1
                            ? BoxDrawing.SingleBottomRight
                            : BoxDrawing.SingleHorizontal;
                }
                else if (col == 0 || col == boxWidth - 1)
                {
                    ch = BoxDrawing.SingleVertical;
                }
                else
                {
                    var i = col - 1;
                    ch = i < inner.Length ? inner[i] : ' ';
                }

                canvas[cx, cy] = ch;
            }
        }
    }

    private static void StampOrthogonalEdge(
        IDictionary<(int X, int Y), WireDir> mask,
        IDictionary<(int X, int Y), WireDir> arrows,
        (int X, int Y, int W, int H) fromBox,
        (int X, int Y, int W, int H) toBox,
        string fromId,
        string toId,
        int canvasWidth,
        int canvasHeight)
    {
        var fromCenter = (fromBox.X + fromBox.W / 2, fromBox.Y + fromBox.H / 2);
        var toCenter = (toBox.X + toBox.W / 2, toBox.Y + toBox.H / 2);

        // Attach outside the boxes so arrows are not covered when nodes are painted.
        var start = ClampPoint(AttachOutside(fromBox, toCenter), canvasWidth, canvasHeight);
        var end = ClampPoint(AttachOutside(toBox, fromCenter), canvasWidth, canvasHeight);

        var waypoints = OrthogonalWaypoints(start, end, fromId, toId);
        for (var i = 0; i < waypoints.Count - 1; i++)
        {
            FlowGraphWires.StampSegment(
                mask,
                waypoints[i].X,
                waypoints[i].Y,
                waypoints[i + 1].X,
                waypoints[i + 1].Y);
        }

        PlaceArrow(arrows, waypoints, toBox, canvasWidth, canvasHeight);
    }

    private static void PlaceArrow(
        IDictionary<(int X, int Y), WireDir> arrows,
        IReadOnlyList<(int X, int Y)> waypoints,
        (int X, int Y, int W, int H) toBox,
        int canvasWidth,
        int canvasHeight)
    {
        if (waypoints.Count < 2)
        {
            return;
        }

        var tip = waypoints[^1];
        var prev = waypoints[^2];
        var inbound = FlowGraphWires.Toward(prev.X, prev.Y, tip.X, tip.Y);
        if (inbound == WireDir.None)
        {
            return;
        }

        // Destination attach point first (outside the box). On conflict, step one cell back.
        foreach (var candidate in new[] { tip, StepAlong(tip, Opposite(inbound)) })
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

            if (arrows.TryGetValue((ax, ay), out var existing) && existing != inbound)
            {
                continue;
            }

            arrows[(ax, ay)] = inbound;
            return;
        }
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

    private static (int X, int Y) AttachOutside(
        (int X, int Y, int W, int H) box,
        (int X, int Y) toward)
    {
        var cx = box.X + box.W / 2;
        var cy = box.Y + box.H / 2;

        // Prefer vertical ports when the other node is clearly above/below (layered flow).
        if (toward.Y < box.Y)
        {
            return (cx, box.Y - 1);
        }

        if (toward.Y >= box.Y + box.H)
        {
            return (cx, box.Y + box.H);
        }

        if (toward.X < box.X)
        {
            return (box.X - 1, cy);
        }

        if (toward.X >= box.X + box.W)
        {
            return (box.X + box.W, cy);
        }

        var dx = toward.X - cx;
        var dy = toward.Y - cy;
        if (Math.Abs(dy) >= Math.Abs(dx))
        {
            return dy < 0 ? (cx, box.Y - 1) : (cx, box.Y + box.H);
        }

        return dx < 0 ? (box.X - 1, cy) : (box.X + box.W, cy);
    }

    private static bool InsideBox((int X, int Y, int W, int H) box, int x, int y) =>
        x >= box.X && x < box.X + box.W && y >= box.Y && y < box.Y + box.H;

    private static List<(int X, int Y)> OrthogonalWaypoints(
        (int X, int Y) start,
        (int X, int Y) end,
        string fromId,
        string toId)
    {
        if (start.X == end.X || start.Y == end.Y)
        {
            return [start, end];
        }

        var midY = (start.Y + end.Y) / 2;
        // Separate reverse edges onto adjacent bend rows when there is room.
        if (Math.Abs(end.Y - start.Y) > 2)
        {
            var bias = string.CompareOrdinal(fromId, toId) < 0 ? -1 : 1;
            midY += bias;
        }

        return [start, (start.X, midY), (end.X, midY), end];
    }

    private static void PaintWires(
        AsciiCanvas canvas,
        IReadOnlyDictionary<(int X, int Y), WireDir> mask,
        IReadOnlyDictionary<(int X, int Y), WireDir> arrows)
    {
        foreach (var ((x, y), dirs) in mask)
        {
            if (!InBounds(canvas, x, y))
            {
                continue;
            }

            canvas[x, y] = FlowGraphWires.GlyphFor(dirs);
        }

        foreach (var ((x, y), inbound) in arrows)
        {
            if (!InBounds(canvas, x, y))
            {
                continue;
            }

            canvas[x, y] = FlowGraphWires.ArrowFor(inbound);
        }
    }

    private static (int X, int Y) ClampPoint((int X, int Y) p, int width, int height) =>
        (Math.Clamp(p.X, 0, Math.Max(0, width - 1)), Math.Clamp(p.Y, 0, Math.Max(0, height - 1)));

    private static bool InBounds(AsciiCanvas canvas, int x, int y) =>
        x >= 0 && y >= 0 && x < canvas.Width && y < canvas.Height;
}
