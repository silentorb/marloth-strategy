namespace MarlothStrategy.Console.Client;

public enum PromptAction
{
    AdvanceTick,
    AdvanceMacro,
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
            return PromptAction.AdvanceTick;
        }

        if (trimmed.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            return PromptAction.Quit;
        }

        if (trimmed.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            return PromptAction.NewGame;
        }

        if (trimmed.Equals("space", StringComparison.OrdinalIgnoreCase))
        {
            return PromptAction.AdvanceMacro;
        }

        return PromptAction.Unknown;
    }

    public static PromptAction FromConsoleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            return PromptAction.AdvanceTick;
        }

        if (key.Key == ConsoleKey.Spacebar || key.KeyChar == ' ')
        {
            return PromptAction.AdvanceMacro;
        }

        if (key.KeyChar is 'q' or 'Q')
        {
            return PromptAction.Quit;
        }

        if (key.KeyChar is 'n' or 'N')
        {
            return PromptAction.NewGame;
        }

        return PromptAction.Unknown;
    }
}
