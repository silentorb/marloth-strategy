using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public sealed record NodeIoRow(
    NodeId NodeId,
    decimal Effort,
    PortId InputPort,
    SignalTypeId InputType,
    int Available,
    int Consumed,
    int Residual,
    PortId OutputPort,
    SignalTypeId OutputType,
    int Produced);

public sealed record ProductionTickResult(
    GameState State,
    ImmutableArray<NodeIoRow> Nodes);
