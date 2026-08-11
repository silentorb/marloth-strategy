namespace MarlothStrategy.Simulation.Graph;

/// <summary>
/// Graph node instance. Port templates live on the catalog <see cref="NodeType"/>;
/// runtime stocks live in <c>GameState.PortSignals</c>.
/// </summary>
public sealed record Node(NodeId Id, NodeTypeId Type);
