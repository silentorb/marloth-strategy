using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public sealed record GameState(
    NodeGraph Graph,
    NodeTypeCatalog Catalog,
    ImmutableDictionary<PortKey, SignalValue> PortSignals,
    ImmutableDictionary<ActorId, Actor> Actors,
    ImmutableArray<Assignment> Assignments,
    int Tick);
