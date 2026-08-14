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
/// Content-addressed enchantment block: parent hash plus discrete unit sets for volume, darkness, and fallacy.
/// </summary>
public sealed record EnchantmentBlock(
    string Hash,
    string? ParentHash,
    ImmutableArray<EnchantmentUnitId> Volume,
    ImmutableArray<EnchantmentUnitId> Darkness,
    ImmutableArray<EnchantmentUnitId> Fallacy)
{
    public const int AbbreviatedHashLength = 7;

    public int VolumeCount => Volume.Length;

    public int DarknessCount => Darkness.Length;

    public int FallacyCount => Fallacy.Length;

    public string AbbreviatedHash =>
        Hash.Length <= AbbreviatedHashLength ? Hash : Hash[..AbbreviatedHashLength];

    public static EnchantmentBlock CreateGenesis() =>
        Create(
            parentHash: null,
            volume: ImmutableArray<EnchantmentUnitId>.Empty,
            darkness: ImmutableArray<EnchantmentUnitId>.Empty,
            fallacy: ImmutableArray<EnchantmentUnitId>.Empty);

    public static EnchantmentBlock Create(
        string? parentHash,
        ImmutableArray<EnchantmentUnitId> volume,
        ImmutableArray<EnchantmentUnitId> darkness,
        ImmutableArray<EnchantmentUnitId> fallacy)
    {
        var orderedVolume = OrderUnique(volume);
        var orderedDarkness = OrderUnique(darkness);
        var orderedFallacy = OrderUnique(fallacy);
        var hash = ComputeHash(parentHash, orderedVolume, orderedDarkness, orderedFallacy);
        return new EnchantmentBlock(hash, parentHash, orderedVolume, orderedDarkness, orderedFallacy);
    }

    public static string ComputeHash(
        string? parentHash,
        ImmutableArray<EnchantmentUnitId> volume,
        ImmutableArray<EnchantmentUnitId> darkness,
        ImmutableArray<EnchantmentUnitId> fallacy)
    {
        var sb = new StringBuilder();
        sb.Append(parentHash ?? "");
        sb.Append('\n');
        AppendUnits(sb, "volume", volume);
        AppendUnits(sb, "darkness", darkness);
        AppendUnits(sb, "fallacy", fallacy);
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

/// <summary>Pure helpers for mutate, testing reduction, ancestry, and commutative combine.</summary>
public static class EnchantmentOps
{
    public static int UnitCount(double configValue) =>
        checked((int)Math.Round(configValue, MidpointRounding.AwayFromZero));

    public static (EnchantmentBlock Block, ulong NextUnitId) Mutate(
        EnchantmentBlock parent,
        EnchantNodeConfig config,
        ulong nextUnitId)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(config);

        var volumeDelta = UnitCount(config.VolumeDelta);
        var darknessDelta = UnitCount(config.DarknessDelta);
        var fallacyAdd = parent.DarknessCount + UnitCount(config.FallacyConstant);
        if (volumeDelta < 0 || darknessDelta < 0 || fallacyAdd < 0)
        {
            throw new InvalidOperationException("Enchantment unit deltas must be non-negative.");
        }

        var next = nextUnitId;
        var volume = parent.Volume.AddRange(Allocate(ref next, volumeDelta));
        var darkness = parent.Darkness.AddRange(Allocate(ref next, darknessDelta));
        var fallacy = parent.Fallacy.AddRange(Allocate(ref next, fallacyAdd));
        var block = EnchantmentBlock.Create(parent.Hash, volume, darkness, fallacy);
        return (block, next);
    }

    public static EnchantmentBlock ReduceFallacy(EnchantmentBlock parent, int removeCount)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (removeCount <= 0)
        {
            return parent;
        }

        // Ascending order: remove lowest ids first.
        var fallacy = parent.FallacyCount <= removeCount
            ? ImmutableArray<EnchantmentUnitId>.Empty
            : parent.Fallacy.RemoveRange(0, removeCount);

        return EnchantmentBlock.Create(parent.Hash, parent.Volume, parent.Darkness, fallacy);
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
            MergeUnits(ancestor.Darkness, unique.Select(b => b.Darkness).ToArray()),
            MergeUnits(ancestor.Fallacy, unique.Select(b => b.Fallacy).ToArray()));
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
            MergeUnits(ancestor.Darkness, left.Darkness, right.Darkness),
            MergeUnits(ancestor.Fallacy, left.Fallacy, right.Fallacy));
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
    /// Builds a block with sequential unit ids for tests / seeding synthetic stocks.
    /// Does not set a meaningful parent chain beyond an optional parent hash.
    /// </summary>
    public static (EnchantmentBlock Block, ulong NextUnitId) FromCounts(
        int volume,
        int darkness,
        int fallacy,
        ulong nextUnitId = 1,
        string? parentHash = null)
    {
        if (volume < 0 || darkness < 0 || fallacy < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Unit counts must be non-negative.");
        }

        var next = nextUnitId;
        var block = EnchantmentBlock.Create(
            parentHash,
            Allocate(ref next, volume),
            Allocate(ref next, darkness),
            Allocate(ref next, fallacy));
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
