using MarlothStrategy.Simulation;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

public static class ConsoleClient
{
    public static void Run(GameConfig config) =>
        Run(config, UserConfigStore.DefaultPath);

    public static void Run(GameConfig config, string userConfigPath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(userConfigPath);

        var state = ScenarioBootstrap.CreateInitialState(config);
        var baseline = state;
        var userConfig = UserConfigStore.LoadOrDefault(userConfigPath, state.TimePartitions);
        var stepResolution = userConfig.StepResolution;
        DrawReport(state, config, baseline);

        while (true)
        {
            System.Console.Write(
                $"Enter = next {stepResolution}, - = finer, + (= key) = coarser, n = new game, q = quit> ");
            switch (ReadPromptAction())
            {
                case PromptAction.Quit:
                    System.Console.WriteLine();
                    System.Console.WriteLine("Exiting.");
                    return;
                case PromptAction.Unknown:
                    DrawReport(state, config, baseline);
                    System.Console.WriteLine(
                        $"Unknown input. Press Enter for next {stepResolution}, - for finer, + (= key) for coarser, n for a new game, or q to quit.");
                    continue;
                case PromptAction.NewGame:
                    state = ScenarioBootstrap.CreateInitialState(config);
                    baseline = state;
                    DrawReport(state, config, baseline);
                    break;
                case PromptAction.AdvanceStep:
                    AdvanceAndDraw(
                        ref state,
                        config,
                        baseline,
                        tickCount: state.TimePartitions.TicksPer(stepResolution));
                    break;
                case PromptAction.DecreaseStepResolution:
                    if (state.TimePartitions.TryGetFinerStepResolution(stepResolution, out var finer))
                    {
                        SaveStepResolution(userConfigPath, state, finer);
                        stepResolution = finer;
                    }

                    DrawReport(state, config, baseline);
                    break;
                case PromptAction.IncreaseStepResolution:
                    if (state.TimePartitions.TryGetCoarserStepResolution(stepResolution, out var coarser))
                    {
                        SaveStepResolution(userConfigPath, state, coarser);
                        stepResolution = coarser;
                    }

                    DrawReport(state, config, baseline);
                    break;
            }
        }
    }

    private static void SaveStepResolution(string userConfigPath, GameState state, string stepResolution)
    {
        // Persist before mutating session selection so a failed write cannot leave
        // interactive state half-updated relative to disk.
        UserConfigStore.Save(
            userConfigPath,
            new UserConfig(stepResolution),
            state.TimePartitions);
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
    /// Interactive terminals use single-key <see cref="System.Console.ReadKey"/>
    /// (Enter / - / =|+ / n / q). Redirected stdin (agent smoke) falls back to line mode.
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
