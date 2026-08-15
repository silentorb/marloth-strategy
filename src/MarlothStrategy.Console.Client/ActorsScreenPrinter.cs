using System.Globalization;
using MarlothStrategy.Simulation;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

/// <summary>Formats the actors modal screen: one full-width subpanel per actor.</summary>
public static class ActorsScreenPrinter
{
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

        var subpanels = new List<IReadOnlyList<string>>();
        if (state.Actors.IsEmpty)
        {
            subpanels.Add(["actors: 0"]);
        }
        else
        {
            foreach (var actorId in state.Actors.Keys.OrderBy(id => id.Value, StringComparer.Ordinal))
            {
                var actor = state.Actors[actorId];
                subpanels.Add(FormatActorSubpanel(state, actor, usableWidth));
            }
        }

        return PanelLayout.ComposeStacked(header, subpanels, width);
    }

    private static List<string> FormatActorSubpanel(GameState state, Actor actor, int usableWidth)
    {
        var propertyLines = FormatPropertyLines(actor);
        var assignmentLines = FormatAssignmentLines(state, actor.Id);
        return MergeTwoColumns(propertyLines, assignmentLines, usableWidth);
    }

    private static List<string> FormatPropertyLines(Actor actor)
    {
        var lines = new List<string>
        {
            $"{actor.Id.Value}:",
            $"  capacity: {DisplayFormatting.FormatDecimal(actor.Capacity)}",
            $"  wage: {FormatWage(actor.Wage)}",
        };

        if (actor.Stats.IsEmpty)
        {
            lines.Add("  stats: none");
            return lines;
        }

        lines.Add("  stats:");
        foreach (var key in actor.Stats.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            lines.Add($"    {key}: {FormatStat(actor.Stats[key])}");
        }

        return lines;
    }

    private static List<string> FormatAssignmentLines(GameState state, ActorId actorId)
    {
        return state.Assignments
            .Where(a => a.ActorId == actorId)
            .OrderBy(a => a.NodeId.Value, StringComparer.Ordinal)
            .Select(a => $"{a.NodeId.Value} {DisplayFormatting.FormatDecimal(a.Weight)}")
            .ToList();
    }

    private static List<string> MergeTwoColumns(
        IReadOnlyList<string> propertyLines,
        IReadOnlyList<string> assignmentLines,
        int usableWidth)
    {
        if (usableWidth < 3)
        {
            return propertyLines.ToList();
        }

        var assignWidth = Math.Clamp(usableWidth / 3, 10, 24);
        if (usableWidth < assignWidth + 1 + 8)
        {
            return propertyLines.ToList();
        }

        var propWidth = usableWidth - 1 - assignWidth;
        var rowCount = Math.Max(1, Math.Max(propertyLines.Count, assignmentLines.Count));
        var merged = new List<string>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            var left = i < propertyLines.Count ? propertyLines[i] : string.Empty;
            var right = i < assignmentLines.Count ? assignmentLines[i] : string.Empty;
            merged.Add(
                $"{ClipPad(left, propWidth)}{BoxDrawing.SingleVertical}{ClipPad(right, assignWidth)}");
        }

        return merged;
    }

    private static string ClipPad(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (text.Length > width)
        {
            return text[..width];
        }

        return text.PadRight(width);
    }

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
