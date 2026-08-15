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
    public void Compose_UsesDoubleOuterAndDoubleSubpanelDividers()
    {
        var text = PanelLayout.Compose(
            headerLines: ["Title", "Tick 0"],
            leftSubpanels:
            [
                new PanelSubpanel(["a:", "  x: 1"]),
                new PanelSubpanel(["b:", "  y: 2"]),
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
        var secondPanelIndex = Array.FindIndex(lines, l => l.Contains("b:", StringComparison.Ordinal));
        Assert.True(secondPanelIndex > 0, "Missing second panel body.");
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", lines[secondPanelIndex - 1]);
        Assert.Contains($"{BoxDrawing.DoubleHorizontal}", lines[secondPanelIndex - 1]);
        Assert.Contains($"{BoxDrawing.DoubleTeeRight}", lines[secondPanelIndex - 1]);
        Assert.Contains($"{BoxDrawing.DoubleTeeTop}", normalized);
        Assert.Contains($"{BoxDrawing.DoubleTeeBottom}", normalized);
        Assert.StartsWith($"{BoxDrawing.DoubleBottomLeft}", lines[^1]);
    }

    [Fact]
    public void Compose_TitledSubpanels_UseCenteredDoubleTitleRules()
    {
        var text = PanelLayout.Compose(
            headerLines: ["H"],
            leftSubpanels:
            [
                new PanelSubpanel("enchant", ["  volume: 1"]),
                new PanelSubpanel("sell", ["  money: 0"]),
            ],
            rightLines: ["g"],
            totalWidth: 40,
            leftInteriorWidth: 18);

        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        var enchantRule = lines.Single(l => l.Contains("  enchant  "));
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", enchantRule);
        Assert.Contains($"{BoxDrawing.DoubleHorizontal}", enchantRule);
        // ╡ keeps the single split vertical continuous instead of stubbing into the right panel.
        Assert.Contains($"{BoxDrawing.DoubleTeeRight}", enchantRule);
        Assert.DoesNotContain($"{BoxDrawing.DoubleTeeTop}", enchantRule);
        Assert.DoesNotContain("enchant:", normalized);

        var sellRule = lines.Single(l => l.Contains("  sell  "));
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", sellRule);
        Assert.Contains($"{BoxDrawing.DoubleTeeRight}", sellRule);

        Assert.Contains("volume: 1", normalized);
        Assert.Contains("money: 0", normalized);
    }

    [Fact]
    public void Compose_TitledSubpanel_AddsTitleLineBelowDividerWithoutReplacingIt()
    {
        var text = PanelLayout.Compose(
            headerLines: ["H"],
            leftSubpanels:
            [
                new PanelSubpanel("first", ["  a"]),
                new PanelSubpanel("second", ["  b"]),
            ],
            rightLines: [""],
            totalWidth: 40,
            leftInteriorWidth: 18);

        var lines = text.Replace("\r\n", "\n").Split('\n');

        // top, header, header-split, title(first), body, divider, title(second), body, floor
        var headerSplit = lines[2];
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", headerSplit);
        Assert.Contains($"{BoxDrawing.DoubleTeeTop}", headerSplit);
        Assert.Contains("  first  ", lines[3]);

        var secondTitleIndex = Array.FindIndex(lines, l => l.Contains("  second  "));
        Assert.True(secondTitleIndex > 0, "Missing second title rule.");

        // The double-line divider is retained on its own line directly above the title rule.
        var divider = lines[secondTitleIndex - 1];
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", divider);
        Assert.Contains($"{BoxDrawing.DoubleHorizontal}", divider);
        Assert.Contains($"{BoxDrawing.DoubleTeeRight}", divider);
        Assert.DoesNotContain("second", divider);
    }

    [Fact]
    public void Compose_UntitledAfterTitled_UsesDoubleDivider()
    {
        var text = PanelLayout.Compose(
            headerLines: ["H"],
            leftSubpanels:
            [
                new PanelSubpanel("named", ["  a"]),
                new PanelSubpanel(["  b"]),
            ],
            rightLines: [""],
            totalWidth: 40,
            leftInteriorWidth: 18);

        var lines = text.Replace("\r\n", "\n").Split('\n');
        Assert.Contains(lines, line => line.Contains("  named  ", StringComparison.Ordinal));
        var bodyIndex = Array.FindIndex(lines, l => l.Contains("  b", StringComparison.Ordinal));
        Assert.True(bodyIndex > 0, "Missing untitled panel body.");
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", lines[bodyIndex - 1]);
        Assert.Contains($"{BoxDrawing.DoubleHorizontal}", lines[bodyIndex - 1]);
        Assert.Contains($"{BoxDrawing.DoubleTeeRight}", lines[bodyIndex - 1]);
    }

    [Fact]
    public void Compose_NarrowTitle_ClipsWithoutOverflowingFrame()
    {
        var text = PanelLayout.Compose(
            headerLines: ["H"],
            leftSubpanels: [new PanelSubpanel("verylongtitle", ["x"])],
            rightLines: [""],
            totalWidth: 16,
            leftInteriorWidth: 6);

        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        // Header split is line index 2 (top, header, split). Title rule is next.
        var titleRule = lines[3];
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", titleRule);
        Assert.Contains($"{BoxDrawing.DoubleTeeRight}", titleRule);
        Assert.DoesNotContain("verylongtitle", titleRule);
        // Interior is only 6 cells: clipped label "  ve  " fills it entirely (no ═ left).
        Assert.Contains("  ve  ", titleRule);
        Assert.Equal(16, titleRule.Length);
    }

    [Fact]
    public void Compose_PadsAndClipsLeftContentToInteriorWidth()
    {
        var text = PanelLayout.Compose(
            headerLines: ["H"],
            leftSubpanels: [new PanelSubpanel(["short", "this-line-is-definitely-too-long-for-the-column"])],
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
                new PanelSubpanel(["a:", "  x: 1"]),
                new PanelSubpanel(["b:", "  y: 2"]),
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
        var secondPanelIndex = Array.FindIndex(lines, l => l.Contains("b:", StringComparison.Ordinal));
        Assert.True(secondPanelIndex > 0, "Missing second panel body.");
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", lines[secondPanelIndex - 1]);
        Assert.EndsWith($"{BoxDrawing.DoubleTeeRight}", lines[secondPanelIndex - 1]);
        Assert.DoesNotContain($"{BoxDrawing.DoubleTeeTop}", normalized);
        Assert.StartsWith($"{BoxDrawing.DoubleBottomLeft}", lines[^1]);
    }

    [Fact]
    public void ComposeStacked_TitledSubpanels_UseCenteredDoubleTitleRules()
    {
        var text = PanelLayout.ComposeStacked(
            headerLines: ["H"],
            subpanels:
            [
                new PanelSubpanel("intern", ["  capacity: 1"]),
                new PanelSubpanel("boss", ["  capacity: 2"]),
            ],
            totalWidth: 40);

        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        var internRule = lines.Single(l => l.Contains("  intern  "));
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", internRule);
        Assert.EndsWith($"{BoxDrawing.DoubleTeeRight}", internRule);
        Assert.Contains($"{BoxDrawing.DoubleHorizontal}", internRule);

        var bossIndex = Array.FindIndex(lines, l => l.Contains("  boss  "));
        Assert.True(bossIndex > 0, "Missing boss title rule.");
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", lines[bossIndex]);
        Assert.EndsWith($"{BoxDrawing.DoubleTeeRight}", lines[bossIndex]);

        // Divider retained on its own line above the title rule.
        var divider = lines[bossIndex - 1];
        Assert.StartsWith($"{BoxDrawing.DoubleTeeLeft}", divider);
        Assert.Contains($"{BoxDrawing.DoubleHorizontal}", divider);
        Assert.EndsWith($"{BoxDrawing.DoubleTeeRight}", divider);
        Assert.DoesNotContain("intern:", normalized);
    }

    [Fact]
    public void ComposeStacked_RejectsEmptySubpanels()
    {
        Assert.Throws<ArgumentException>(() =>
            PanelLayout.ComposeStacked(
                headerLines: ["H"],
                subpanels: Array.Empty<PanelSubpanel>(),
                totalWidth: 40));
    }
}
