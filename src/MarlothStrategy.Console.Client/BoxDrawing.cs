namespace MarlothStrategy.Console.Client;

/// <summary>Classic single-, double-, and mixed-line box-drawing characters.</summary>
public static class BoxDrawing
{
    // Single
    public const char SingleHorizontal = '─';
    public const char SingleVertical = '│';
    public const char SingleTopLeft = '┌';
    public const char SingleTopRight = '┐';
    public const char SingleBottomLeft = '└';
    public const char SingleBottomRight = '┘';
    public const char SingleTeeLeft = '├';
    public const char SingleTeeRight = '┤';
    public const char SingleTeeTop = '┬';
    public const char SingleTeeBottom = '┴';
    public const char SingleCross = '┼';

    // Double
    public const char DoubleHorizontal = '═';
    public const char DoubleVertical = '║';
    public const char DoubleTopLeft = '╔';
    public const char DoubleTopRight = '╗';
    public const char DoubleBottomLeft = '╚';
    public const char DoubleBottomRight = '╝';
    public const char DoubleTeeLeft = '╠';
    public const char DoubleTeeRight = '╣';
    public const char DoubleTeeTop = '╦';
    public const char DoubleTeeBottom = '╩';
    public const char DoubleCross = '╬';

    // Mixed: double vertical + single horizontal
    public const char MixedTeeLeft = '╟';
    public const char MixedTeeRight = '╢';
    public const char MixedCross = '╫';

    // Mixed: single vertical + double horizontal
    public const char MixedTeeTop = '╤';
    public const char MixedTeeBottom = '╧';
    public const char MixedCrossDoubleH = '╪';

    /// <summary>Single vertical continuing up/down with a double horizontal arriving from the left.</summary>
    public const char MixedTeeRightDoubleH = '╡';
}
