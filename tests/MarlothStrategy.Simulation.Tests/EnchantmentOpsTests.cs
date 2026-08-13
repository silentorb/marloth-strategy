using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;
using MarlothStrategy.Simulation.Production;

namespace MarlothStrategy.Simulation.Tests;

public sealed class EnchantmentOpsTests
{
    private static readonly EnchantNodeConfig EnchantConfig = new(
        Effort: 10,
        VolumeDelta: 10,
        DarknessDelta: 1,
        FallacyConstant: 1);

    [Fact]
    public void ComputeHash_IsStableForSameContent()
    {
        var a = EnchantmentBlock.Create(
            null,
            ImmutableArray.Create(new EnchantmentUnitId(1), new EnchantmentUnitId(2)),
            ImmutableArray.Create(new EnchantmentUnitId(3)),
            ImmutableArray<EnchantmentUnitId>.Empty);
        var b = EnchantmentBlock.Create(
            null,
            ImmutableArray.Create(new EnchantmentUnitId(2), new EnchantmentUnitId(1)),
            ImmutableArray.Create(new EnchantmentUnitId(3)),
            ImmutableArray<EnchantmentUnitId>.Empty);

        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(7, a.AbbreviatedHash.Length);
    }

    [Fact]
    public void Mutate_AppendsDocumentedUnitCounts()
    {
        var genesis = EnchantmentBlock.CreateGenesis();
        var (once, next) = EnchantmentOps.Mutate(genesis, EnchantConfig, nextUnitId: 1);

        Assert.Equal(10, once.VolumeCount);
        Assert.Equal(1, once.DarknessCount);
        Assert.Equal(1, once.FallacyCount);
        Assert.Equal(genesis.Hash, once.ParentHash);
        Assert.Equal(13UL, next);

        var (twice, _) = EnchantmentOps.Mutate(once, EnchantConfig, next);
        Assert.Equal(20, twice.VolumeCount);
        Assert.Equal(2, twice.DarknessCount);
        Assert.Equal(3, twice.FallacyCount);
    }

    [Fact]
    public void ReduceFallacy_RemovesLowestIdsFirst()
    {
        var (block, _) = EnchantmentOps.FromCounts(volume: 2, darkness: 0, fallacy: 5, nextUnitId: 1);
        Assert.Equal(
            new[] { 3UL, 4UL, 5UL, 6UL, 7UL },
            block.Fallacy.Select(u => u.Value).ToArray());

        var reduced = EnchantmentOps.ReduceFallacy(block, removeCount: 2);
        Assert.Equal(new[] { 5UL, 6UL, 7UL }, reduced.Fallacy.Select(u => u.Value).ToArray());
        Assert.Equal(block.Hash, reduced.ParentHash);

        var cleared = EnchantmentOps.ReduceFallacy(reduced, removeCount: 99);
        Assert.Empty(cleared.Fallacy);
    }

    [Fact]
    public void ResolveMerge_FastForwardsToDescendant()
    {
        var genesis = EnchantmentBlock.CreateGenesis();
        var (child, next) = EnchantmentOps.Mutate(genesis, EnchantConfig, 1);
        var (grandchild, _) = EnchantmentOps.Mutate(child, EnchantConfig, next);
        var blocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
            .Add(genesis.Hash, genesis)
            .Add(child.Hash, child)
            .Add(grandchild.Hash, grandchild);

        Assert.Equal(
            grandchild,
            EnchantmentOps.ResolveMerge(genesis, grandchild, blocks.ToBuilder()));
        Assert.Equal(
            grandchild,
            EnchantmentOps.ResolveMerge(grandchild, genesis, blocks.ToBuilder()));
    }

    [Fact]
    public void ResolveMerge_IncompatibleHistories_ReturnsPrimary()
    {
        var (left, next) = EnchantmentOps.FromCounts(1, 0, 0, nextUnitId: 1);
        var (right, _) = EnchantmentOps.FromCounts(1, 0, 0, nextUnitId: next);
        var blocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
            .Add(left.Hash, left)
            .Add(right.Hash, right);

        Assert.Equal(left, EnchantmentOps.ResolveMerge(left, right, blocks.ToBuilder()));
    }

    [Fact]
    public void ThreeWayMerge_OmitsAncestorUnitsMissingFromEitherSide()
    {
        var a1 = new EnchantmentUnitId(1);
        var a2 = new EnchantmentUnitId(2);
        var a3 = new EnchantmentUnitId(3);
        var pOnly = new EnchantmentUnitId(10);
        var sOnly = new EnchantmentUnitId(20);

        var ancestor = EnchantmentBlock.Create(
            null,
            ImmutableArray.Create(a1, a2, a3),
            ImmutableArray<EnchantmentUnitId>.Empty,
            ImmutableArray<EnchantmentUnitId>.Empty);
        // Primary deleted a2; secondary deleted a3; both kept a1; each added new units.
        var primary = EnchantmentBlock.Create(
            ancestor.Hash,
            ImmutableArray.Create(a1, a3, pOnly),
            ImmutableArray<EnchantmentUnitId>.Empty,
            ImmutableArray<EnchantmentUnitId>.Empty);
        var secondary = EnchantmentBlock.Create(
            ancestor.Hash,
            ImmutableArray.Create(a1, a2, sOnly),
            ImmutableArray<EnchantmentUnitId>.Empty,
            ImmutableArray<EnchantmentUnitId>.Empty);

        var merged = EnchantmentOps.ThreeWayMerge(ancestor, primary, secondary);
        Assert.Equal(new[] { 1UL, 10UL, 20UL }, merged.Volume.Select(u => u.Value).ToArray());
        Assert.Equal(primary.Hash, merged.ParentHash);
    }
}
