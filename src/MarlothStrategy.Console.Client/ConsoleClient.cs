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
        var advanceLabel = state.TimePartitions.AdvanceUnit;
        DrawReport(state, config, baseline);

        while (true)
        {
            System.Console.Write(
                $"Enter = next tick, Space = next {advanceLabel}, q = quit> ");
            switch (ReadPromptAction())
            {
                case PromptAction.Quit:
                    System.Console.WriteLine();
                    System.Console.WriteLine("Exiting.");
                    return;
                case PromptAction.Unknown:
                    DrawReport(state, config, baseline);
                    System.Console.WriteLine(
                        $"Unknown input. Press Enter for next tick, Space for next {advanceLabel}, or q to quit.");
                    continue;
                case PromptAction.AdvanceTick:
                    AdvanceAndDraw(ref state, config, baseline, tickCount: 1);
                    break;
                case PromptAction.AdvanceMacro:
                    AdvanceAndDraw(
                        ref state,
                        config,
                        baseline,
                        tickCount: state.TimePartitions.AdvanceTickCount);
                    break;
            }
        }
    }

    private static void AdvanceAndDraw(
        ref GameState state,
        GameConfig config,
        GameState baseline,
        int tickCount)
    {
        var previous = state;
        var result = ProductionTick.AdvanceTicksWithReport(state, tickCount);
        state = result.State;
        DrawReport(state, config, baseline, previous, result);
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

    /// <summary>
    /// Interactive terminals use single-key <see cref="System.Console.ReadKey"/> (Enter / Space / q).
    /// Redirected stdin (agent smoke) falls back to line mode.
    /// </summary>
    private static PromptAction ReadPromptAction()
    {
        if (System.Console.IsInputRedirected)
        {
            return PromptDecoder.FromRedirectedLine(System.Console.ReadLine());
        }

        return PromptDecoder.FromConsoleKey(System.Console.ReadKey(intercept: true));
    }
}
