using System.Globalization;
using System.Text;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

public static class TickReportPrinter
{
    private const char Arrow = '\u2192';

    public static string FormatStateSnapshot(GameState state, GameState? previous = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sb = new StringBuilder();
        sb.AppendLine($"## Tick {state.Tick}");
        sb.AppendLine();

        var nodes = state.Graph.Nodes.Keys
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        for (var i = 0; i < nodes.Length; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            AppendNodeBlock(sb, state, previous, nodes[i]);
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

    private static void AppendNodeBlock(
        StringBuilder sb,
        GameState state,
        GameState? previous,
        NodeId nodeId)
    {
        sb.AppendLine($"{nodeId.Value}:");

        var node = state.Graph.Nodes[nodeId];
        var nodeType = state.Catalog.Get(node.Type);
        var ports = nodeType.Inputs.Keys
            .Concat(nodeType.Outputs.Keys)
            .Distinct()
            .OrderBy(p => p.Value, StringComparer.Ordinal);

        foreach (var portId in ports)
        {
            var port = nodeType.Inputs.TryGetValue(portId, out var inputPort)
                ? inputPort
                : nodeType.Outputs[portId];
            AppendPort(sb, state, previous, nodeId, port);
        }

        var progress = FormatRounded(state.NodeProgress.GetValueOrDefault(nodeId, 0));
        if (previous is null)
        {
            sb.AppendLine($"  progress: {progress}");
            return;
        }

        var priorProgress = FormatRounded(previous.NodeProgress.GetValueOrDefault(nodeId, 0));
        sb.AppendLine($"  progress: {FormatChange(priorProgress, progress)}");
    }

    private static void AppendPort(
        StringBuilder sb,
        GameState state,
        GameState? previous,
        NodeId nodeId,
        Port port)
    {
        var key = new PortKey(nodeId, port.Id);
        var current = state.PortSignals.GetValueOrDefault(key);
        var prior = previous?.PortSignals.GetValueOrDefault(key);
        var kind = KindOf(port.Type.Id);

        if (kind == SignalKind.Resource)
        {
            var currentText = FormatResourceLeaf(current);
            if (previous is null)
            {
                sb.AppendLine($"  {port.Id.Value}: {currentText}");
                return;
            }

            var priorText = FormatResourceLeaf(prior);
            sb.AppendLine($"  {port.Id.Value}: {FormatChange(priorText, currentText)}");
            return;
        }

        // Information
        var currentInfo = current as SignalValue.Enchantment;
        var priorInfo = prior as SignalValue.Enchantment;

        if (currentInfo is null && (previous is null || priorInfo is null))
        {
            sb.AppendLine($"  {port.Id.Value}: -");
            return;
        }

        if (currentInfo is null && priorInfo is not null)
        {
            sb.AppendLine($"  {port.Id.Value}:");
            AppendEnchantmentLeaf(sb, "volume", FormatRounded(priorInfo.Volume), "-");
            AppendEnchantmentLeaf(sb, "darkness", FormatRounded(priorInfo.Darkness), "-");
            AppendEnchantmentLeaf(sb, "fallacy", FormatRounded(priorInfo.Fallacy), "-");
            return;
        }

        // current present
        sb.AppendLine($"  {port.Id.Value}:");
        if (previous is null)
        {
            sb.AppendLine($"    volume: {FormatRounded(currentInfo!.Volume)}");
            sb.AppendLine($"    darkness: {FormatRounded(currentInfo.Darkness)}");
            sb.AppendLine($"    fallacy: {FormatRounded(currentInfo.Fallacy)}");
            return;
        }

        var priorVolume = priorInfo is null ? "-" : FormatRounded(priorInfo.Volume);
        var priorDarkness = priorInfo is null ? "-" : FormatRounded(priorInfo.Darkness);
        var priorFallacy = priorInfo is null ? "-" : FormatRounded(priorInfo.Fallacy);
        AppendEnchantmentLeaf(sb, "volume", priorVolume, FormatRounded(currentInfo!.Volume));
        AppendEnchantmentLeaf(sb, "darkness", priorDarkness, FormatRounded(currentInfo.Darkness));
        AppendEnchantmentLeaf(sb, "fallacy", priorFallacy, FormatRounded(currentInfo.Fallacy));
    }

    private static void AppendEnchantmentLeaf(
        StringBuilder sb,
        string name,
        string prior,
        string current) =>
        sb.AppendLine($"    {name}: {FormatChange(prior, current)}");

    private static string FormatResourceLeaf(SignalValue? value) => value switch
    {
        null => "0",
        SignalValue.Money m => FormatRounded(m.Amount),
        _ => throw new InvalidOperationException(
            $"Expected resource money on port, got {value.GetType().Name}."),
    };

    private static string FormatChange(string prior, string current) =>
        prior == current ? current : $"{prior} {Arrow} {current}";

    private static SignalKind KindOf(SignalTypeId typeId)
    {
        if (typeId == SignalTypes.Money)
        {
            return SignalKind.Resource;
        }

        if (typeId == SignalTypes.Enchantment)
        {
            return SignalKind.Information;
        }

        throw new InvalidOperationException($"Unknown signal type for display: {typeId.Value}.");
    }

    private static string FormatRounded(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
}
