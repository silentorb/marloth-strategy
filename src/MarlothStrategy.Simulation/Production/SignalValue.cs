using System.Diagnostics.CodeAnalysis;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Strongly typed signal payload: resources (additive) or information (copied/mutated).
/// Money amounts remain floating-point; designs and enchantment properties are discrete counts.
/// </summary>
public abstract record SignalValue(SignalTypeId TypeId)
{
    public abstract SignalKind Kind { get; }

    /// <summary>Resource: money quantity owned on a port. Routing adds piles; sell emits payout; payroll debits treasury.</summary>
    public sealed record Money(double Amount) : SignalValue(SignalTypes.Money)
    {
        public override SignalKind Kind => SignalKind.Resource;

        public Money Add(double delta) => new(Amount + delta);

        public Money WithAmount(double amount) => new(amount);
    }

    /// <summary>Resource: discrete design units owned on a port. Routing adds counts.</summary>
    public sealed record Designs(int Amount) : SignalValue(SignalTypes.Designs)
    {
        public override SignalKind Kind => SignalKind.Resource;

        public Designs Add(int delta) => new(Amount + delta);

        public Designs WithAmount(int amount) => new(amount);
    }

    /// <summary>
    /// Information: a single enchantment block. Copied on route; fan-in combines via
    /// <see cref="EnchantmentOps.TryCombine"/> (the information <c>+</c> operator).
    /// </summary>
    public sealed record Enchantment(EnchantmentBlock Block) : SignalValue(SignalTypes.Enchantment)
    {
        public override SignalKind Kind => SignalKind.Information;

        public int Volume => Block.VolumeCount;

        public int Darkness => Block.DarknessCount;

        public int Fallacy => Block.FallacyCount;

        public string Hash => Block.Hash;

        public double SellPayout(SellNodeConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return Math.Max(config.PayoutFloor, Volume - Fallacy);
        }
    }

    public SignalValue Copy() => this switch
    {
        Money m => new Money(m.Amount),
        Designs d => new Designs(d.Amount),
        Enchantment e => new Enchantment(e.Block),
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
            Designs d when other is Designs o => d.Add(o.Amount),
            _ => throw new InvalidOperationException($"Unsupported resource add for {TypeId}."),
        };
    }

    /// <summary>
    /// Combines same-typed signals (<c>+</c>): money and designs add; enchantment uses
    /// <see cref="EnchantmentOps.TryCombine"/>. Returns <c>false</c> when the set is empty
    /// or enchantment histories are incompatible (destination should be empty).
    /// </summary>
    public static bool TryCombine(
        IReadOnlyList<SignalValue> values,
        IDictionary<string, EnchantmentBlock> blocks,
        [NotNullWhen(true)] out SignalValue? combined)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(blocks);

        combined = null;
        if (values.Count == 0)
        {
            return false;
        }

        var first = values[0];
        ArgumentNullException.ThrowIfNull(first);
        for (var i = 1; i < values.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(values[i]);
            if (values[i].TypeId != first.TypeId)
            {
                throw new InvalidOperationException(
                    $"Cannot combine signal types {first.TypeId} and {values[i].TypeId}.");
            }
        }

        switch (first)
        {
            case Money:
            case Designs:
                SignalValue acc = first;
                for (var i = 1; i < values.Count; i++)
                {
                    acc = acc.AddResource(values[i]);
                }

                combined = acc;
                return true;

            case Enchantment:
                var inputBlocks = new EnchantmentBlock[values.Count];
                for (var i = 0; i < values.Count; i++)
                {
                    inputBlocks[i] = ((Enchantment)values[i]).Block;
                }

                var block = EnchantmentOps.TryCombine(inputBlocks, blocks);
                if (block is null)
                {
                    return false;
                }

                combined = new Enchantment(block);
                return true;

            default:
                throw new InvalidOperationException($"Unknown signal value kind: {first.GetType().Name}.");
        }
    }

    public bool IsEmptyResource => this is Money { Amount: 0 } or Designs { Amount: 0 };
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
    public static readonly SignalTypeId Designs = new("designs");
}
