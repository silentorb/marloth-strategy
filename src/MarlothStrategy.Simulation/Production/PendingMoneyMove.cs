using System.Collections.Immutable;

namespace MarlothStrategy.Simulation.Production;

public enum MoneyMoveDirection
{
    In,
    Out,
}

/// <summary>One pending treasury money move (FIFO on <see cref="GameState.PendingMoneyMoves"/>).</summary>
public sealed record PendingMoneyMove(
    MoneyMoveDirection Direction,
    double Amount,
    int? PayrollRunPeriodIndex = null,
    ImmutableArray<PayrollObligation>? Payees = null);
