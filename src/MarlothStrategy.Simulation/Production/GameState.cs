using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Time;

namespace MarlothStrategy.Simulation.Production;

public sealed record GameState(
    NodeGraph Graph,
    NodeTypeCatalog Catalog,
    ImmutableDictionary<PortKey, SignalValue> PortSignals,
    ImmutableDictionary<ActorId, Actor> Actors,
    ImmutableArray<Assignment> Assignments,
    NodeTypeConfigs NodeConfigs,
    ImmutableDictionary<NodeId, double> NodeProgress,
    ImmutableDictionary<NodeId, int> NodeTimers,
    ImmutableDictionary<NodeId, int> NodeCycles,
    ImmutableArray<PendingMoneyMove> PendingMoneyMoves,
    ImmutableDictionary<string, EnchantmentBlock> EnchantmentBlocks,
    ulong NextUnitId,
    int Tick,
    TimePartitionConfig TimePartitions,
    PayrollRun? ActivePayrollRun = null)
{
    /// <summary>
    /// Lifetime signed resource that passed through a port without resting in
    /// <see cref="PortSignals"/>: money created on an output (`+`) or disbursed off an
    /// input (`-`). Committed stock alone hides throughput on pass-through money ports.
    /// </summary>
    public ImmutableDictionary<PortKey, double> PortFlowTotals { get; init; } =
        ImmutableDictionary<PortKey, double>.Empty;
}
