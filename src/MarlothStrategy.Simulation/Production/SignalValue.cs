using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Strongly typed signal payload: resources (additive) or information (copied/mutated).
/// </summary>
public abstract record SignalValue(SignalTypeId TypeId)
{
    public abstract SignalKind Kind { get; }

    /// <summary>Resource: money quantity. Added and subtracted when routed or consumed.</summary>
    public sealed record Money(int Amount) : SignalValue(SignalTypes.Money)
    {
        public override SignalKind Kind => SignalKind.Resource;

        public Money Add(int delta) => new(Amount + delta);

        public Money WithAmount(int amount) => new(amount);
    }

    /// <summary>
    /// Information: a single enchantment. Copied on route and mutated by nodes (not additively merged).
    /// </summary>
    public sealed record Enchantment(int Volume, int Darkness, int Fallacy)
        : SignalValue(SignalTypes.Enchantment)
    {
        public override SignalKind Kind => SignalKind.Information;

        public Enchantment Mutate() => new(
            Volume + 10,
            Darkness + 1,
            Fallacy + Darkness + 1);

        public int SellPayout() => Math.Max(0, Volume - Fallacy);
    }

    public SignalValue Copy() => this switch
    {
        Money m => new Money(m.Amount),
        Enchantment e => new Enchantment(e.Volume, e.Darkness, e.Fallacy),
        _ => throw new InvalidOperationException($"Unknown signal value kind: {GetType().Name}."),
    };

    public SignalValue AddResource(SignalValue other)
    {
        if (Kind != SignalKind.Resource || other.Kind != SignalKind.Resource)
        {
            throw new InvalidOperationException("Only resource signals can be added.");
        }

        if (TypeId != other.TypeId)
        {
            throw new InvalidOperationException(
                $"Cannot add resource types {TypeId} and {other.TypeId}.");
        }

        return this switch
        {
            Money m when other is Money o => m.Add(o.Amount),
            _ => throw new InvalidOperationException($"Unsupported resource add for {TypeId}."),
        };
    }

    public bool IsEmptyResource => this is Money { Amount: 0 };
}

public enum SignalKind
{
    Resource,
    Information,
}

public static class SignalTypes
{
    public static readonly SignalTypeId Money = new("money");
    public static readonly SignalTypeId Enchantment = new("enchantment");
}
