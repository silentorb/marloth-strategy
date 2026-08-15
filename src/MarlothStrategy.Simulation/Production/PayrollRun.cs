using System.Collections.Immutable;

namespace MarlothStrategy.Simulation.Production;

/// <summary>One actor wage obligation captured when a payroll run opens.</summary>
public sealed record PayrollObligation(ActorId ActorId, double Wage);

/// <summary>
/// Active monthly payroll run: snapshot of owed wages, paid recipients, and whether
/// the single payroll-node attempt has been submitted.
/// </summary>
public sealed record PayrollRun(
    int PeriodIndex,
    ImmutableArray<PayrollObligation> Obligations,
    ImmutableHashSet<ActorId> PaidActorIds,
    bool AttemptSubmitted)
{
    public IEnumerable<PayrollObligation> UnpaidObligations() =>
        Obligations.Where(o => !PaidActorIds.Contains(o.ActorId));

    public double WageTotal() => Obligations.Sum(o => o.Wage);

    public PayrollRun WithPaid(IEnumerable<ActorId> newlyPaid)
    {
        ArgumentNullException.ThrowIfNull(newlyPaid);
        var paid = PaidActorIds.ToBuilder();
        foreach (var id in newlyPaid)
        {
            paid.Add(id);
        }

        return this with { PaidActorIds = paid.ToImmutable() };
    }

    public PayrollRun WithAttemptSubmitted() => this with { AttemptSubmitted = true };
}
