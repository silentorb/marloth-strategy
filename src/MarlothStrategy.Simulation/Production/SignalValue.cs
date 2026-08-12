using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Strongly typed signal payload: resources (additive) or information (copied/mutated).
/// Numeric fields are floating-point (<see cref="double"/>).
/// </summary>
public abstract record SignalValue(SignalTypeId TypeId)
{
    public abstract SignalKind Kind { get; }

    /// <summary>Resource: money quantity. Added and subtracted when routed or consumed.</summary>
    public sealed record Money(double Amount) : SignalValue(SignalTypes.Money)
    {
        public override SignalKind Kind => SignalKind.Resource;

        public Money Add(double delta) => new(Amount + delta);

        public Money WithAmount(double amount) => new(amount);
    }

    /// <summary>
    /// Information: a single enchantment. Copied on route and mutated by nodes (not additively merged).
    /// </summary>
    public sealed record Enchantment(double Volume, double Darkness, double Fallacy)
        : SignalValue(SignalTypes.Enchantment)
    {
        public override SignalKind Kind => SignalKind.Information;

        public Enchantment Mutate(EnchantNodeConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return new(
                Volume + config.VolumeDelta,
                Darkness + config.DarknessDelta,
                Fallacy + Darkness + config.FallacyConstant);
        }

        public double SellPayout(SellNodeConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return Math.Max(config.PayoutFloor, Volume - Fallacy);
        }
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
