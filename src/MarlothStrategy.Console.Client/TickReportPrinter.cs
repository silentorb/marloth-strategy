using System.Globalization;
using System.Text;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

public static class TickReportPrinter
{
    public static string FormatStartingStocks(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sb = new StringBuilder();
        sb.AppendLine($"Starting stocks (tick {state.Tick})");
        foreach (var pair in state.PortSignals.OrderBy(p => p.Key.Node.Value, StringComparer.Ordinal)
                     .ThenBy(p => p.Key.Port.Value, StringComparer.Ordinal))
        {
            sb.AppendLine(
                $"  {pair.Key.Node}.{pair.Key.Port}: {FormatQuantity(pair.Value.Quantity)}");
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatTickReport(ProductionTickResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var sb = new StringBuilder();
        sb.AppendLine($"Tick {result.State.Tick}");
        sb.AppendLine(
            $"{Pad("Node", 10)} {Pad("Effort", 8)} {Pad("Input", 22)} {Pad("Consumed", 10)} {Pad("Residual", 10)} {"Output"}");

        foreach (var row in result.Nodes)
        {
            var input = $"{row.InputType.Value} {FormatQuantity(row.Available)}";
            var output = $"{row.OutputType.Value} {FormatQuantity(row.Produced)}";
            sb.AppendLine(
                $"{Pad(row.NodeId.Value, 10)} {Pad(FormatQuantity(row.Effort), 8)} {Pad(input, 22)} {Pad(FormatQuantity(row.Consumed), 10)} {Pad(FormatQuantity(row.Residual), 10)} {output}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatQuantity(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string FormatQuantity(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Pad(string value, int width) =>
        value.Length >= width ? value : value.PadRight(width);
}
