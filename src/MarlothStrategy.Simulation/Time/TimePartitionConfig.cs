using System.Collections.Immutable;

namespace MarlothStrategy.Simulation.Time;

/// <summary>
/// One link in a nested time hierarchy: <see cref="Name"/> contains
/// <see cref="Contains"/> units named <see cref="Of"/>.
/// </summary>
public sealed record TimePartitionUnit(string Name, int Contains, string Of);

/// <summary>
/// Validated nested calendar over the tick counter. The simulation tick remains
/// the sole mutable clock; this config only labels and measures intervals.
/// </summary>
public sealed class TimePartitionConfig : IEquatable<TimePartitionConfig>
{
    public const string TickUnitName = "tick";

    /// <summary>
    /// Units ordered from smallest (nearest to tick) to largest.
    /// </summary>
    public ImmutableArray<TimePartitionUnit> Units { get; }

    /// <summary>
    /// Named unit used for session macro advance (e.g. Space). Must not be <see cref="TickUnitName"/>.
    /// </summary>
    public string AdvanceUnit { get; }

    /// <summary>
    /// Tick duration of each named unit, including <see cref="TickUnitName"/> = 1.
    /// </summary>
    public ImmutableDictionary<string, int> TicksPerUnit { get; }

    public int AdvanceTickCount => TicksPerUnit[AdvanceUnit];

    internal TimePartitionConfig(
        ImmutableArray<TimePartitionUnit> units,
        string advanceUnit,
        ImmutableDictionary<string, int> ticksPerUnit)
    {
        Units = units;
        AdvanceUnit = advanceUnit;
        TicksPerUnit = ticksPerUnit;
    }

    /// <summary>
    /// One-based position of <paramref name="tick"/> within each configured unit.
    /// Within a parent (when present), the index is modulo that parent's child count.
    /// At tick 0 every unit is at position 1.
    /// </summary>
    public ImmutableArray<TimePartitionPosition> PositionsAt(int tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick must be non-negative.");
        }

        var builder = ImmutableArray.CreateBuilder<TimePartitionPosition>(Units.Length);
        for (var i = 0; i < Units.Length; i++)
        {
            var unit = Units[i];
            var duration = TicksPerUnit[unit.Name];
            var absoluteIndex = tick / duration;
            int position;
            int? ofParent;
            if (i + 1 < Units.Length)
            {
                var parent = Units[i + 1];
                ofParent = parent.Contains;
                position = (absoluteIndex % parent.Contains) + 1;
            }
            else
            {
                // Largest unit: unbounded absolute one-based index.
                ofParent = null;
                position = absoluteIndex + 1;
            }

            builder.Add(new TimePartitionPosition(unit.Name, position, ofParent));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Named units (smallest → largest) whose absolute index increases over
    /// (<paramref name="fromTick"/>, <paramref name="toTick"/>]. Empty when the range is empty
    /// or no boundary is crossed. Does not include the leaf <c>tick</c> name.
    /// </summary>
    public ImmutableArray<string> BoundariesCrossed(int fromTick, int toTick)
    {
        if (fromTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fromTick), fromTick, "fromTick must be non-negative.");
        }

        if (toTick < fromTick)
        {
            throw new ArgumentOutOfRangeException(nameof(toTick), toTick, "toTick must be >= fromTick.");
        }

        if (toTick == fromTick)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var unit in Units)
        {
            var duration = TicksPerUnit[unit.Name];
            if (toTick / duration > fromTick / duration)
            {
                builder.Add(unit.Name);
            }
        }

        return builder.ToImmutable();
    }

    public int TicksPer(string unitName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitName);
        if (!TicksPerUnit.TryGetValue(unitName, out var ticks))
        {
            throw new InvalidOperationException($"Unknown time unit '{unitName}'.");
        }

        return ticks;
    }

    public bool Equals(TimePartitionConfig? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return AdvanceUnit == other.AdvanceUnit
            && Units.SequenceEqual(other.Units)
            && TicksPerUnit.Count == other.TicksPerUnit.Count
            && TicksPerUnit.All(kv =>
                other.TicksPerUnit.TryGetValue(kv.Key, out var ticks) && ticks == kv.Value);
    }

    public override bool Equals(object? obj) => Equals(obj as TimePartitionConfig);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AdvanceUnit, StringComparer.Ordinal);
        foreach (var unit in Units)
        {
            hash.Add(unit);
        }

        foreach (var kv in TicksPerUnit.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            hash.Add(kv.Key, StringComparer.Ordinal);
            hash.Add(kv.Value);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// One-based index of a named unit at a tick. <see cref="OfParent"/> is the parent unit's
/// <c>contains</c> when nested; null for the largest unit.
/// </summary>
public sealed record TimePartitionPosition(string Name, int Index, int? OfParent);
