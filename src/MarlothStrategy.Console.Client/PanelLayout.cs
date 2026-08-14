namespace MarlothStrategy.Console.Client;

/// <summary>Composes the top status panel and bottom split (stacked left subpanels | right panel).</summary>
public static class PanelLayout
{
    public const int DefaultWidth = 120;

    /// <summary>Left column interior width for a left:right = 1:2 bottom split.</summary>
    public static int LeftInteriorWidthForTotal(int totalWidth)
    {
        if (totalWidth < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(totalWidth), totalWidth, "totalWidth must be at least 10.");
        }

        var left = Math.Max(1, (totalWidth - 3) / 3);
        var maxLeft = totalWidth - 5;
        return left > maxLeft ? maxLeft : left;
    }

    /// <summary>
    /// Builds a double-bordered frame: top panel spanning full width, then a bottom split into left
    /// stacked subpanels (shared outer double border, single-line <c>╟</c>/<c>╢</c> dividers) and a right panel.
    /// </summary>
    /// <param name="totalWidth">Total frame width including outer borders.</param>
    /// <param name="leftInteriorWidth">Interior width of the left column (between vertical borders / split).</param>
    public static string Compose(
        IReadOnlyList<string> headerLines,
        IReadOnlyList<IReadOnlyList<string>> leftSubpanels,
        IReadOnlyList<string> rightLines,
        int totalWidth,
        int leftInteriorWidth)
    {
        ArgumentNullException.ThrowIfNull(headerLines);
        ArgumentNullException.ThrowIfNull(leftSubpanels);
        ArgumentNullException.ThrowIfNull(rightLines);

        if (totalWidth < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(totalWidth), totalWidth, "totalWidth must be at least 10.");
        }

        if (leftSubpanels.Count == 0)
        {
            throw new ArgumentException("At least one left subpanel is required.", nameof(leftSubpanels));
        }

        // Layout: ║ + leftInterior + │ + rightInterior + ║
        // totalWidth = 1 + leftInterior + 1 + rightInterior + 1
        var minLeft = 1;
        var maxLeft = totalWidth - 5; // leave room for borders + at least 1 right interior
        if (leftInteriorWidth < minLeft || leftInteriorWidth > maxLeft)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leftInteriorWidth),
                leftInteriorWidth,
                $"leftInteriorWidth must be in [{minLeft}, {maxLeft}] for totalWidth {totalWidth}.");
        }

        var rightInteriorWidth = totalWidth - leftInteriorWidth - 3;
        var headerInteriorWidth = totalWidth - 2;

        var headerBodyHeight = Math.Max(1, headerLines.Count);
        var leftBodyHeights = leftSubpanels.Select(p => Math.Max(1, p.Count)).ToArray();
        var leftBodyHeight = leftBodyHeights.Sum() + (leftSubpanels.Count - 1); // content + dividers
        var rightBodyHeight = Math.Max(1, rightLines.Count);
        var bottomBodyHeight = Math.Max(leftBodyHeight, rightBodyHeight);

        var totalHeight = 1 + headerBodyHeight + 1 + bottomBodyHeight + 1; // top, header, split, bottom, floor
        var canvas = new AsciiCanvas(totalWidth, totalHeight);

        // Outer double rectangle
        canvas.DrawDoubleRect(0, 0, totalWidth, totalHeight);

        // Header / bottom split (double horizontal with tees)
        var splitY = 1 + headerBodyHeight;
        canvas.DrawHorizontalDivider(
            0,
            totalWidth - 1,
            splitY,
            BoxDrawing.DoubleTeeLeft,
            BoxDrawing.DoubleHorizontal,
            BoxDrawing.DoubleTeeRight);

        // Vertical split in bottom region
        var splitX = 1 + leftInteriorWidth;
        for (var y = splitY + 1; y < totalHeight - 1; y++)
        {
            canvas[splitX, y] = BoxDrawing.SingleVertical;
        }

        // Junctions on the header split and bottom edge
        canvas[splitX, splitY] = BoxDrawing.MixedTeeTop;
        canvas[splitX, totalHeight - 1] = BoxDrawing.MixedTeeBottom;

        // Header content
        for (var i = 0; i < headerBodyHeight; i++)
        {
            var line = i < headerLines.Count ? headerLines[i] : string.Empty;
            WritePadded(canvas, 1, 1 + i, line, headerInteriorWidth);
        }

        // Left subpanels
        var leftY = splitY + 1;
        for (var panelIndex = 0; panelIndex < leftSubpanels.Count; panelIndex++)
        {
            if (panelIndex > 0)
            {
                // Divider across left column only: ╟──…──┤ (mixed left, single right into split)
                canvas.DrawHorizontalDivider(
                    0,
                    splitX,
                    leftY,
                    BoxDrawing.MixedTeeLeft,
                    BoxDrawing.SingleHorizontal,
                    BoxDrawing.SingleTeeRight);
                leftY++;
            }

            var panel = leftSubpanels[panelIndex];
            var panelHeight = leftBodyHeights[panelIndex];
            for (var row = 0; row < panelHeight; row++)
            {
                var line = row < panel.Count ? panel[row] : string.Empty;
                WritePadded(canvas, 1, leftY + row, line, leftInteriorWidth);
            }

            leftY += panelHeight;
        }

        // Right panel content (top-aligned)
        for (var i = 0; i < rightLines.Count && i < bottomBodyHeight; i++)
        {
            WritePadded(canvas, splitX + 1, splitY + 1 + i, rightLines[i], rightInteriorWidth);
        }

        return canvas.ToString();
    }

    private static void WritePadded(AsciiCanvas canvas, int x, int y, string text, int width)
    {
        if (width <= 0)
        {
            return;
        }

        // One-cell left margin inside the panel so text is not flush against the border.
        if (width == 1)
        {
            canvas.WriteText(x, y, text.Length > 0 ? text[..1] : " ", 1);
            return;
        }

        var contentWidth = width - 1;
        var clipped = text.Length > contentWidth ? text[..contentWidth] : text;
        canvas.WriteText(x, y, (" " + clipped).PadRight(width), width);
    }
}
