namespace MarlothStrategy.Console.Client;

/// <summary>Direction bits for orthogonal ASCII wire cells (N/E/S/W connections).</summary>
[Flags]
public enum WireDir : byte
{
    None = 0,
    N = 1,
    E = 2,
    S = 4,
    W = 8,
}

/// <summary>Resolves orthogonal connector direction masks into single-line box-drawing glyphs.</summary>
public static class FlowGraphWires
{
    public static WireDir Toward(int fromX, int fromY, int toX, int toY)
    {
        if (toX == fromX && toY < fromY)
        {
            return WireDir.N;
        }

        if (toX == fromX && toY > fromY)
        {
            return WireDir.S;
        }

        if (toY == fromY && toX > fromX)
        {
            return WireDir.E;
        }

        if (toY == fromY && toX < fromX)
        {
            return WireDir.W;
        }

        return WireDir.None;
    }

    public static void StampSegment(
        IDictionary<(int X, int Y), WireDir> mask,
        int x0,
        int y0,
        int x1,
        int y1)
    {
        if (x0 != x1 && y0 != y1)
        {
            throw new ArgumentException("Wire segments must be axis-aligned.");
        }

        if (x0 == x1 && y0 == y1)
        {
            return;
        }

        var cells = CellsOnSegment(x0, y0, x1, y1);
        for (var i = 0; i < cells.Count - 1; i++)
        {
            var (ax, ay) = cells[i];
            var (bx, by) = cells[i + 1];
            Add(mask, ax, ay, Toward(ax, ay, bx, by));
            Add(mask, bx, by, Toward(bx, by, ax, ay));
        }
    }

    public static char GlyphFor(WireDir dirs) => dirs switch
    {
        WireDir.None => ' ',
        WireDir.N or WireDir.S or (WireDir.N | WireDir.S) => BoxDrawing.SingleVertical,
        WireDir.E or WireDir.W or (WireDir.E | WireDir.W) => BoxDrawing.SingleHorizontal,
        WireDir.N | WireDir.E => BoxDrawing.SingleBottomLeft,   // └
        WireDir.N | WireDir.W => BoxDrawing.SingleBottomRight,  // ┘
        WireDir.S | WireDir.E => BoxDrawing.SingleTopLeft,      // ┌
        WireDir.S | WireDir.W => BoxDrawing.SingleTopRight,     // ┐
        WireDir.N | WireDir.S | WireDir.E => BoxDrawing.SingleTeeLeft,    // ├
        WireDir.N | WireDir.S | WireDir.W => BoxDrawing.SingleTeeRight,   // ┤
        WireDir.E | WireDir.W | WireDir.N => BoxDrawing.SingleTeeBottom,  // ┴
        WireDir.E | WireDir.W | WireDir.S => BoxDrawing.SingleTeeTop,     // ┬
        WireDir.N | WireDir.E | WireDir.S | WireDir.W => BoxDrawing.SingleCross, // ┼
        _ => BoxDrawing.SingleCross,
    };

    public static char ArrowFor(WireDir inbound) => inbound switch
    {
        WireDir.N => '▲', // arriving from south, pointing up
        WireDir.S => '▼',
        WireDir.E => '►',
        WireDir.W => '◄',
        _ => '▼',
    };

    private static void Add(IDictionary<(int X, int Y), WireDir> mask, int x, int y, WireDir dir)
    {
        if (dir == WireDir.None)
        {
            return;
        }

        var key = (x, y);
        mask[key] = mask.TryGetValue(key, out var existing) ? existing | dir : dir;
    }

    public static List<(int X, int Y)> CellsOnSegment(int x0, int y0, int x1, int y1)
    {
        var cells = new List<(int X, int Y)>();
        if (y0 == y1)
        {
            var step = x1 >= x0 ? 1 : -1;
            for (var x = x0; ; x += step)
            {
                cells.Add((x, y0));
                if (x == x1)
                {
                    break;
                }
            }
        }
        else
        {
            var step = y1 >= y0 ? 1 : -1;
            for (var y = y0; ; y += step)
            {
                cells.Add((x0, y));
                if (y == y1)
                {
                    break;
                }
            }
        }

        return cells;
    }
}
