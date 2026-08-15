namespace MarlothStrategy.Console.Client;

/// <summary>One stacked subpanel: optional centered title rule line plus body lines.</summary>
public readonly record struct PanelSubpanel(string? Title, IReadOnlyList<string> Lines)
{
    public PanelSubpanel(IReadOnlyList<string> lines)
        : this(null, lines)
    {
    }

    public bool HasTitle => !string.IsNullOrEmpty(Title);
}

/// <summary>Composes the top status panel and bottom split (stacked left subpanels | right panel).</summary>
public static class PanelLayout
{
    public const int DefaultWidth = 120;

    /// <summary>Spaces on each side of a centered title within the double-line rule.</summary>
    public const int TitleSidePadding = 2;

    /// <summary>Left column interior width for a left:right = 1:1 bottom split.</summary>
    public static int LeftInteriorWidthForTotal(int totalWidth)
    {
        if (totalWidth < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(totalWidth), totalWidth, "totalWidth must be at least 10.");
        }

        var left = Math.Max(1, (totalWidth - 3) / 2);
        var maxLeft = totalWidth - 5;
        return left > maxLeft ? maxLeft : left;
    }

    /// <summary>
    /// Builds a double-bordered frame: top panel spanning full width, then a bottom split into left
    /// stacked subpanels (double-line <c>╠</c>/<c>╣</c> dividers between panels, plus an extra
    /// double-line title rule below that border when the panel is titled) and a right panel.
    /// </summary>
    /// <param name="totalWidth">Total frame width including outer borders.</param>
    /// <param name="leftInteriorWidth">Interior width of the left column (between vertical borders / split).</param>
    public static string Compose(
        IReadOnlyList<string> headerLines,
        IReadOnlyList<PanelSubpanel> leftSubpanels,
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
        var leftBodyHeight = MeasureStackHeight(leftSubpanels);
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
            canvas[splitX, y] = BoxDrawing.DoubleVertical;
        }

        // Junctions on the header split and bottom edge
        canvas[splitX, splitY] = BoxDrawing.DoubleTeeTop;
        canvas[splitX, totalHeight - 1] = BoxDrawing.DoubleTeeBottom;

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
            var panel = leftSubpanels[panelIndex];
            if (panelIndex > 0)
            {
                // Divider across left column only: ╠══…══╣ (the split vertical continues past it)
                canvas.DrawHorizontalDivider(
                    0,
                    splitX,
                    leftY,
                    BoxDrawing.DoubleTeeLeft,
                    BoxDrawing.DoubleHorizontal,
                    BoxDrawing.DoubleTeeRight);
                leftY++;
            }

            if (panel.HasTitle)
            {
                // Extra line below the divider; ╣ keeps the split vertical continuous.
                DrawTitleRule(
                    canvas,
                    leftX: 0,
                    rightX: splitX,
                    y: leftY,
                    title: panel.Title!,
                    leftJunction: BoxDrawing.DoubleTeeLeft,
                    rightJunction: BoxDrawing.DoubleTeeRight);
                leftY++;
            }

            var panelHeight = Math.Max(1, panel.Lines.Count);
            for (var row = 0; row < panelHeight; row++)
            {
                var line = row < panel.Lines.Count ? panel.Lines[row] : string.Empty;
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

    /// <summary>
    /// Builds a double-bordered frame: top panel spanning full width, then full-width stacked
    /// subpanels (double-line <c>╠</c>/<c>╣</c> dividers between panels, plus an extra double-line
    /// title rule below that border when the panel is titled).
    /// </summary>
    public static string ComposeStacked(
        IReadOnlyList<string> headerLines,
        IReadOnlyList<PanelSubpanel> subpanels,
        int totalWidth)
    {
        ArgumentNullException.ThrowIfNull(headerLines);
        ArgumentNullException.ThrowIfNull(subpanels);

        if (totalWidth < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(totalWidth), totalWidth, "totalWidth must be at least 10.");
        }

        if (subpanels.Count == 0)
        {
            throw new ArgumentException("At least one subpanel is required.", nameof(subpanels));
        }

        var interiorWidth = totalWidth - 2;
        var headerBodyHeight = Math.Max(1, headerLines.Count);
        var bodyHeight = MeasureStackHeight(subpanels);

        var totalHeight = 1 + headerBodyHeight + 1 + bodyHeight + 1; // top, header, split, body, floor
        var canvas = new AsciiCanvas(totalWidth, totalHeight);

        canvas.DrawDoubleRect(0, 0, totalWidth, totalHeight);

        var splitY = 1 + headerBodyHeight;
        canvas.DrawHorizontalDivider(
            0,
            totalWidth - 1,
            splitY,
            BoxDrawing.DoubleTeeLeft,
            BoxDrawing.DoubleHorizontal,
            BoxDrawing.DoubleTeeRight);

        for (var i = 0; i < headerBodyHeight; i++)
        {
            var line = i < headerLines.Count ? headerLines[i] : string.Empty;
            WritePadded(canvas, 1, 1 + i, line, interiorWidth);
        }

        var bodyY = splitY + 1;
        for (var panelIndex = 0; panelIndex < subpanels.Count; panelIndex++)
        {
            var panel = subpanels[panelIndex];
            if (panelIndex > 0)
            {
                canvas.DrawHorizontalDivider(
                    0,
                    totalWidth - 1,
                    bodyY,
                    BoxDrawing.DoubleTeeLeft,
                    BoxDrawing.DoubleHorizontal,
                    BoxDrawing.DoubleTeeRight);
                bodyY++;
            }

            if (panel.HasTitle)
            {
                // Extra line below the divider, spanning the full interior.
                DrawTitleRule(
                    canvas,
                    leftX: 0,
                    rightX: totalWidth - 1,
                    y: bodyY,
                    title: panel.Title!,
                    leftJunction: BoxDrawing.DoubleTeeLeft,
                    rightJunction: BoxDrawing.DoubleTeeRight);
                bodyY++;
            }

            var panelHeight = Math.Max(1, panel.Lines.Count);
            for (var row = 0; row < panelHeight; row++)
            {
                var line = row < panel.Lines.Count ? panel.Lines[row] : string.Empty;
                WritePadded(canvas, 1, bodyY + row, line, interiorWidth);
            }

            bodyY += panelHeight;
        }

        return canvas.ToString();
    }

    private static int MeasureStackHeight(IReadOnlyList<PanelSubpanel> panels)
    {
        var height = 0;
        for (var i = 0; i < panels.Count; i++)
        {
            var panel = panels[i];
            if (i > 0)
            {
                height++; // divider above every panel after the first
            }

            if (panel.HasTitle)
            {
                height++; // title rule sits on its own line below that divider
            }

            height += Math.Max(1, panel.Lines.Count);
        }

        return height;
    }

    /// <summary>
    /// Draws a double-line title rule with the title centered and <see cref="TitleSidePadding"/>
    /// spaces on each side. Falls back to a plain double divider when the title cannot fit.
    /// </summary>
    private static void DrawTitleRule(
        AsciiCanvas canvas,
        int leftX,
        int rightX,
        int y,
        string title,
        char leftJunction,
        char rightJunction)
    {
        canvas.DrawHorizontalDivider(
            leftX,
            rightX,
            y,
            leftJunction,
            BoxDrawing.DoubleHorizontal,
            rightJunction);

        var interiorWidth = rightX - leftX - 1;
        var minLabelWidth = TitleSidePadding * 2 + 1; // "  X  "
        if (interiorWidth < minLabelWidth || string.IsNullOrEmpty(title))
        {
            return;
        }

        var maxTitleWidth = interiorWidth - TitleSidePadding * 2;
        var clipped = title.Length > maxTitleWidth ? title[..maxTitleWidth] : title;
        var label = new string(' ', TitleSidePadding) + clipped + new string(' ', TitleSidePadding);
        var startX = leftX + 1 + (interiorWidth - label.Length) / 2;
        canvas.WriteText(startX, y, label, label.Length);
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
