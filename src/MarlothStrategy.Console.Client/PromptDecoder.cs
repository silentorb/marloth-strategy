namespace MarlothStrategy.Console.Client;

public enum PromptAction
{
    AdvanceStep,
    DecreaseStepResolution,
    IncreaseStepResolution,
    ShowWorkflowScreen,
    ShowActorsScreen,
    NewGame,
    Quit,
    Unknown,
}

/// <summary>
/// Decodes console prompt input for interactive keys and redirected line mode.
/// </summary>
public static class PromptDecoder
{
    public static PromptAction FromRedirectedLine(string? line)
    {
        if (line is null)
        {
            return PromptAction.Quit;
        }

        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return PromptAction.AdvanceStep;
        }

        if (trimmed.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            return PromptAction.Quit;
        }

        if (trimmed.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            return PromptAction.NewGame;
        }

        if (trimmed.Equals("w", StringComparison.OrdinalIgnoreCase))
        {
            return PromptAction.ShowWorkflowScreen;
        }

        if (trimmed.Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            return PromptAction.ShowActorsScreen;
        }

        if (trimmed == "-")
        {
            return PromptAction.DecreaseStepResolution;
        }

        if (trimmed is "+" or "=")
        {
            return PromptAction.IncreaseStepResolution;
        }

        return PromptAction.Unknown;
    }

    public static PromptAction FromConsoleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            return PromptAction.AdvanceStep;
        }

        if (key.KeyChar == '-')
        {
            return PromptAction.DecreaseStepResolution;
        }

        // Unshifted '=' shares the '+' keycap on common US layouts; also accept
        // shifted '+' and numpad Add so players never need Shift for coarser steps.
        if (key.KeyChar is '+' or '=' || key.Key == ConsoleKey.Add)
        {
            return PromptAction.IncreaseStepResolution;
        }

        if (key.KeyChar is 'q' or 'Q')
        {
            return PromptAction.Quit;
        }

        if (key.KeyChar is 'n' or 'N')
        {
            return PromptAction.NewGame;
        }

        if (key.KeyChar is 'w' or 'W')
        {
            return PromptAction.ShowWorkflowScreen;
        }

        if (key.KeyChar is 'a' or 'A')
        {
            return PromptAction.ShowActorsScreen;
        }

        return PromptAction.Unknown;
    }
}
