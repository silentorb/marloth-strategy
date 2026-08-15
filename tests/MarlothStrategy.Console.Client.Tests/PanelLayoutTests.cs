namespace MarlothStrategy.Console.Client.Tests;

public sealed class PanelLayoutTests
{
    [Fact]
    public void LeftInteriorWidthForTotal_DefaultWidth_IsHalfInterior()
    {
        Assert.Equal(120, PanelLayout.DefaultWidth);
        var left = PanelLayout.LeftInteriorWidthForTotal(PanelLayout.DefaultWidth);
        Assert.Equal((PanelLayout.DefaultWidth - 3) / 2, left);
        var right = PanelLayout.DefaultWidth - left - 3;
        Assert.True(Math.Abs(right - left) <= 1, $"Expected ~1:1 split, got left={left} right={right}");
    }

    [Fact]
    public void Compose_UsesDoubleOuterAndMixedSubpanelDividers()
    {
        var text = PanelLayout.Compose(
            headerLines: ["Title", "Tick 0"],
            leftSubpanels:
            [
                ["a:", "  x: 1"],
                ["b:", "  y: 2"],
            ],
            rightLines: ["graph"],
            totalWidth: 40,
            leftInteriorWidth: 18);

        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        Assert.Equal(40, lines[0].Length);
        Assert.StartsWith($"{BoxDrawing.DoubleTopLeft}", lines[0]);
        Assert.EndsWith($"{BoxDrawing.DoubleTopRight}", lines[0]);
        Assert.Contains("Title", normalized);
        Assert.Contains("Tick 0", normalized);
        Assert.Contains("a:", normalized);
        Assert.Contains("b:", normalized);
        Assert.Contains("graph", normalized);
        Assert.Contains($"{BoxDrawing.MixedTeeLeft}", normalized);
        Assert.Contains($"{BoxDrawing.MixedTeeTop}", normalized);
        Assert.Contains($"{BoxDrawing.MixedTeeBottom}", normalized);
        Assert.StartsWith($"{BoxDrawing.DoubleBottomLeft}", lines[^1]);
    }

    [Fact]
    public void Compose_PadsAndClipsLeftContentToInteriorWidth()
    {
        var text = PanelLayout.Compose(
            headerLines: ["H"],
            leftSubpanels: [["short", "this-line-is-definitely-too-long-for-the-column"]],
            rightLines: [""],
            totalWidth: 30,
            leftInteriorWidth: 10);

        var normalized = text.Replace("\r\n", "\n");
        Assert.Contains("short", normalized);
        Assert.Contains("this-line", normalized);
        Assert.DoesNotContain("definitely-too-long", normalized);
    }

    [Fact]
    public void ComposeStacked_UsesDoubleOuterAndFullWidthSubpanelDividers()
    {
        var text = PanelLayout.ComposeStacked(
            headerLines: ["Title", "Tick 0"],
            subpanels:
            [
                ["a:", "  x: 1"],
                ["b:", "  y: 2"],
            ],
            totalWidth: 40);

        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        Assert.Equal(40, lines[0].Length);
        Assert.StartsWith($"{BoxDrawing.DoubleTopLeft}", lines[0]);
        Assert.EndsWith($"{BoxDrawing.DoubleTopRight}", lines[0]);
        Assert.Contains("Title", normalized);
        Assert.Contains("Tick 0", normalized);
        Assert.Contains("a:", normalized);
        Assert.Contains("b:", normalized);
        Assert.Contains($"{BoxDrawing.MixedTeeLeft}", normalized);
        Assert.Contains($"{BoxDrawing.MixedTeeRight}", normalized);
        Assert.DoesNotContain($"{BoxDrawing.MixedTeeTop}", normalized);
        Assert.StartsWith($"{BoxDrawing.DoubleBottomLeft}", lines[^1]);
    }

    [Fact]
    public void ComposeStacked_RejectsEmptySubpanels()
    {
        Assert.Throws<ArgumentException>(() =>
            PanelLayout.ComposeStacked(
                headerLines: ["H"],
                subpanels: Array.Empty<IReadOnlyList<string>>(),
                totalWidth: 40));
    }
}
