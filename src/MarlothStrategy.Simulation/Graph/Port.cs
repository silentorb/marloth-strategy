namespace MarlothStrategy.Simulation.Graph;

public sealed record SignalType(SignalTypeId Id);

public sealed record Port(PortId Id, SignalType Type);

public sealed record PortReference(NodeId Node, PortId Port);
