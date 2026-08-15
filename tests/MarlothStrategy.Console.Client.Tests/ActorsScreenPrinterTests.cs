using System.Collections.Immutable;
using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class ActorsScreenPrinterTests
{
    private static readonly NodeTypeConfigs DefaultConfigs = new(
        new EnchantNodeConfig(
            Effort: 10,
            VolumeDelta: 10,
            DarknessDelta: 1,
            FallacyConstant: 1,
            DesignDarknessDelta: 0.3),
        new TestingNodeConfig(Effort: 10, FallacyReduction: 5),
        new SellNodeConfig(Effort: 10, PayoutFloor: 0),
        new TreasuryNodeConfig(Effort: 1),
        new PayrollNodeConfig(new PayrollScheduleConfig("month", "day", 0, 10), BaseEffort: 1, PerActorEffort: 1),
        new MergeNodeConfig(Effort: 1),
        new DesignNodeConfig(Effort: 3, DesignDelta: 1, DarknessReduction: 0.9));

    private static readonly ImmutableDictionary<ActorId, Actor> DefaultActors =
        ImmutableDictionary<ActorId, Actor>.Empty
            .Add(
                MagicAgencySeed.ActorId,
                new Actor(
                    MagicAgencySeed.ActorId,
                    Capacity: 1.0m,
                    ImmutableDictionary<string, double>.Empty
                        .Add(ActorStatKeys.Enchanting, 10)
                        .Add(ActorStatKeys.Sales, 10),
                    Wage: 2))
            .Add(
                MagicAgencySeed.BossActorId,
                new Actor(
                    MagicAgencySeed.BossActorId,
                    Capacity: 1.0m,
                    ImmutableDictionary<string, double>.Empty
                        .Add(ActorStatKeys.Sales, 10)
                        .Add(ActorStatKeys.Payroll, 10)
                        .Add(ActorStatKeys.Treasury, 10),
                    Wage: 3));

    [Fact]
    public void FormatScreen_UsesPanelFrameHeaderAndActorContent()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var text = ActorsScreenPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);
        var normalized = text.Replace("\r\n", "\n");

        Assert.StartsWith($"{BoxDrawing.DoubleTopLeft}", normalized);
        Assert.Contains("Marloth Strategy", text);
        Assert.Contains("Tick 0", text);
        Assert.Contains("month 1, week 1/4, day 1/7", text);
        Assert.Contains("screen: actors", text);
        Assert.Contains("actors: boss, intern", text);
        Assert.Contains("  intern  ", text);
        Assert.Contains("  boss  ", text);
        Assert.DoesNotContain("intern:", text);
        Assert.DoesNotContain("boss:", text);

        // Values live in their own column; nested stat keys keep their deeper indentation.
        AssertValue(normalized, "  intern  ", "  capacity:", "1");
        AssertValue(normalized, "  intern  ", "  wage:", "2");
        AssertValue(normalized, "  intern  ", "  stats:", string.Empty);
        AssertValue(normalized, "  intern  ", "    enchanting:", "10");
        AssertValue(normalized, "  intern  ", "    sales:", "10");
        AssertValue(normalized, "  boss  ", "  wage:", "3");
        AssertValue(normalized, "  boss  ", "    payroll:", "10");
        AssertValue(normalized, "  boss  ", "    treasury:", "10");
        Assert.Contains("enchant 1", text);
        Assert.Contains("testing 1", text);
        Assert.Contains("payroll 1", text);
        Assert.Contains("sell 1", text);
        Assert.Contains("treasury 1", text);
        Assert.Contains($"{BoxDrawing.DoubleTeeLeft}", normalized);
        Assert.Contains($"{BoxDrawing.DoubleTeeRight}", normalized);
        Assert.DoesNotContain($"{BoxDrawing.MixedTeeLeft}", normalized);
        Assert.DoesNotContain($"{BoxDrawing.MixedTeeRight}", normalized);
        Assert.DoesNotContain("screen: workflow", text);
    }

    [Fact]
    public void FormatScreen_UnpaidActor_ShowsWageNone()
    {
        var unpaidId = new ActorId("volunteer");
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            unpaidId,
            new Actor(
                unpaidId,
                Capacity: 0.5m,
                ImmutableDictionary<string, double>.Empty.Add(ActorStatKeys.Enchanting, 1),
                Wage: null));
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray<Assignment>.Empty,
        };

        var text = ActorsScreenPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);

        var normalized = text.Replace("\r\n", "\n");
        Assert.Contains("  volunteer  ", text);
        Assert.DoesNotContain("volunteer:", text);
        AssertValue(normalized, "  volunteer  ", "  wage:", "none");
        AssertValue(normalized, "  volunteer  ", "  capacity:", "0.5");
    }

    [Fact]
    public void FormatScreen_EmptyStats_ShowsStatsNone()
    {
        var id = new ActorId("blank");
        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            id,
            new Actor(id, Capacity: 1m, ImmutableDictionary<string, double>.Empty, Wage: 1));
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = actors,
            Assignments = ImmutableArray<Assignment>.Empty,
        };

        var text = ActorsScreenPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);

        AssertValue(text.Replace("\r\n", "\n"), "  blank  ", "  stats:", "none");
    }

    [Fact]
    public void FormatScreen_EmptyRoster_ShowsActorsZeroSubpanel()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors) with
        {
            Actors = ImmutableDictionary<ActorId, Actor>.Empty,
            Assignments = ImmutableArray<Assignment>.Empty,
        };

        var text = ActorsScreenPrinter.FormatScreen(state, width: PanelLayout.DefaultWidth);

        Assert.Contains("actors: 0", text);
        Assert.StartsWith($"{BoxDrawing.DoubleTopLeft}", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void FormatScreen_NarrowWidth_DoesNotThrow()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var text = ActorsScreenPrinter.FormatScreen(state, width: 40);
        Assert.Contains("screen: actors", text);
        Assert.Equal(40, text.Replace("\r\n", "\n").Split('\n')[0].Length);
    }

    [Fact]
    public void FormatScreen_ValueColumn_AlignsAcrossActorSubpanels()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var normalized = ActorsScreenPrinter
            .FormatScreen(state, width: PanelLayout.DefaultWidth)
            .Replace("\r\n", "\n");

        var internRow = RowAfterTitle(normalized, "  intern  ", "  capacity:");
        var bossRow = RowAfterTitle(normalized, "  boss  ", "  capacity:");

        Assert.Equal(
            internRow.IndexOf(BoxDrawing.SingleVertical),
            bossRow.IndexOf(BoxDrawing.SingleVertical));
    }

    [Fact]
    public void FormatScreen_NumericValues_PadSoDigitsAlignByMagnitude()
    {
        var state = MagicAgencySeed.CreateInitialState(DefaultConfigs, DefaultActors);
        var normalized = ActorsScreenPrinter
            .FormatScreen(state, width: PanelLayout.DefaultWidth)
            .Replace("\r\n", "\n");

        // Stats reach two digits, so single-digit values shift right by one to line up.
        var enchanting = RawCell(RowAfterTitle(normalized, "  intern  ", "    enchanting:"), 1);
        var capacity = RawCell(RowAfterTitle(normalized, "  intern  ", "  capacity:"), 1);

        Assert.StartsWith("10", enchanting);
        Assert.StartsWith(" 1", capacity);
    }

    /// <summary>Asserts the value cell of <paramref name="key"/> inside the panel titled <paramref name="title"/>.</summary>
    private static void AssertValue(string normalized, string title, string key, string expectedValue) =>
        Assert.Equal(expectedValue, Cell(RowAfterTitle(normalized, title, key), 1));

    /// <summary>Column cell without trimming, so leading alignment padding stays visible.</summary>
    private static string RawCell(string row, int index)
    {
        var cells = row.Split(BoxDrawing.SingleVertical);
        Assert.True(cells.Length > index, $"Row has no column {index}: {row}");
        return cells[index];
    }

    /// <summary>Finds the row whose key column holds <paramref name="key"/>, searching after the panel title.</summary>
    private static string RowAfterTitle(string normalized, string title, string key)
    {
        var lines = normalized.Split('\n');
        var start = Array.FindIndex(lines, l => l.Contains(title, StringComparison.Ordinal));
        Assert.True(start >= 0, $"Missing subpanel title '{title}'.");

        // Key cell keeps its indentation behind the border and the one-cell panel margin.
        var expectedKeyCell = $"{BoxDrawing.DoubleVertical} {key}";
        var row = Array.FindIndex(lines, start, l => Cell(l, 0).TrimEnd() == expectedKeyCell);
        Assert.True(row >= 0, $"Missing row '{key}' after '{title}'.");
        return lines[row];
    }

    private static string Cell(string row, int index)
    {
        var cells = row.Split(BoxDrawing.SingleVertical);
        Assert.True(cells.Length > index, $"Row has no column {index}: {row}");
        return index == 0 ? cells[0] : cells[index].Trim();
    }
}
