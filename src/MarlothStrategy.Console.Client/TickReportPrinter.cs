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
                $"  {pair.Key.Node}.{pair.Key.Port}: {FormatSignal(pair.Value)}");
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatTickReport(ProductionTickResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var sb = new StringBuilder();
        sb.AppendLine($"Tick {result.State.Tick}");
        sb.AppendLine(
            $"{Pad("Node", 10)} {Pad("Effort", 8)} {Pad("Input", 28)} {Pad("Consumed", 10)} {Pad("Residual", 16)} {"Output"}");

        foreach (var row in result.Nodes)
        {
            var input = $"{row.InputType.Value} {FormatSignal(row.Available)}";
            var residual = FormatSignal(row.Residual);
            var output = $"{row.OutputType.Value} {FormatSignal(row.Produced)}";
            sb.AppendLine(
                $"{Pad(row.NodeId.Value, 10)} {Pad(FormatEffort(row.Effort), 8)} {Pad(input, 28)} {Pad(row.Consumed ? "yes" : "no", 10)} {Pad(residual, 16)} {output}");
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatSignal(SignalValue? value) => value switch
    {
        null => "-",
        SignalValue.Money m => FormatRounded(m.Amount),
        SignalValue.Enchantment e =>
            $"{FormatRounded(e.Volume)}/{FormatRounded(e.Darkness)}/{FormatRounded(e.Fallacy)}",
        _ => throw new InvalidOperationException($"Unknown signal value kind: {value.GetType().Name}."),
    };

    private static string FormatRounded(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

    private static string FormatEffort(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Pad(string value, int width) =>
        value.Length >= width ? value : value.PadRight(width);
}
