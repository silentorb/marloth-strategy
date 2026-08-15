using System.Collections.Immutable;
using System.Globalization;
using MarlothStrategy.Simulation;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Console.Client;

public static class TickReportPrinter
{
    private const char Arrow = '\u2192';
    private const string DeltaCaption = "\u0394";
    public const string Title = "Marloth Strategy";

    private readonly record struct NodeStateRow(string Text, string Delta);

    public static string FormatScreen(
        GameState state,
        GameState? previous = null,
        ProductionTickResult? tick = null,
        int width = PanelLayout.DefaultWidth,
        GameConfig? config = null,
        GameState? baseline = null)
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

        // Left:right interior = 1:1 bottom split.
        var leftInteriorWidth = PanelLayout.LeftInteriorWidthForTotal(width);
        // WritePadded reserves one cell of left margin inside the column.
        var usableLeftWidth = Math.Max(1, leftInteriorWidth - 1);

        var leftSubpanels = new List<IReadOnlyList<string>>(nodes.Length);
        foreach (var nodeId in nodes)
        {
            leftSubpanels.Add(FormatNodeLines(state, previous, baseline, nodeId, usableLeftWidth));
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
            $"{e.Block.AbbreviatedHash} {FormatRounded(e.Volume)}/{FormatRounded(e.Designs)}/{FormatScalar(e.Darkness)}/{FormatScalar(e.Fallacy)}",
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
        GameState? baseline,
        NodeId nodeId,
        int usableWidth)
    {
        var stateRows = FormatNodeStateRows(state, previous, baseline, nodeId);
        var assignmentLines = FormatNodeAssignmentLines(state, nodeId);
        return MergeNodeColumns(stateRows, assignmentLines, usableWidth, includeDelta: baseline is not null);
    }

    private static List<NodeStateRow> FormatNodeStateRows(
        GameState state,
        GameState? previous,
        GameState? baseline,
        NodeId nodeId)
    {
        var rows = new List<NodeStateRow>
        {
            new($"{nodeId.Value}:", baseline is null ? string.Empty : DeltaCaption),
        };

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
            AppendPort(rows, state, previous, baseline, nodeId, port);
        }

        if (!ShowsCycles(node.Type))
        {
            return rows;
        }

        var cycles = FormatInt(state.NodeCycles.GetValueOrDefault(nodeId, 0));
        if (previous is null)
        {
            rows.Add(new($"  cycles: {cycles}", string.Empty));
            return rows;
        }

        var priorCycles = FormatInt(previous.NodeCycles.GetValueOrDefault(nodeId, 0));
        rows.Add(new($"  cycles: {FormatChange(priorCycles, cycles)}", string.Empty));
        return rows;
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
    /// Horizontally splits a node subpanel: state | optional Δ | preferred assignments.
    /// </summary>
    private static List<string> MergeNodeColumns(
        IReadOnlyList<NodeStateRow> stateRows,
        IReadOnlyList<string> assignmentLines,
        int usableWidth,
        bool includeDelta)
    {
        if (usableWidth < 3)
        {
            // Too narrow for a split — prefer state content.
            return stateRows.Select(r => r.Text).ToList();
        }

        // Right column sized for short "actor weight" rows; prefer room for state leaves.
        var assignWidth = Math.Clamp(usableWidth / 4, 8, 10);
        if (!includeDelta || usableWidth < assignWidth + 1 + 6 + 1 + 8)
        {
            // Not enough room for three columns — fall back to state | assignments.
            var stateWidthTwo = usableWidth - 1 - assignWidth;
            return MergeTwoColumns(
                stateRows.Select(r => r.Text).ToList(),
                assignmentLines,
                stateWidthTwo,
                assignWidth);
        }

        var deltaWidth = Math.Clamp(usableWidth / 8, 6, 8);
        var stateWidth = usableWidth - 1 - deltaWidth - 1 - assignWidth;
        var rowCount = Math.Max(1, Math.Max(stateRows.Count, assignmentLines.Count));
        var merged = new List<string>(rowCount);

        for (var i = 0; i < rowCount; i++)
        {
            var left = i < stateRows.Count ? stateRows[i].Text : string.Empty;
            var mid = i < stateRows.Count ? stateRows[i].Delta : string.Empty;
            var right = i < assignmentLines.Count ? assignmentLines[i] : string.Empty;
            merged.Add(
                $"{ClipPad(left, stateWidth)}{BoxDrawing.SingleVertical}" +
                $"{ClipPad(mid, deltaWidth)}{BoxDrawing.SingleVertical}" +
                $"{ClipPad(right, assignWidth)}");
        }

        return merged;
    }

    private static List<string> MergeTwoColumns(
        IReadOnlyList<string> stateLines,
        IReadOnlyList<string> assignmentLines,
        int stateWidth,
        int assignWidth)
    {
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

    private static bool ShowsCycles(NodeTypeId typeId) =>
        typeId == MagicAgencySeed.EnchantTypeId
        || typeId == MagicAgencySeed.TestingTypeId
        || typeId == MagicAgencySeed.DesignTypeId
        || typeId == MagicAgencySeed.SellTypeId
        || typeId == MagicAgencySeed.TreasuryTypeId
        || typeId == MagicAgencySeed.PayrollTypeId
        || typeId == MagicAgencySeed.MergeTypeId;

    private static void AppendPort(
        List<NodeStateRow> rows,
        GameState state,
        GameState? previous,
        GameState? baseline,
        NodeId nodeId,
        Port port)
    {
        var key = new PortKey(nodeId, port.Id);
        var current = state.PortSignals.GetValueOrDefault(key);
        var prior = previous?.PortSignals.GetValueOrDefault(key);
        var baseValue = baseline?.PortSignals.GetValueOrDefault(key);
        var kind = KindOf(port.Type.Id);

        if (kind == SignalKind.Resource)
        {
            var currentText = FormatResourceLeaf(current);
            var delta = baseline is null
                ? string.Empty
                : FormatSignedDelta(RoundedResource(baseValue), RoundedResource(current));
            if (previous is null)
            {
                rows.Add(new($"  {port.Id.Value}: {currentText}", delta));
                return;
            }

            var priorText = FormatResourceLeaf(prior);
            rows.Add(new($"  {port.Id.Value}: {FormatChange(priorText, currentText)}", delta));
            return;
        }

        // Information — empty stock displays as 0 (same sentinel as money).
        var currentInfo = current as SignalValue.Enchantment;
        var priorInfo = prior as SignalValue.Enchantment;
        var baseInfo = baseValue as SignalValue.Enchantment;

        if (currentInfo is null && (previous is null || priorInfo is null))
        {
            rows.Add(new($"  {port.Id.Value}: 0", string.Empty));
            return;
        }

        if (currentInfo is null && priorInfo is not null)
        {
            rows.Add(new($"  {port.Id.Value}:", string.Empty));
            AppendEnchantmentLeaf(rows, "hash", priorInfo.Block.AbbreviatedHash, "0", string.Empty);
            AppendEnchantmentLeaf(
                rows,
                "volume",
                FormatRounded(priorInfo.Volume),
                "0",
                FormatCountDelta(baseline, baseInfo?.Volume ?? 0, 0));
            AppendEnchantmentLeaf(
                rows,
                "designs",
                FormatRounded(priorInfo.Designs),
                "0",
                FormatCountDelta(baseline, baseInfo?.Designs ?? 0, 0));
            AppendEnchantmentLeaf(
                rows,
                "darkness",
                FormatScalar(priorInfo.Darkness),
                "0",
                FormatScalarDelta(baseline, baseInfo?.Darkness ?? 0, 0));
            AppendEnchantmentLeaf(
                rows,
                "fallacy",
                FormatScalar(priorInfo.Fallacy),
                "0",
                FormatScalarDelta(baseline, baseInfo?.Fallacy ?? 0, 0));
            return;
        }

        // current present
        rows.Add(new($"  {port.Id.Value}:", string.Empty));
        if (previous is null)
        {
            rows.Add(new($"    hash: {currentInfo!.Block.AbbreviatedHash}", string.Empty));
            rows.Add(new(
                $"    volume: {FormatRounded(currentInfo.Volume)}",
                FormatCountDelta(baseline, baseInfo?.Volume ?? 0, currentInfo.Volume)));
            rows.Add(new(
                $"    designs: {FormatRounded(currentInfo.Designs)}",
                FormatCountDelta(baseline, baseInfo?.Designs ?? 0, currentInfo.Designs)));
            rows.Add(new(
                $"    darkness: {FormatScalar(currentInfo.Darkness)}",
                FormatScalarDelta(baseline, baseInfo?.Darkness ?? 0, currentInfo.Darkness)));
            rows.Add(new(
                $"    fallacy: {FormatScalar(currentInfo.Fallacy)}",
                FormatScalarDelta(baseline, baseInfo?.Fallacy ?? 0, currentInfo.Fallacy)));
            return;
        }

        var priorHash = priorInfo is null ? "0" : priorInfo.Block.AbbreviatedHash;
        var priorVolume = priorInfo is null ? "0" : FormatRounded(priorInfo.Volume);
        var priorDesigns = priorInfo is null ? "0" : FormatRounded(priorInfo.Designs);
        var priorDarkness = priorInfo is null ? "0" : FormatScalar(priorInfo.Darkness);
        var priorFallacy = priorInfo is null ? "0" : FormatScalar(priorInfo.Fallacy);
        AppendEnchantmentLeaf(
            rows,
            "hash",
            priorHash,
            currentInfo!.Block.AbbreviatedHash,
            string.Empty);
        AppendEnchantmentLeaf(
            rows,
            "volume",
            priorVolume,
            FormatRounded(currentInfo.Volume),
            FormatCountDelta(baseline, baseInfo?.Volume ?? 0, currentInfo.Volume));
        AppendEnchantmentLeaf(
            rows,
            "designs",
            priorDesigns,
            FormatRounded(currentInfo.Designs),
            FormatCountDelta(baseline, baseInfo?.Designs ?? 0, currentInfo.Designs));
        AppendEnchantmentLeaf(
            rows,
            "darkness",
            priorDarkness,
            FormatScalar(currentInfo.Darkness),
            FormatScalarDelta(baseline, baseInfo?.Darkness ?? 0, currentInfo.Darkness));
        AppendEnchantmentLeaf(
            rows,
            "fallacy",
            priorFallacy,
            FormatScalar(currentInfo.Fallacy),
            FormatScalarDelta(baseline, baseInfo?.Fallacy ?? 0, currentInfo.Fallacy));
    }

    private static void AppendEnchantmentLeaf(
        List<NodeStateRow> rows,
        string name,
        string prior,
        string current,
        string delta) =>
        rows.Add(new($"    {name}: {FormatChange(prior, current)}", delta));

    private static string FormatCountDelta(GameState? baseline, double baseAmount, double currentAmount) =>
        baseline is null ? string.Empty : FormatSignedDelta(RoundAway(baseAmount), RoundAway(currentAmount));

    private static string FormatScalarDelta(GameState? baseline, double baseAmount, double currentAmount)
    {
        if (baseline is null)
        {
            return string.Empty;
        }

        var delta = currentAmount - baseAmount;
        if (Math.Abs(delta) < 1e-9)
        {
            return "0";
        }

        var text = FormatScalar(Math.Abs(delta));
        return delta > 0 ? $"+{text}" : $"-{text}";
    }

    private static long RoundedResource(SignalValue? value) => value switch
    {
        null => 0,
        SignalValue.Money m => RoundAway(m.Amount),
        _ => throw new InvalidOperationException(
            $"Expected resource money on port, got {value.GetType().Name}."),
    };

    private static string FormatSignedDelta(long baseline, long current)
    {
        var delta = current - baseline;
        if (delta == 0)
        {
            return "0";
        }

        return delta > 0
            ? $"+{delta.ToString(CultureInfo.InvariantCulture)}"
            : delta.ToString(CultureInfo.InvariantCulture);
    }

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

    private static long RoundAway(double value) =>
        (long)Math.Round(value, MidpointRounding.AwayFromZero);

    private static string FormatRounded(double value) =>
        RoundAway(value).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats floating-point enchantment scalars with trimmed fractional digits
    /// so low amounts remain visible (unlike integer rounding).
    /// </summary>
    private static string FormatScalar(double value)
    {
        if (Math.Abs(value) < 1e-12)
        {
            return "0";
        }

        var rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string FormatInt(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
