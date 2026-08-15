using MarlothStrategy.Console.Client;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class PromptDecoderTests
{
    [Theory]
    [InlineData(null, PromptAction.Quit)]
    [InlineData("", PromptAction.AdvanceTick)]
    [InlineData("   ", PromptAction.AdvanceTick)]
    [InlineData("q", PromptAction.Quit)]
    [InlineData("Q", PromptAction.Quit)]
    [InlineData("n", PromptAction.NewGame)]
    [InlineData("N", PromptAction.NewGame)]
    [InlineData("space", PromptAction.AdvanceMacro)]
    [InlineData("SPACE", PromptAction.AdvanceMacro)]
    [InlineData("x", PromptAction.Unknown)]
    public void FromRedirectedLine_MapsExpectedTokens(string? line, PromptAction expected)
    {
        Assert.Equal(expected, PromptDecoder.FromRedirectedLine(line));
    }

    [Fact]
    public void FromConsoleKey_MapsEnterSpaceNewGameAndQuit()
    {
        Assert.Equal(
            PromptAction.AdvanceTick,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
        Assert.Equal(
            PromptAction.AdvanceMacro,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false)));
        Assert.Equal(
            PromptAction.Quit,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false)));
        Assert.Equal(
            PromptAction.NewGame,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false)));
        Assert.Equal(
            PromptAction.Unknown,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false)));
    }
}
