using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Per-node primary process I/O for one tick. Available / residual / produced are typed signals
/// (null = empty stock / no output).
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
    SignalValue? Produced);

public sealed record ProductionTickResult(
    GameState State,
    ImmutableArray<NodeIoRow> Nodes);
