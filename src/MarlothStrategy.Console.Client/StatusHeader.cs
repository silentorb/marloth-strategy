using System.Collections.Immutable;
using MarlothStrategy.Simulation;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

/// <summary>Shared top status strip for modal console screens.</summary>
public static class StatusHeader
{
    private const char Arrow = '\u2192';

    public static IReadOnlyList<string> Build(
        GameState state,
        GameState? previous,
        GameConfig? config,
        ScreenId screen)
    {
        ArgumentNullException.ThrowIfNull(state);

        var header = new List<string>
        {
            TickReportPrinter.Title,
            $"Tick {state.Tick}",
            TickReportPrinter.FormatCalendarLine(state.TimePartitions, state.Tick),
            FormatScreenLine(screen),
        };
        if (config is not null)
        {
            header.Add($"scenario: {config.ScenarioLabel} seed {config.ScenarioSeed}");
        }

        header.Add(FormatActorsLine(state, previous));
        return header;
    }

    public static string FormatScreenLine(ScreenId screen) => screen switch
    {
        ScreenId.Workflow => "screen: workflow",
        ScreenId.Actors => "screen: actors",
        _ => throw new ArgumentOutOfRangeException(nameof(screen), screen, "Unknown screen."),
    };

    private static string FormatActorsLine(GameState state, GameState? previous)
    {
        var current = FormatActorRoster(state.Actors);
        if (previous is null)
        {
            return $"actors: {current}";
        }

        var prior = FormatActorRoster(previous.Actors);
        return $"actors: {FormatChange(prior, current)}";
    }

    private static string FormatActorRoster(ImmutableDictionary<ActorId, Actor> actors)
    {
        if (actors.IsEmpty)
        {
            return "0";
        }

        return string.Join(
            ", ",
            actors.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).Select(id => id.Value));
    }

    private static string FormatChange(string prior, string current) =>
        prior == current ? current : $"{prior} {Arrow} {current}";
}
