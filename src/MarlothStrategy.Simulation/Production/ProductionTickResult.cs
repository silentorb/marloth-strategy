using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Per-node primary process I/O for one tick. Available / residual / produced are typed signals
/// (null = empty stock / no output). <see cref="MoneyIn"/> / <see cref="MoneyOut"/> describe the
/// continuous money transform through this node (not per-node ownership).
/// </summary>
public sealed record NodeIoRow(
    NodeId NodeId,
    decimal Effort,
    PortId InputPort,
    SignalTypeId InputType,
    SignalValue? Available,
    bool Consumed,
    SignalValue? Residual,
    PortId OutputPort,
    SignalTypeId OutputType,
    SignalValue? Produced,
    double? MoneyIn = null,
    double? MoneyOut = null);

public sealed record ProductionTickResult(
    GameState State,
    ImmutableArray<NodeIoRow> Nodes);
