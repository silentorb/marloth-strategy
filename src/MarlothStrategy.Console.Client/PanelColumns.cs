namespace MarlothStrategy.Console.Client;

/// <summary>Shared helpers for splitting subpanel bodies into fixed-width text columns.</summary>
internal static class PanelColumns
{
    /// <summary>Clips or right-pads <paramref name="text"/> to exactly <paramref name="width"/> cells.</summary>
    public static string ClipPad(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        return text.Length > width ? text[..width] : text.PadRight(width);
    }

    /// <summary>Longest key length in <paramref name="keys"/>, or 0 when empty.</summary>
    public static int LongestKey(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var longest = 0;
        foreach (var key in keys)
        {
            if (key.Length > longest)
            {
                longest = key.Length;
            }
        }

        return longest;
    }

    /// <summary>
    /// Width for the property-name column: wide enough for <paramref name="longestKey"/> so nested
    /// indentation survives, while still leaving <paramref name="minValueWidth"/> cells for values.
    /// Callers share one width across sibling subpanels so the value column lines up vertically.
    /// </summary>
    public static int KeyColumnWidth(int longestKey, int available, int minValueWidth)
    {
        var max = available - 1 - minValueWidth;
        if (max < 1)
        {
            return Math.Max(1, available - 1);
        }

        return Math.Clamp(longestKey, 1, max);
    }

    /// <summary>
    /// Widest leading integer part among cells that start with a plain number. Callers pass this to
    /// <see cref="AlignNumeric"/> so digits of the same magnitude stack in a column.
    /// </summary>
    public static int NumericPrefixWidth(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var widest = 0;
        foreach (var value in values)
        {
            var width = LeadingIntegerWidth(value);
            if (width > widest)
            {
                widest = width;
            }
        }

        return widest;
    }

    /// <summary>
    /// Left-pads a numeric cell so its integer part ends <paramref name="prefixWidth"/> cells in,
    /// lining ones up with ones and tens with tens. Non-numeric cells stay flush left.
    /// </summary>
    public static string AlignNumeric(string value, int prefixWidth)
    {
        var width = LeadingIntegerWidth(value);
        if (width <= 0 || width >= prefixWidth)
        {
            return value;
        }

        return value.PadLeft(value.Length + (prefixWidth - width));
    }

    /// <summary>
    /// Length of the integer part (sign included) when the first whitespace-delimited token is a
    /// plain number such as <c>0</c>, <c>-15</c>, or <c>2.5</c>; 0 otherwise so hashes and words are
    /// left alone. Change annotations like <c>0 → 10</c> align on their leading number.
    /// </summary>
    private static int LeadingIntegerWidth(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var space = value.IndexOf(' ');
        var token = space < 0 ? value : value[..space];

        var index = 0;
        if (token[0] == '+' || token[0] == '-')
        {
            index++;
        }

        var digitsStart = index;
        while (index < token.Length && char.IsAsciiDigit(token[index]))
        {
            index++;
        }

        if (index == digitsStart)
        {
            return 0;
        }

        var integerWidth = index;
        if (index == token.Length)
        {
            return integerWidth;
        }

        // Only a fractional tail may follow; anything else (e.g. a hex hash) is not a number.
        if (token[index] != '.')
        {
            return 0;
        }

        index++;
        var fractionStart = index;
        while (index < token.Length && char.IsAsciiDigit(token[index]))
        {
            index++;
        }

        return index > fractionStart && index == token.Length ? integerWidth : 0;
    }
}
