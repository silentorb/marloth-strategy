using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public sealed record NodeIoRow(
    NodeId NodeId,
    decimal Effort,
    PortId InputPort,
    SignalTypeId InputType,
    decimal Available,
    decimal Consumed,
    decimal Residual,
    PortId OutputPort,
    SignalTypeId OutputType,
    decimal Produced);

public sealed record ProductionTickResult(
    GameState State,
    ImmutableArray<NodeIoRow> Nodes);
