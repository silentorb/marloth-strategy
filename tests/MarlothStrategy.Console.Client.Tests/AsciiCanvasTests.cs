namespace MarlothStrategy.Console.Client.Tests;

public sealed class AsciiCanvasTests
{
    [Fact]
    public void DrawDoubleRect_WritesCornersAndEdges()
    {
        var canvas = new AsciiCanvas(5, 4);
        canvas.DrawDoubleRect(0, 0, 5, 4);
        var text = canvas.ToString().Replace("\r\n", "\n");

        Assert.Equal(
            string.Join(
                '\n',
                $"{BoxDrawing.DoubleTopLeft}{BoxDrawing.DoubleHorizontal}{BoxDrawing.DoubleHorizontal}{BoxDrawing.DoubleHorizontal}{BoxDrawing.DoubleTopRight}",
                $"{BoxDrawing.DoubleVertical}   {BoxDrawing.DoubleVertical}",
                $"{BoxDrawing.DoubleVertical}   {BoxDrawing.DoubleVertical}",
                $"{BoxDrawing.DoubleBottomLeft}{BoxDrawing.DoubleHorizontal}{BoxDrawing.DoubleHorizontal}{BoxDrawing.DoubleHorizontal}{BoxDrawing.DoubleBottomRight}"),
            text);
    }

    [Fact]
    public void WriteText_ClipsToMaxWidth()
    {
        var canvas = new AsciiCanvas(6, 1);
        canvas.WriteText(1, 0, "abcdef", maxWidth: 3);
        Assert.Equal(" abc  ", canvas.ToString());
    }

    [Fact]
    public void DrawHorizontalDivider_WritesJunctionsAndFill()
    {
        var canvas = new AsciiCanvas(5, 1);
        canvas.DrawHorizontalDivider(0, 4, 0, BoxDrawing.MixedTeeLeft, BoxDrawing.SingleHorizontal, BoxDrawing.MixedTeeRight);
        Assert.Equal(
            $"{BoxDrawing.MixedTeeLeft}{BoxDrawing.SingleHorizontal}{BoxDrawing.SingleHorizontal}{BoxDrawing.SingleHorizontal}{BoxDrawing.MixedTeeRight}",
            canvas.ToString());
    }
}
