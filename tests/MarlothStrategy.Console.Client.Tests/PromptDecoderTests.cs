using MarlothStrategy.Console.Client;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class PromptDecoderTests
{
    [Theory]
    [InlineData(null, PromptAction.Quit)]
    [InlineData("", PromptAction.AdvanceStep)]
    [InlineData("   ", PromptAction.AdvanceStep)]
    [InlineData("q", PromptAction.Quit)]
    [InlineData("Q", PromptAction.Quit)]
    [InlineData("n", PromptAction.NewGame)]
    [InlineData("N", PromptAction.NewGame)]
    [InlineData("w", PromptAction.ShowWorkflowScreen)]
    [InlineData("W", PromptAction.ShowWorkflowScreen)]
    [InlineData("a", PromptAction.ShowActorsScreen)]
    [InlineData("A", PromptAction.ShowActorsScreen)]
    [InlineData("-", PromptAction.DecreaseStepResolution)]
    [InlineData("+", PromptAction.IncreaseStepResolution)]
    [InlineData("=", PromptAction.IncreaseStepResolution)]
    [InlineData("space", PromptAction.Unknown)]
    [InlineData("x", PromptAction.Unknown)]
    public void FromRedirectedLine_MapsExpectedTokens(string? line, PromptAction expected)
    {
        Assert.Equal(expected, PromptDecoder.FromRedirectedLine(line));
    }

    [Fact]
    public void FromConsoleKey_MapsEnterResolutionControlsScreensNewGameAndQuit()
    {
        Assert.Equal(
            PromptAction.AdvanceStep,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
        Assert.Equal(
            PromptAction.DecreaseStepResolution,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('-', ConsoleKey.OemMinus, false, false, false)));
        Assert.Equal(
            PromptAction.IncreaseStepResolution,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('=', ConsoleKey.OemPlus, false, false, false)));
        Assert.Equal(
            PromptAction.IncreaseStepResolution,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('+', ConsoleKey.OemPlus, true, false, false)));
        Assert.Equal(
            PromptAction.IncreaseStepResolution,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('+', ConsoleKey.Add, false, false, false)));
        Assert.Equal(
            PromptAction.Quit,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false)));
        Assert.Equal(
            PromptAction.NewGame,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false)));
        Assert.Equal(
            PromptAction.ShowWorkflowScreen,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('w', ConsoleKey.W, false, false, false)));
        Assert.Equal(
            PromptAction.ShowWorkflowScreen,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('W', ConsoleKey.W, true, false, false)));
        Assert.Equal(
            PromptAction.ShowActorsScreen,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false)));
        Assert.Equal(
            PromptAction.ShowActorsScreen,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('A', ConsoleKey.A, true, false, false)));
        Assert.Equal(
            PromptAction.Unknown,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false)));
        Assert.Equal(
            PromptAction.Unknown,
            PromptDecoder.FromConsoleKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false)));
    }
}
