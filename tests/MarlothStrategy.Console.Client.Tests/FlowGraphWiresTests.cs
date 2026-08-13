namespace MarlothStrategy.Console.Client.Tests;

public sealed class FlowGraphWiresTests
{
    [Theory]
    [InlineData(WireDir.N | WireDir.E, BoxDrawing.SingleBottomLeft)]
    [InlineData(WireDir.N | WireDir.W, BoxDrawing.SingleBottomRight)]
    [InlineData(WireDir.S | WireDir.E, BoxDrawing.SingleTopLeft)]
    [InlineData(WireDir.S | WireDir.W, BoxDrawing.SingleTopRight)]
    [InlineData(WireDir.N | WireDir.S | WireDir.E, BoxDrawing.SingleTeeLeft)]
    [InlineData(WireDir.N | WireDir.S | WireDir.W, BoxDrawing.SingleTeeRight)]
    [InlineData(WireDir.E | WireDir.W | WireDir.N, BoxDrawing.SingleTeeBottom)]
    [InlineData(WireDir.E | WireDir.W | WireDir.S, BoxDrawing.SingleTeeTop)]
    [InlineData(WireDir.N | WireDir.E | WireDir.S | WireDir.W, BoxDrawing.SingleCross)]
    [InlineData(WireDir.N | WireDir.S, BoxDrawing.SingleVertical)]
    [InlineData(WireDir.E | WireDir.W, BoxDrawing.SingleHorizontal)]
    public void GlyphFor_MapsDirectionMaskToBoxDrawing(WireDir dirs, char expected) =>
        Assert.Equal(expected, FlowGraphWires.GlyphFor(dirs));

    [Fact]
    public void StampSegment_CornerAndTeeAccumulateDirections()
    {
        var mask = new Dictionary<(int X, int Y), WireDir>();

        // ┌─┐ shaped path: (0,0)->(0,1)->(2,1)->(2,0) plus a tee down from mid
        FlowGraphWires.StampSegment(mask, 0, 0, 0, 1);
        FlowGraphWires.StampSegment(mask, 0, 1, 2, 1);
        FlowGraphWires.StampSegment(mask, 2, 1, 2, 0);
        FlowGraphWires.StampSegment(mask, 1, 1, 1, 2);

        Assert.Equal(BoxDrawing.SingleBottomLeft, FlowGraphWires.GlyphFor(mask[(0, 1)]));  // └
        Assert.Equal(BoxDrawing.SingleBottomRight, FlowGraphWires.GlyphFor(mask[(2, 1)])); // ┘
        Assert.Equal(BoxDrawing.SingleTeeTop, FlowGraphWires.GlyphFor(mask[(1, 1)]));      // ┬
        Assert.Equal(BoxDrawing.SingleVertical, FlowGraphWires.GlyphFor(mask[(0, 0)]));
    }
}
