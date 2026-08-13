using System.Text;

namespace MarlothStrategy.Console.Client;

/// <summary>Mutable rectangular character buffer for ASCII panel composition.</summary>
public sealed class AsciiCanvas
{
    private readonly char[] _cells;

    public AsciiCanvas(int width, int height)
    {
        if (width < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be at least 1.");
        }

        if (height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be at least 1.");
        }

        Width = width;
        Height = height;
        _cells = new char[width * height];
        Array.Fill(_cells, ' ');
    }

    public int Width { get; }

    public int Height { get; }

    public char this[int x, int y]
    {
        get
        {
            EnsureInBounds(x, y);
            return _cells[y * Width + x];
        }
        set
        {
            EnsureInBounds(x, y);
            _cells[y * Width + x] = value;
        }
    }

    public void Fill(char ch)
    {
        Array.Fill(_cells, ch);
    }

    public void WriteText(int x, int y, string text, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), maxWidth, "maxWidth must be non-negative.");
        }

        if (maxWidth == 0 || y < 0 || y >= Height || x >= Width)
        {
            return;
        }

        var startX = Math.Max(0, x);
        var skip = startX - x;
        var available = Math.Min(maxWidth - skip, Width - startX);
        if (available <= 0 || skip >= text.Length)
        {
            return;
        }

        var count = Math.Min(available, text.Length - skip);
        for (var i = 0; i < count; i++)
        {
            _cells[y * Width + startX + i] = text[skip + i];
        }
    }

    public void DrawDoubleRect(int x, int y, int width, int height) =>
        DrawRect(
            x,
            y,
            width,
            height,
            BoxDrawing.DoubleHorizontal,
            BoxDrawing.DoubleVertical,
            BoxDrawing.DoubleTopLeft,
            BoxDrawing.DoubleTopRight,
            BoxDrawing.DoubleBottomLeft,
            BoxDrawing.DoubleBottomRight);

    public void DrawSingleRect(int x, int y, int width, int height) =>
        DrawRect(
            x,
            y,
            width,
            height,
            BoxDrawing.SingleHorizontal,
            BoxDrawing.SingleVertical,
            BoxDrawing.SingleTopLeft,
            BoxDrawing.SingleTopRight,
            BoxDrawing.SingleBottomLeft,
            BoxDrawing.SingleBottomRight);

    public void DrawRect(
        int x,
        int y,
        int width,
        int height,
        char horizontal,
        char vertical,
        char topLeft,
        char topRight,
        char bottomLeft,
        char bottomRight)
    {
        if (width < 2 || height < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Rectangle must be at least 2x2 (got {width}x{height}).");
        }

        var x2 = x + width - 1;
        var y2 = y + height - 1;
        this[x, y] = topLeft;
        this[x2, y] = topRight;
        this[x, y2] = bottomLeft;
        this[x2, y2] = bottomRight;

        for (var xi = x + 1; xi < x2; xi++)
        {
            this[xi, y] = horizontal;
            this[xi, y2] = horizontal;
        }

        for (var yi = y + 1; yi < y2; yi++)
        {
            this[x, yi] = vertical;
            this[x2, yi] = vertical;
        }
    }

    /// <summary>
    /// Draws a horizontal divider spanning inclusive <paramref name="x1"/>..<paramref name="x2"/> at <paramref name="y"/>,
    /// writing <paramref name="leftJunction"/> / <paramref name="rightJunction"/> at the ends and <paramref name="fill"/> between.
    /// </summary>
    public void DrawHorizontalDivider(int x1, int x2, int y, char leftJunction, char fill, char rightJunction)
    {
        if (x2 < x1)
        {
            throw new ArgumentOutOfRangeException(nameof(x2), "x2 must be >= x1.");
        }

        this[x1, y] = leftJunction;
        this[x2, y] = rightJunction;
        for (var x = x1 + 1; x < x2; x++)
        {
            this[x, y] = fill;
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder(Height * (Width + Environment.NewLine.Length));
        for (var y = 0; y < Height; y++)
        {
            sb.Append(_cells, y * Width, Width);
            if (y < Height - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private void EnsureInBounds(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException($"Cell ({x},{y}) outside {Width}x{Height} canvas.");
        }
    }
}
