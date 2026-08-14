using MarlothStrategy.Simulation;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

public static class ConsoleClient
{
    public static void Run(GameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var state = ScenarioBootstrap.CreateInitialState(config);
        var baseline = state;
        DrawReport(state, config, baseline);

        while (true)
        {
            System.Console.Write("Enter = next tick, q = quit> ");
            switch (ReadPromptAction())
            {
                case PromptAction.Quit:
                    System.Console.WriteLine();
                    System.Console.WriteLine("Exiting.");
                    return;
                case PromptAction.Unknown:
                    DrawReport(state, config, baseline);
                    System.Console.WriteLine("Unknown input. Press Enter for next tick, or q to quit.");
                    continue;
                case PromptAction.Advance:
                    var previous = state;
                    var result = ProductionTick.AdvanceTickWithReport(state);
                    state = result.State;
                    DrawReport(state, config, baseline, previous, result);
                    break;
            }
        }
    }

    private static void DrawReport(
        GameState state,
        GameConfig config,
        GameState baseline,
        GameState? previous = null,
        ProductionTickResult? tick = null)
    {
        TryClear();
        var width = ResolveWidth();
        System.Console.WriteLine(
            TickReportPrinter.FormatScreen(state, previous, tick, width, config, baseline));
        System.Console.WriteLine();
    }

    private static void TryClear()
    {
        try
        {
            System.Console.Clear();
        }
        catch (IOException)
        {
            // Some redirected / non-TTY hosts reject Clear; continue with a fresh write.
        }
    }

    private static int ResolveWidth()
    {
        try
        {
            var windowWidth = System.Console.WindowWidth;
            if (windowWidth >= 40)
            {
                return Math.Min(windowWidth, PanelLayout.DefaultWidth);
            }
        }
        catch (IOException)
        {
            // WindowWidth unavailable when redirected.
        }

        return PanelLayout.DefaultWidth;
    }

    private enum PromptAction
    {
        Advance,
        Quit,
        Unknown,
    }

    /// <summary>
    /// Interactive terminals use single-key <see cref="System.Console.ReadKey"/> (Enter / q).
    /// Redirected stdin (agent smoke) falls back to line mode.
    /// </summary>
    private static PromptAction ReadPromptAction()
    {
        if (System.Console.IsInputRedirected)
        {
            var line = System.Console.ReadLine();
            if (line is null)
            {
                return PromptAction.Quit;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                return PromptAction.Advance;
            }

            return trimmed.Equals("q", StringComparison.OrdinalIgnoreCase)
                ? PromptAction.Quit
                : PromptAction.Unknown;
        }

        var key = System.Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            return PromptAction.Advance;
        }

        if (key.KeyChar is 'q' or 'Q')
        {
            return PromptAction.Quit;
        }

        return PromptAction.Unknown;
    }
}
