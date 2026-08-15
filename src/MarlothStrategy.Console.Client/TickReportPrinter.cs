using System.Globalization;
using MarlothStrategy.Simulation;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;
using MarlothStrategy.Simulation.Time;

namespace MarlothStrategy.Console.Client;

public static class TickReportPrinter
{
    private const char Arrow = '\u2192';
    private const string DeltaCaption = "\u0394";

    /// <summary>Cells reserved for values; long property names clip before values do.</summary>
    private const int MinValueWidth = 8;

    public const string Title = "Marloth Strategy";

    /// <summary>One body row: indented property name, its value, and the optional Δ cell.</summary>
    private readonly record struct NodeStateRow(string Key, string Value, string Delta);

    /// <summary>Column metrics shared by every node subpanel so cells align down the screen.</summary>
    private readonly record struct ColumnMetrics(int LongestKey, int ValuePrefix, int DeltaPrefix);

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

        var header = StatusHeader.Build(state, previous, config, ScreenId.Workflow);

        var nodes = state.Graph.Nodes.Keys
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();

        // Left:right interior = 1:1 bottom split.
        var leftInteriorWidth = PanelLayout.LeftInteriorWidthForTotal(width);
        // WritePadded reserves one cell of left margin inside the column.
        var usableLeftWidth = Math.Max(1, leftInteriorWidth - 1);

        var bodies = nodes
            .Select(id => (
                Id: id,
                Rows: FormatNodeStateRows(state, previous, baseline, id),
                Assignments: FormatNodeAssignmentLines(state, id)))
            .ToArray();

        // Metrics span every node subpanel so columns line up down the whole left column.
        var allRows = bodies.SelectMany(b => b.Rows).ToArray();
        var metrics = new ColumnMetrics(
            PanelColumns.LongestKey(allRows.Select(r => r.Key)),
            PanelColumns.NumericPrefixWidth(allRows.Select(r => r.Value)),
            PanelColumns.NumericPrefixWidth(allRows.Select(r => r.Delta)));

        var leftSubpanels = new List<PanelSubpanel>(nodes.Length);
        foreach (var body in bodies)
        {
            var lines = MergeNodeColumns(
                body.Rows,
                body.Assignments,
                usableLeftWidth,
                includeDelta: baseline is not null,
                metrics);
            leftSubpanels.Add(new PanelSubpanel(body.Id.Value, lines));
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

    public static string FormatCalendarLine(TimePartitionConfig timePartitions, int tick)
    {
        ArgumentNullException.ThrowIfNull(timePartitions);
        var positions = timePartitions.PositionsAt(tick);
        // Largest unit first for a calendar-like read (month … day).
        return string.Join(
            ", ",
            positions.Reverse().Select(FormatPosition));
    }

    public static string FormatSignal(SignalValue? value) => value switch
    {
        null => "0",
        SignalValue.Money m => FormatRounded(m.Amount),
        SignalValue.Enchantment e =>
            $"{e.Block.AbbreviatedHash} {FormatRounded(e.Volume)}/{FormatRounded(e.Designs)}/{FormatScalar(e.Darkness)}/{FormatScalar(e.Fallacy)}",
        _ => throw new InvalidOperationException($"Unknown signal value kind: {value.GetType().Name}."),
    };

    private static string FormatPosition(TimePartitionPosition position)
    {
        if (position.OfParent is int ofParent)
        {
            return $"{position.Name} {position.Index}/{ofParent}";
        }

        return $"{position.Name} {position.Index}";
    }

    private static List<NodeStateRow> FormatNodeStateRows(
        GameState state,
        GameState? previous,
        GameState? baseline,
        NodeId nodeId)
    {
        var rows = new List<NodeStateRow>();
        if (baseline is not null)
        {
            // Column header for Δ; node id lives in the subpanel title rule.
            rows.Add(new(string.Empty, string.Empty, DeltaCaption));
        }

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
            rows.Add(new("  cycles:", cycles, string.Empty));
            return rows;
        }

        var priorCycles = FormatInt(previous.NodeCycles.GetValueOrDefault(nodeId, 0));
        rows.Add(new("  cycles:", FormatChange(priorCycles, cycles), string.Empty));
        return rows;
    }

    private static List<string> FormatNodeAssignmentLines(GameState state, NodeId nodeId)
    {
        return state.Assignments
            .Where(a => a.NodeId == nodeId)
            .OrderBy(a => a.ActorId.Value, StringComparer.Ordinal)
            .Select(a => $"{a.ActorId.Value} {DisplayFormatting.FormatDecimal(a.Weight)}")
            .ToList();
    }

    /// <summary>
    /// Horizontally splits a node subpanel: property name | value | optional Δ | preferred assignments.
    /// </summary>
    private static List<string> MergeNodeColumns(
        IReadOnlyList<NodeStateRow> stateRows,
        IReadOnlyList<string> assignmentLines,
        int usableWidth,
        bool includeDelta,
        ColumnMetrics metrics)
    {
        if (usableWidth < 3)
        {
            // Too narrow for a split — prefer state content.
            return stateRows.Select(FlattenRow).ToList();
        }

        // Right column sized for short "actor weight" rows; prefer room for state leaves.
        var assignWidth = Math.Clamp(usableWidth / 4, 8, 10);
        if (!includeDelta || usableWidth < assignWidth + 1 + 6 + 1 + 8)
        {
            // Not enough room for a Δ column — fall back to name | value | assignments.
            return MergeStateColumns(
                stateRows,
                assignmentLines,
                usableWidth - 1 - assignWidth,
                deltaWidth: 0,
                assignWidth,
                metrics);
        }

        var deltaWidth = Math.Clamp(usableWidth / 8, 6, 8);
        var stateWidth = usableWidth - 1 - deltaWidth - 1 - assignWidth;
        return MergeStateColumns(stateRows, assignmentLines, stateWidth, deltaWidth, assignWidth, metrics);
    }

    private static List<string> MergeStateColumns(
        IReadOnlyList<NodeStateRow> stateRows,
        IReadOnlyList<string> assignmentLines,
        int stateWidth,
        int deltaWidth,
        int assignWidth,
        ColumnMetrics metrics)
    {
        if (stateWidth < 3)
        {
            return stateRows.Select(FlattenRow).ToList();
        }

        var keyWidth = PanelColumns.KeyColumnWidth(metrics.LongestKey, stateWidth, MinValueWidth);
        var valueWidth = stateWidth - 1 - keyWidth;
        var rowCount = Math.Max(1, Math.Max(stateRows.Count, assignmentLines.Count));
        var merged = new List<string>(rowCount);

        for (var i = 0; i < rowCount; i++)
        {
            var key = i < stateRows.Count ? stateRows[i].Key : string.Empty;
            var value = i < stateRows.Count ? stateRows[i].Value : string.Empty;
            var delta = i < stateRows.Count ? stateRows[i].Delta : string.Empty;
            var right = i < assignmentLines.Count ? assignmentLines[i] : string.Empty;

            var line =
                $"{PanelColumns.ClipPad(key, keyWidth)}{BoxDrawing.SingleVertical}" +
                $"{PanelColumns.ClipPad(PanelColumns.AlignNumeric(value, metrics.ValuePrefix), valueWidth)}";
            if (deltaWidth > 0)
            {
                var aligned = PanelColumns.AlignNumeric(delta, metrics.DeltaPrefix);
                line += $"{BoxDrawing.SingleVertical}{PanelColumns.ClipPad(aligned, deltaWidth)}";
            }

            merged.Add($"{line}{BoxDrawing.SingleVertical}{PanelColumns.ClipPad(right, assignWidth)}");
        }

        return merged;
    }

    private static string FlattenRow(NodeStateRow row) =>
        row.Value.Length == 0 ? row.Key : $"{row.Key} {row.Value}";

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
            // Pass-through ports hold no stock, so throughput carries the lifetime signal.
            var delta = baseline is null
                ? string.Empty
                : FormatSignedDelta(
                    RoundedResource(baseValue) + RoundAway(baseline.PortFlowTotals.GetValueOrDefault(key)),
                    RoundedResource(current) + RoundAway(state.PortFlowTotals.GetValueOrDefault(key)));
            if (previous is null)
            {
                rows.Add(new($"  {port.Id.Value}:", currentText, delta));
                return;
            }

            var priorText = FormatResourceLeaf(prior);
            rows.Add(new($"  {port.Id.Value}:", FormatChange(priorText, currentText), delta));
            return;
        }

        // Information — empty stock displays as 0 (same sentinel as money).
        var currentInfo = current as SignalValue.Enchantment;
        var priorInfo = prior as SignalValue.Enchantment;
        var baseInfo = baseValue as SignalValue.Enchantment;

        if (currentInfo is null && (previous is null || priorInfo is null))
        {
            rows.Add(new($"  {port.Id.Value}:", "0", string.Empty));
            return;
        }

        if (currentInfo is null && priorInfo is not null)
        {
            rows.Add(new($"  {port.Id.Value}:", string.Empty, string.Empty));
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
        rows.Add(new($"  {port.Id.Value}:", string.Empty, string.Empty));
        if (previous is null)
        {
            rows.Add(new("    hash:", currentInfo!.Block.AbbreviatedHash, string.Empty));
            rows.Add(new(
                "    volume:",
                FormatRounded(currentInfo.Volume),
                FormatCountDelta(baseline, baseInfo?.Volume ?? 0, currentInfo.Volume)));
            rows.Add(new(
                "    designs:",
                FormatRounded(currentInfo.Designs),
                FormatCountDelta(baseline, baseInfo?.Designs ?? 0, currentInfo.Designs)));
            rows.Add(new(
                "    darkness:",
                FormatScalar(currentInfo.Darkness),
                FormatScalarDelta(baseline, baseInfo?.Darkness ?? 0, currentInfo.Darkness)));
            rows.Add(new(
                "    fallacy:",
                FormatScalar(currentInfo.Fallacy),
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
        rows.Add(new($"    {name}:", FormatChange(prior, current), delta));

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
