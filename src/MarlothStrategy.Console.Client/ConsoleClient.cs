using MarlothStrategy.Simulation;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

public static class ConsoleClient
{
    public static void Run(GameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        System.Console.WriteLine("Marloth Strategy — console prototype");
        System.Console.WriteLine();

        var state = MagicAgencySeed.CreateInitialState();
        System.Console.WriteLine(TickReportPrinter.FormatStartingStocks(state));
        System.Console.WriteLine();

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
                    System.Console.WriteLine();
                    System.Console.WriteLine("Unknown input. Press Enter for next tick, or q to quit.");
                    continue;
                case PromptAction.Advance:
                    System.Console.WriteLine();
                    var result = ProductionTick.AdvanceTickWithReport(state);
                    state = result.State;
                    System.Console.WriteLine(TickReportPrinter.FormatTickReport(result));
                    System.Console.WriteLine();
                    break;
            }
        }
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
