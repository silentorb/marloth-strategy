using System.Globalization;

namespace MarlothStrategy.Console.Client;

/// <summary>Shared numeric formatting for console panel leaves.</summary>
public static class DisplayFormatting
{
    /// <summary>
    /// Formats a decimal weight/capacity: whole numbers without a trailing ".0".
    /// </summary>
    public static string FormatDecimal(decimal value)
    {
        if (value == decimal.Truncate(value))
        {
            return decimal.Truncate(value).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }
}
