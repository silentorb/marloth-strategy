using System.Globalization;
using MarlothStrategy.Simulation;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

/// <summary>Formats the actors modal screen: one full-width subpanel per actor.</summary>
public static class ActorsScreenPrinter
{
    /// <summary>Cells reserved for values; long property names clip before values do.</summary>
    private const int MinValueWidth = 8;

    /// <summary>One body row: indented property name and its value.</summary>
    private readonly record struct ActorPropertyRow(string Key, string Value);

    public static string FormatScreen(
        GameState state,
        GameState? previous = null,
        int width = PanelLayout.DefaultWidth,
        GameConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (width < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "width must be at least 10.");
        }

        var header = StatusHeader.Build(state, previous, config, ScreenId.Actors);
        var interiorWidth = width - 2;
        // WritePadded reserves one cell of left margin inside the column.
        var usableWidth = Math.Max(1, interiorWidth - 1);

        var subpanels = new List<PanelSubpanel>();
        if (state.Actors.IsEmpty)
        {
            subpanels.Add(new PanelSubpanel(["actors: 0"]));
            return PanelLayout.ComposeStacked(header, subpanels, width);
        }

        var bodies = state.Actors.Keys
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .Select(id => (
                Id: id,
                Rows: FormatPropertyRows(state.Actors[id]),
                Assignments: FormatAssignmentLines(state, id)))
            .ToArray();

        // Metrics span every actor so columns line up down the screen.
        var allRows = bodies.SelectMany(b => b.Rows).ToArray();
        var longestKey = PanelColumns.LongestKey(allRows.Select(r => r.Key));
        var valuePrefix = PanelColumns.NumericPrefixWidth(allRows.Select(r => r.Value));

        foreach (var body in bodies)
        {
            var lines = MergeActorColumns(body.Rows, body.Assignments, usableWidth, longestKey, valuePrefix);
            subpanels.Add(new PanelSubpanel(body.Id.Value, lines));
        }

        return PanelLayout.ComposeStacked(header, subpanels, width);
    }

    private static List<ActorPropertyRow> FormatPropertyRows(Actor actor)
    {
        var rows = new List<ActorPropertyRow>
        {
            new("  capacity:", DisplayFormatting.FormatDecimal(actor.Capacity)),
            new("  wage:", FormatWage(actor.Wage)),
        };

        if (actor.Stats.IsEmpty)
        {
            rows.Add(new("  stats:", "none"));
            return rows;
        }

        rows.Add(new("  stats:", string.Empty));
        foreach (var key in actor.Stats.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            rows.Add(new($"    {key}:", FormatStat(actor.Stats[key])));
        }

        return rows;
    }

    private static List<string> FormatAssignmentLines(GameState state, ActorId actorId)
    {
        return state.Assignments
            .Where(a => a.ActorId == actorId)
            .OrderBy(a => a.NodeId.Value, StringComparer.Ordinal)
            .Select(a => $"{a.NodeId.Value} {DisplayFormatting.FormatDecimal(a.Weight)}")
            .ToList();
    }

    /// <summary>Splits an actor subpanel: property name | value | preferred assignments.</summary>
    private static List<string> MergeActorColumns(
        IReadOnlyList<ActorPropertyRow> propertyRows,
        IReadOnlyList<string> assignmentLines,
        int usableWidth,
        int longestKey,
        int valuePrefix)
    {
        if (usableWidth < 3)
        {
            return propertyRows.Select(FlattenRow).ToList();
        }

        var assignWidth = Math.Clamp(usableWidth / 3, 10, 24);
        if (usableWidth < assignWidth + 1 + 8)
        {
            return propertyRows.Select(FlattenRow).ToList();
        }

        var propWidth = usableWidth - 1 - assignWidth;
        var keyWidth = PanelColumns.KeyColumnWidth(longestKey, propWidth, MinValueWidth);
        var valueWidth = propWidth - 1 - keyWidth;
        var rowCount = Math.Max(1, Math.Max(propertyRows.Count, assignmentLines.Count));
        var merged = new List<string>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            var key = i < propertyRows.Count ? propertyRows[i].Key : string.Empty;
            var value = i < propertyRows.Count ? propertyRows[i].Value : string.Empty;
            var right = i < assignmentLines.Count ? assignmentLines[i] : string.Empty;
            merged.Add(
                $"{PanelColumns.ClipPad(key, keyWidth)}{BoxDrawing.SingleVertical}" +
                $"{PanelColumns.ClipPad(PanelColumns.AlignNumeric(value, valuePrefix), valueWidth)}" +
                $"{BoxDrawing.SingleVertical}{PanelColumns.ClipPad(right, assignWidth)}");
        }

        return merged;
    }

    private static string FlattenRow(ActorPropertyRow row) =>
        row.Value.Length == 0 ? row.Key : $"{row.Key} {row.Value}";

    private static string FormatWage(double? wage) =>
        wage is null ? "none" : FormatStat(wage.Value);

    private static string FormatStat(double value)
    {
        if (value == Math.Truncate(value))
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }
}
