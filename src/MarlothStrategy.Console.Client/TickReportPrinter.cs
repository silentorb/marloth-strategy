using System.Collections.Immutable;
using System.Globalization;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

public static class TickReportPrinter
{
    private const char Arrow = '\u2192';
    public const string Title = "Marloth Strategy";

    public static string FormatScreen(
        GameState state,
        GameState? previous = null,
        ProductionTickResult? tick = null,
        int width = PanelLayout.DefaultWidth)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ = tick;

        if (width < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "width must be at least 10.");
        }

        var header = new List<string>
        {
            Title,
            $"Tick {state.Tick}",
            FormatActorsLine(state, previous),
        };

        var nodes = state.Graph.Nodes.Keys
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        var leftSubpanels = new List<IReadOnlyList<string>>(nodes.Length);
        foreach (var nodeId in nodes)
        {
            leftSubpanels.Add(FormatNodeLines(state, previous, nodeId));
        }

        var rightLines = FlowGraphPrinter.FormatLines(state);

        // Prefer a wider left column for state text; remaining width for the graph.
        var leftInteriorWidth = Math.Max(24, (width * 5) / 10 - 2);
        var maxLeft = width - 5;
        if (leftInteriorWidth > maxLeft)
        {
            leftInteriorWidth = maxLeft;
        }

        return PanelLayout.Compose(header, leftSubpanels, rightLines, width, leftInteriorWidth);
    }

    /// <summary>Legacy name for the full panel screen (same as <see cref="FormatScreen"/>).</summary>
    public static string FormatStateSnapshot(
        GameState state,
        GameState? previous = null,
        ProductionTickResult? tick = null) =>
        FormatScreen(state, previous, tick);

    public static string FormatSignal(SignalValue? value) => value switch
    {
        null => "0",
        SignalValue.Money m => FormatRounded(m.Amount),
        SignalValue.Enchantment e =>
            $"{FormatRounded(e.Volume)}/{FormatRounded(e.Darkness)}/{FormatRounded(e.Fallacy)}",
        _ => throw new InvalidOperationException($"Unknown signal value kind: {value.GetType().Name}."),
    };

    private static string FormatActorsLine(GameState state, GameState? previous)
    {
        var current = FormatActorRoster(state.Actors);
        if (previous is null)
        {
            return $"actors: {current}";
        }

        var prior = FormatActorRoster(previous.Actors);
        return $"actors: {FormatChange(prior, current)}";
    }

    private static string FormatActorRoster(ImmutableDictionary<ActorId, Actor> actors)
    {
        if (actors.IsEmpty)
        {
            return "0";
        }

        return string.Join(
            ", ",
            actors.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).Select(id => id.Value));
    }

    private static List<string> FormatNodeLines(
        GameState state,
        GameState? previous,
        NodeId nodeId)
    {
        var lines = new List<string> { $"{nodeId.Value}:" };

        var node = state.Graph.Nodes[nodeId];
        var nodeType = state.Catalog.Get(node.Type);
        // Same-named input/output ports share one PortSignals stock and one display entry.
        var ports = nodeType.Inputs.Keys
            .Concat(nodeType.Outputs.Keys)
            .Distinct()
            .OrderBy(p => p.Value, StringComparer.Ordinal);

        foreach (var portId in ports)
        {
            var port = nodeType.Inputs.TryGetValue(portId, out var inputPort)
                ? inputPort
                : nodeType.Outputs[portId];
            AppendPort(lines, state, previous, nodeId, port);
        }

        if (ShowsTimer(node.Type))
        {
            var timer = FormatInt(state.NodeTimers.GetValueOrDefault(nodeId, 0));
            if (previous is null)
            {
                lines.Add($"  timer: {timer}");
            }
            else
            {
                var priorTimer = FormatInt(previous.NodeTimers.GetValueOrDefault(nodeId, 0));
                lines.Add($"  timer: {FormatChange(priorTimer, timer)}");
            }
        }

        if (!ShowsProgress(node.Type))
        {
            return lines;
        }

        var progress = FormatRounded(state.NodeProgress.GetValueOrDefault(nodeId, 0));
        if (previous is null)
        {
            lines.Add($"  progress: {progress}");
            return lines;
        }

        var priorProgress = FormatRounded(previous.NodeProgress.GetValueOrDefault(nodeId, 0));
        lines.Add($"  progress: {FormatChange(priorProgress, progress)}");
        return lines;
    }

    private static bool ShowsProgress(NodeTypeId typeId) =>
        typeId == MagicAgencySeed.EnchantTypeId
        || typeId == MagicAgencySeed.TestingTypeId
        || typeId == MagicAgencySeed.SellTypeId
        || typeId == MagicAgencySeed.TreasuryTypeId
        || typeId == MagicAgencySeed.PayrollTypeId;

    private static bool ShowsTimer(NodeTypeId typeId) =>
        typeId == MagicAgencySeed.PayrollTypeId;

    private static void AppendPort(
        List<string> lines,
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
                lines.Add($"  {port.Id.Value}: {currentText}");
                return;
            }

            var priorText = FormatResourceLeaf(prior);
            lines.Add($"  {port.Id.Value}: {FormatChange(priorText, currentText)}");
            return;
        }

        // Information — empty stock displays as 0 (same sentinel as money).
        var currentInfo = current as SignalValue.Enchantment;
        var priorInfo = prior as SignalValue.Enchantment;

        if (currentInfo is null && (previous is null || priorInfo is null))
        {
            lines.Add($"  {port.Id.Value}: 0");
            return;
        }

        if (currentInfo is null && priorInfo is not null)
        {
            lines.Add($"  {port.Id.Value}:");
            AppendEnchantmentLeaf(lines, "volume", FormatRounded(priorInfo.Volume), "0");
            AppendEnchantmentLeaf(lines, "darkness", FormatRounded(priorInfo.Darkness), "0");
            AppendEnchantmentLeaf(lines, "fallacy", FormatRounded(priorInfo.Fallacy), "0");
            return;
        }

        // current present
        lines.Add($"  {port.Id.Value}:");
        if (previous is null)
        {
            lines.Add($"    volume: {FormatRounded(currentInfo!.Volume)}");
            lines.Add($"    darkness: {FormatRounded(currentInfo.Darkness)}");
            lines.Add($"    fallacy: {FormatRounded(currentInfo.Fallacy)}");
            return;
        }

        var priorVolume = priorInfo is null ? "0" : FormatRounded(priorInfo.Volume);
        var priorDarkness = priorInfo is null ? "0" : FormatRounded(priorInfo.Darkness);
        var priorFallacy = priorInfo is null ? "0" : FormatRounded(priorInfo.Fallacy);
        AppendEnchantmentLeaf(lines, "volume", priorVolume, FormatRounded(currentInfo!.Volume));
        AppendEnchantmentLeaf(lines, "darkness", priorDarkness, FormatRounded(currentInfo.Darkness));
        AppendEnchantmentLeaf(lines, "fallacy", priorFallacy, FormatRounded(currentInfo.Fallacy));
    }

    private static void AppendEnchantmentLeaf(
        List<string> lines,
        string name,
        string prior,
        string current) =>
        lines.Add($"    {name}: {FormatChange(prior, current)}");

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

    private static string FormatInt(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
