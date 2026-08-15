using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MarlothStrategy.Simulation.Production;

/// <summary>Unique id within an enchantment composite property; ordered for deterministic processing.</summary>
public readonly record struct EnchantmentUnitId(ulong Value) : IComparable<EnchantmentUnitId>
{
    public int CompareTo(EnchantmentUnitId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Content-addressed enchantment block: parent hash plus discrete unit sets for volume and designs,
/// with floating-point darkness and fallacy scalars that do not participate in hashing.
/// </summary>
public sealed record EnchantmentBlock(
    string Hash,
    string? ParentHash,
    ImmutableArray<EnchantmentUnitId> Volume,
    ImmutableArray<EnchantmentUnitId> Designs,
    double Darkness,
    double Fallacy)
{
    public const int AbbreviatedHashLength = 7;

    public int VolumeCount => Volume.Length;

    public int DesignsCount => Designs.Length;

    public string AbbreviatedHash =>
        Hash.Length <= AbbreviatedHashLength ? Hash : Hash[..AbbreviatedHashLength];

    public static EnchantmentBlock CreateGenesis() =>
        Create(
            parentHash: null,
            volume: ImmutableArray<EnchantmentUnitId>.Empty,
            designs: ImmutableArray<EnchantmentUnitId>.Empty,
            darkness: 0,
            fallacy: 0);

    public static EnchantmentBlock Create(
        string? parentHash,
        ImmutableArray<EnchantmentUnitId> volume,
        ImmutableArray<EnchantmentUnitId> designs,
        double darkness,
        double fallacy)
    {
        if (darkness < 0 || fallacy < 0)
        {
            throw new ArgumentOutOfRangeException(
                darkness < 0 ? nameof(darkness) : nameof(fallacy),
                "Darkness and fallacy must be non-negative.");
        }

        var orderedVolume = OrderUnique(volume);
        var orderedDesigns = OrderUnique(designs);
        var hash = ComputeHash(parentHash, orderedVolume, orderedDesigns);
        return new EnchantmentBlock(
            hash,
            parentHash,
            orderedVolume,
            orderedDesigns,
            darkness,
            fallacy);
    }

    public static string ComputeHash(
        string? parentHash,
        ImmutableArray<EnchantmentUnitId> volume,
        ImmutableArray<EnchantmentUnitId> designs)
    {
        var sb = new StringBuilder();
        sb.Append(parentHash ?? "");
        sb.Append('\n');
        AppendUnits(sb, "volume", volume);
        AppendUnits(sb, "designs", designs);
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendUnits(
        StringBuilder sb,
        string label,
        ImmutableArray<EnchantmentUnitId> units)
    {
        sb.Append(label);
        sb.Append(':');
        for (var i = 0; i < units.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(units[i].Value.ToString(CultureInfo.InvariantCulture));
        }

        sb.Append('\n');
    }

    private static ImmutableArray<EnchantmentUnitId> OrderUnique(
        ImmutableArray<EnchantmentUnitId> units)
    {
        if (units.IsDefaultOrEmpty)
        {
            return ImmutableArray<EnchantmentUnitId>.Empty;
        }

        return units
            .Distinct()
            .OrderBy(u => u.Value)
            .ToImmutableArray();
    }
}

/// <summary>Pure helpers for mutate, design growth, testing reduction, ancestry, and commutative combine.</summary>
public static class EnchantmentOps
{
    public static int UnitCount(double configValue) =>
        checked((int)Math.Round(configValue, MidpointRounding.AwayFromZero));

    public static double ClampNonNegative(double value) => value < 0 ? 0 : value;

    public static (EnchantmentBlock Block, ulong NextUnitId) Mutate(
        EnchantmentBlock parent,
        EnchantNodeConfig config,
        ulong nextUnitId)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(config);

        var volumeDelta = UnitCount(config.VolumeDelta);
        if (volumeDelta < 0)
        {
            throw new InvalidOperationException("Enchantment volume delta must be non-negative.");
        }

        if (config.DarknessDelta < 0
            || config.FallacyConstant < 0
            || config.DesignDarknessDelta < 0)
        {
            throw new InvalidOperationException(
                "Enchantment darkness and fallacy parameters must be non-negative.");
        }

        var volumeInParent = ToSet(parent.Volume);
        var unusedDesigns = parent.Designs
            .Where(id => !volumeInParent.Contains(id))
            .OrderBy(id => id.Value)
            .ToArray();

        var next = nextUnitId;
        var volumeBuilder = parent.Volume.ToBuilder();
        var darknessAdd = 0.0;
        var remaining = volumeDelta;
        var designIndex = 0;

        while (remaining > 0 && designIndex < unusedDesigns.Length)
        {
            volumeBuilder.Add(unusedDesigns[designIndex]);
            darknessAdd += config.DesignDarknessDelta;
            designIndex++;
            remaining--;
        }

        if (remaining > 0)
        {
            volumeBuilder.AddRange(Allocate(ref next, remaining));
            darknessAdd += config.DarknessDelta * remaining;
        }

        var darkness = ClampNonNegative(parent.Darkness + darknessAdd);
        var fallacy = ClampNonNegative(
            parent.Fallacy + parent.Darkness + config.FallacyConstant);
        var block = EnchantmentBlock.Create(
            parent.Hash,
            volumeBuilder.ToImmutable(),
            parent.Designs,
            darkness,
            fallacy);
        return (block, next);
    }

    public static (EnchantmentBlock Block, ulong NextUnitId) ApplyDesign(
        EnchantmentBlock parent,
        DesignNodeConfig config,
        ulong nextUnitId,
        int applications)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(config);
        if (applications < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(applications));
        }

        if (applications == 0)
        {
            return (parent, nextUnitId);
        }

        var designDelta = UnitCount(config.DesignDelta);
        if (designDelta < 0 || config.DarknessReduction < 0)
        {
            throw new InvalidOperationException(
                "Design delta and darkness reduction must be non-negative.");
        }

        var unitsToAdd = checked(designDelta * applications);
        var next = nextUnitId;
        var designs = parent.Designs.AddRange(Allocate(ref next, unitsToAdd));
        var darkness = ClampNonNegative(
            parent.Darkness - (config.DarknessReduction * applications));
        var block = EnchantmentBlock.Create(
            parent.Hash,
            parent.Volume,
            designs,
            darkness,
            parent.Fallacy);
        return (block, next);
    }

    public static EnchantmentBlock ReduceFallacy(EnchantmentBlock parent, double removeAmount)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (removeAmount <= 0)
        {
            return parent;
        }

        var fallacy = ClampNonNegative(parent.Fallacy - removeAmount);
        if (fallacy == parent.Fallacy)
        {
            return parent;
        }

        return EnchantmentBlock.Create(
            parent.Hash,
            parent.Volume,
            parent.Designs,
            parent.Darkness,
            fallacy);
    }

    public static bool IsAncestor(
        EnchantmentBlock candidate,
        EnchantmentBlock descendant,
        IDictionary<string, EnchantmentBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(descendant);
        ArgumentNullException.ThrowIfNull(blocks);

        var current = descendant;
        while (true)
        {
            if (current.Hash == candidate.Hash)
            {
                return true;
            }

            if (current.ParentHash is null)
            {
                return false;
            }

            if (!blocks.TryGetValue(current.ParentHash, out var parent))
            {
                throw new InvalidOperationException(
                    $"Missing enchantment block '{current.ParentHash}' while walking ancestry.");
            }

            current = parent;
        }
    }

    public static EnchantmentBlock? FindCommonAncestor(
        EnchantmentBlock left,
        EnchantmentBlock right,
        IDictionary<string, EnchantmentBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(blocks);

        var leftAncestors = new HashSet<string>(StringComparer.Ordinal);
        var current = left;
        while (true)
        {
            leftAncestors.Add(current.Hash);
            if (current.ParentHash is null)
            {
                break;
            }

            if (!blocks.TryGetValue(current.ParentHash, out var parent))
            {
                throw new InvalidOperationException(
                    $"Missing enchantment block '{current.ParentHash}' while walking ancestry.");
            }

            current = parent;
        }

        current = right;
        while (true)
        {
            if (leftAncestors.Contains(current.Hash))
            {
                return current;
            }

            if (current.ParentHash is null)
            {
                return null;
            }

            if (!blocks.TryGetValue(current.ParentHash, out var parent))
            {
                throw new InvalidOperationException(
                    $"Missing enchantment block '{current.ParentHash}' while walking ancestry.");
            }

            current = parent;
        }
    }

    /// <summary>
    /// Combines enchantment histories. Returns <c>null</c> when the input set is empty
    /// or any pair has no common ancestor (no value / empty port).
    /// </summary>
    public static EnchantmentBlock? TryCombine(
        IReadOnlyList<EnchantmentBlock> inputs,
        IDictionary<string, EnchantmentBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(blocks);

        var unique = new List<EnchantmentBlock>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in inputs)
        {
            ArgumentNullException.ThrowIfNull(block);
            if (seen.Add(block.Hash))
            {
                unique.Add(block);
            }
        }

        if (unique.Count == 0)
        {
            return null;
        }

        if (unique.Count == 1)
        {
            return unique[0];
        }

        for (var i = 0; i < unique.Count; i++)
        {
            for (var j = i + 1; j < unique.Count; j++)
            {
                if (FindCommonAncestor(unique[i], unique[j], blocks) is null)
                {
                    return null;
                }
            }
        }

        foreach (var candidate in unique)
        {
            var isNewestTip = true;
            foreach (var other in unique)
            {
                if (other.Hash == candidate.Hash)
                {
                    continue;
                }

                if (!IsAncestor(other, candidate, blocks))
                {
                    isNewestTip = false;
                    break;
                }
            }

            if (isNewestTip)
            {
                return candidate;
            }
        }

        var ancestor = unique[0];
        for (var i = 1; i < unique.Count; i++)
        {
            ancestor = FindCommonAncestor(ancestor, unique[i], blocks)
                ?? throw new InvalidOperationException(
                    "Expected a common ancestor after pairwise ancestry checks.");
        }

        var parentHash = unique
            .Where(candidate => unique.All(other =>
                other.Hash == candidate.Hash || !IsAncestor(candidate, other, blocks)))
            .Select(tip => tip.Hash)
            .Min(StringComparer.Ordinal)
            ?? throw new InvalidOperationException("Expected at least one incomparable tip.");

        return EnchantmentBlock.Create(
            parentHash,
            MergeUnits(ancestor.Volume, unique.Select(b => b.Volume).ToArray()),
            MergeUnits(ancestor.Designs, unique.Select(b => b.Designs).ToArray()),
            MergeScalar(ancestor.Darkness, unique.Select(b => b.Darkness).ToArray()),
            MergeScalar(ancestor.Fallacy, unique.Select(b => b.Fallacy).ToArray()));
    }

    public static EnchantmentBlock ThreeWayMerge(
        EnchantmentBlock ancestor,
        EnchantmentBlock left,
        EnchantmentBlock right)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var parentHash = string.CompareOrdinal(left.Hash, right.Hash) <= 0
            ? left.Hash
            : right.Hash;
        return EnchantmentBlock.Create(
            parentHash,
            MergeUnits(ancestor.Volume, left.Volume, right.Volume),
            MergeUnits(ancestor.Designs, left.Designs, right.Designs),
            MergeScalar(ancestor.Darkness, left.Darkness, right.Darkness),
            MergeScalar(ancestor.Fallacy, left.Fallacy, right.Fallacy));
    }

    /// <summary>
    /// Units in ancestor missing from either side are omitted; otherwise union of both sides.
    /// </summary>
    public static ImmutableArray<EnchantmentUnitId> MergeUnits(
        ImmutableArray<EnchantmentUnitId> ancestor,
        ImmutableArray<EnchantmentUnitId> left,
        ImmutableArray<EnchantmentUnitId> right) =>
        MergeUnits(ancestor, [left, right]);

    /// <summary>
    /// Units in ancestor missing from any side are omitted; otherwise union of all sides.
    /// </summary>
    public static ImmutableArray<EnchantmentUnitId> MergeUnits(
        ImmutableArray<EnchantmentUnitId> ancestor,
        IReadOnlyList<ImmutableArray<EnchantmentUnitId>> sides)
    {
        ArgumentNullException.ThrowIfNull(sides);

        var a = ToSet(ancestor);
        var sets = new HashSet<EnchantmentUnitId>[sides.Count];
        var union = new SortedSet<ulong>();
        for (var i = 0; i < sides.Count; i++)
        {
            sets[i] = ToSet(sides[i]);
            foreach (var id in sets[i])
            {
                union.Add(id.Value);
            }
        }

        var result = new SortedSet<ulong>();
        foreach (var value in union)
        {
            var id = new EnchantmentUnitId(value);
            if (a.Contains(id))
            {
                var missingFromAny = false;
                foreach (var set in sets)
                {
                    if (!set.Contains(id))
                    {
                        missingFromAny = true;
                        break;
                    }
                }

                if (missingFromAny)
                {
                    continue;
                }
            }

            result.Add(value);
        }

        return result.Select(v => new EnchantmentUnitId(v)).ToImmutableArray();
    }

    /// <summary>
    /// Scalar merge: ancestor + sum of each branch's delta from ancestor, clamped at zero.
    /// </summary>
    public static double MergeScalar(double ancestor, params double[] sides) =>
        MergeScalar(ancestor, (IReadOnlyList<double>)sides);

    public static double MergeScalar(double ancestor, IReadOnlyList<double> sides)
    {
        ArgumentNullException.ThrowIfNull(sides);
        var total = ancestor;
        foreach (var side in sides)
        {
            total += side - ancestor;
        }

        return ClampNonNegative(total);
    }

    /// <summary>
    /// Builds a block with sequential unit ids for tests / seeding synthetic stocks.
    /// Does not set a meaningful parent chain beyond an optional parent hash.
    /// </summary>
    public static (EnchantmentBlock Block, ulong NextUnitId) FromCounts(
        int volume,
        int designs,
        double darkness,
        double fallacy,
        ulong nextUnitId = 1,
        string? parentHash = null)
    {
        if (volume < 0 || designs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Unit counts must be non-negative.");
        }

        if (darkness < 0 || fallacy < 0)
        {
            throw new ArgumentOutOfRangeException(
                darkness < 0 ? nameof(darkness) : nameof(fallacy),
                "Darkness and fallacy must be non-negative.");
        }

        var next = nextUnitId;
        var block = EnchantmentBlock.Create(
            parentHash,
            Allocate(ref next, volume),
            Allocate(ref next, designs),
            darkness,
            fallacy);
        return (block, next);
    }

    private static HashSet<EnchantmentUnitId> ToSet(ImmutableArray<EnchantmentUnitId> units) =>
        units.IsDefaultOrEmpty
            ? new HashSet<EnchantmentUnitId>()
            : units.ToHashSet();

    private static ImmutableArray<EnchantmentUnitId> Allocate(ref ulong nextUnitId, int count)
    {
        if (count == 0)
        {
            return ImmutableArray<EnchantmentUnitId>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<EnchantmentUnitId>(count);
        for (var i = 0; i < count; i++)
        {
            builder.Add(new EnchantmentUnitId(nextUnitId++));
        }

        return builder.MoveToImmutable();
    }
}
