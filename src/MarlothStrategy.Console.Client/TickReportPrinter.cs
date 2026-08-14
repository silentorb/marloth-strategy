using System.Collections.Immutable;
using System.Globalization;
using MarlothStrategy.Simulation;
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
        int width = PanelLayout.DefaultWidth,
        GameConfig? config = null)
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
        };
        if (config is not null)
        {
            header.Add($"scenario: {config.ScenarioLabel} seed {config.ScenarioSeed}");
        }

        header.Add(FormatActorsLine(state, previous));

        var nodes = state.Graph.Nodes.Keys
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        // Left:right interior = 1:2 so the flow graph has more horizontal room.
        var leftInteriorWidth = PanelLayout.LeftInteriorWidthForTotal(width);
        // WritePadded reserves one cell of left margin inside the column.
        var usableLeftWidth = Math.Max(1, leftInteriorWidth - 1);

        var leftSubpanels = new List<IReadOnlyList<string>>(nodes.Length);
        foreach (var nodeId in nodes)
        {
            leftSubpanels.Add(FormatNodeLines(state, previous, nodeId, usableLeftWidth));
        }

        var rightInteriorWidth = width - leftInteriorWidth - 3;
        var rightLines = FlowGraphPrinter.FormatLines(state, rightInteriorWidth);

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
            $"{e.Block.AbbreviatedHash} {FormatRounded(e.Volume)}/{FormatRounded(e.Darkness)}/{FormatRounded(e.Fallacy)}",
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
        NodeId nodeId,
        int usableWidth)
    {
        var stateLines = FormatNodeStateLines(state, previous, nodeId);
        var assignmentLines = FormatNodeAssignmentLines(state, nodeId);
        return MergeNodeColumns(stateLines, assignmentLines, usableWidth);
    }

    private static List<string> FormatNodeStateLines(
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

    private static List<string> FormatNodeAssignmentLines(GameState state, NodeId nodeId)
    {
        return state.Assignments
            .Where(a => a.NodeId == nodeId)
            .OrderBy(a => a.ActorId.Value, StringComparer.Ordinal)
            .Select(a => $"{a.ActorId.Value} {FormatWeight(a.Weight)}")
            .ToList();
    }

    /// <summary>
    /// Horizontally splits a node subpanel: state on the left, preferred assignments on the right.
    /// </summary>
    private static List<string> MergeNodeColumns(
        IReadOnlyList<string> stateLines,
        IReadOnlyList<string> assignmentLines,
        int usableWidth)
    {
        if (usableWidth < 3)
        {
            // Too narrow for a split — prefer state content.
            return stateLines.ToList();
        }

        // Right column sized for short "actor weight" rows; prefer room for state leaves.
        var assignWidth = Math.Clamp(usableWidth / 4, 8, 10);
        var stateWidth = usableWidth - 1 - assignWidth;
        var rowCount = Math.Max(1, Math.Max(stateLines.Count, assignmentLines.Count));
        var merged = new List<string>(rowCount);

        for (var i = 0; i < rowCount; i++)
        {
            var left = i < stateLines.Count ? stateLines[i] : string.Empty;
            var right = i < assignmentLines.Count ? assignmentLines[i] : string.Empty;
            merged.Add(
                $"{ClipPad(left, stateWidth)}{BoxDrawing.SingleVertical}{ClipPad(right, assignWidth)}");
        }

        return merged;
    }

    private static string ClipPad(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (text.Length > width)
        {
            return text[..width];
        }

        return text.PadRight(width);
    }

    private static string FormatWeight(decimal weight)
    {
        // Relative ratios: show whole numbers without a trailing ".0".
        if (weight == decimal.Truncate(weight))
        {
            return decimal.Truncate(weight).ToString(CultureInfo.InvariantCulture);
        }

        return weight.ToString(CultureInfo.InvariantCulture);
    }

    private static bool ShowsProgress(NodeTypeId typeId) =>
        typeId == MagicAgencySeed.EnchantTypeId
        || typeId == MagicAgencySeed.TestingTypeId
        || typeId == MagicAgencySeed.SellTypeId
        || typeId == MagicAgencySeed.TreasuryTypeId
        || typeId == MagicAgencySeed.PayrollTypeId
        || typeId == MagicAgencySeed.MergeTypeId;

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
            AppendEnchantmentLeaf(lines, "hash", priorInfo.Block.AbbreviatedHash, "0");
            AppendEnchantmentLeaf(lines, "volume", FormatRounded(priorInfo.Volume), "0");
            AppendEnchantmentLeaf(lines, "darkness", FormatRounded(priorInfo.Darkness), "0");
            AppendEnchantmentLeaf(lines, "fallacy", FormatRounded(priorInfo.Fallacy), "0");
            return;
        }

        // current present
        lines.Add($"  {port.Id.Value}:");
        if (previous is null)
        {
            lines.Add($"    hash: {currentInfo!.Block.AbbreviatedHash}");
            lines.Add($"    volume: {FormatRounded(currentInfo.Volume)}");
            lines.Add($"    darkness: {FormatRounded(currentInfo.Darkness)}");
            lines.Add($"    fallacy: {FormatRounded(currentInfo.Fallacy)}");
            return;
        }

        var priorHash = priorInfo is null ? "0" : priorInfo.Block.AbbreviatedHash;
        var priorVolume = priorInfo is null ? "0" : FormatRounded(priorInfo.Volume);
        var priorDarkness = priorInfo is null ? "0" : FormatRounded(priorInfo.Darkness);
        var priorFallacy = priorInfo is null ? "0" : FormatRounded(priorInfo.Fallacy);
        AppendEnchantmentLeaf(lines, "hash", priorHash, currentInfo!.Block.AbbreviatedHash);
        AppendEnchantmentLeaf(lines, "volume", priorVolume, FormatRounded(currentInfo.Volume));
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
