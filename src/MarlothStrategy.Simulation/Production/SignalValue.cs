using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Strongly typed signal payload. v1: resource quantities; extend with further cases later.
/// </summary>
public abstract record SignalValue(SignalTypeId TypeId)
{
    public sealed record Money(decimal Amount) : SignalValue(SignalTypes.Money)
    {
        public Money Add(decimal delta) => new(Amount + delta);

        public Money WithAmount(decimal amount) => new(amount);
    }

    public sealed record Enchantments(decimal Amount) : SignalValue(SignalTypes.Enchantments)
    {
        public Enchantments Add(decimal delta) => new(Amount + delta);

        public Enchantments WithAmount(decimal amount) => new(amount);
    }

    public decimal Quantity => this switch
    {
        Money m => m.Amount,
        Enchantments e => e.Amount,
        _ => throw new InvalidOperationException($"Unknown signal value kind: {GetType().Name}."),
    };

    public SignalValue WithQuantity(decimal amount) => this switch
    {
        Money => new Money(amount),
        Enchantments => new Enchantments(amount),
        _ => throw new InvalidOperationException($"Unknown signal value kind: {GetType().Name}."),
    };

    public SignalValue AddQuantity(decimal delta) => WithQuantity(Quantity + delta);
}

public static class SignalTypes
{
    public static readonly SignalTypeId Money = new("money");
    public static readonly SignalTypeId Enchantments = new("enchantments");
}
