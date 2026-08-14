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
        Assert.Equal(2, once.DarknessCount);
        Assert.Equal(1, once.FallacyCount);
        Assert.Equal(genesis.Hash, once.ParentHash);
        Assert.Equal(14UL, next);

        var (twice, _) = EnchantmentOps.Mutate(once, EnchantConfig, next);
        Assert.Equal(20, twice.VolumeCount);
        Assert.Equal(4, twice.DarknessCount);
        Assert.Equal(4, twice.FallacyCount);
    }

    [Fact]
    public void Mutate_DarknessUsesDoubledDeltaMinusDesigns()
    {
        var genesis = EnchantmentBlock.CreateGenesis();
        var (block, next) = EnchantmentOps.Mutate(genesis, EnchantConfig, nextUnitId: 1, designs: 1);

        Assert.Equal(10, block.VolumeCount);
        Assert.Equal(1, block.DarknessCount);
        Assert.Equal(1, block.FallacyCount);
        Assert.Equal(13UL, next);
    }

    [Fact]
    public void Mutate_DesignsClampDarknessAtZero()
    {
        var genesis = EnchantmentBlock.CreateGenesis();
        var (block, _) = EnchantmentOps.Mutate(genesis, EnchantConfig, nextUnitId: 1, designs: 5);

        Assert.Equal(0, block.DarknessCount);
        Assert.Equal(1, block.FallacyCount);
        Assert.Equal(10, block.VolumeCount);
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
    public void TryCombine_FastForwardsToDescendant_EitherOrder()
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
            EnchantmentOps.TryCombine([genesis, grandchild], blocks.ToBuilder()));
        Assert.Equal(
            grandchild,
            EnchantmentOps.TryCombine([grandchild, genesis], blocks.ToBuilder()));
    }

    [Fact]
    public void TryCombine_IncompatibleHistories_ReturnsNull()
    {
        var (left, next) = EnchantmentOps.FromCounts(1, 0, 0, nextUnitId: 1);
        var (right, _) = EnchantmentOps.FromCounts(1, 0, 0, nextUnitId: next);
        var blocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
            .Add(left.Hash, left)
            .Add(right.Hash, right);

        Assert.Null(EnchantmentOps.TryCombine([left, right], blocks.ToBuilder()));
        Assert.Null(EnchantmentOps.TryCombine([right, left], blocks.ToBuilder()));
    }

    [Fact]
    public void TryCombine_AnyIncompatiblePair_ReturnsNull()
    {
        var genesis = EnchantmentBlock.CreateGenesis();
        var (child, _) = EnchantmentOps.Mutate(genesis, EnchantConfig, 1);
        var (unrelated, _) = EnchantmentOps.FromCounts(1, 0, 0, nextUnitId: 100);
        var blocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
            .Add(genesis.Hash, genesis)
            .Add(child.Hash, child)
            .Add(unrelated.Hash, unrelated);

        Assert.Null(EnchantmentOps.TryCombine([genesis, child, unrelated], blocks.ToBuilder()));
    }

    [Fact]
    public void TryCombine_DivergentTips_IsCommutative()
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
        var left = EnchantmentBlock.Create(
            ancestor.Hash,
            ImmutableArray.Create(a1, a3, pOnly),
            ImmutableArray<EnchantmentUnitId>.Empty,
            ImmutableArray<EnchantmentUnitId>.Empty);
        var right = EnchantmentBlock.Create(
            ancestor.Hash,
            ImmutableArray.Create(a1, a2, sOnly),
            ImmutableArray<EnchantmentUnitId>.Empty,
            ImmutableArray<EnchantmentUnitId>.Empty);
        var blocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
            .Add(ancestor.Hash, ancestor)
            .Add(left.Hash, left)
            .Add(right.Hash, right);

        var ab = EnchantmentOps.TryCombine([left, right], blocks.ToBuilder());
        var ba = EnchantmentOps.TryCombine([right, left], blocks.ToBuilder());
        Assert.NotNull(ab);
        Assert.NotNull(ba);
        Assert.Equal(ab.Hash, ba.Hash);
        Assert.Equal(new[] { 1UL, 10UL, 20UL }, ab.Volume.Select(u => u.Value).ToArray());
        var expectedParent = string.CompareOrdinal(left.Hash, right.Hash) <= 0
            ? left.Hash
            : right.Hash;
        Assert.Equal(expectedParent, ab.ParentHash);
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
        // Left deleted a2; right deleted a3; both kept a1; each added new units.
        var left = EnchantmentBlock.Create(
            ancestor.Hash,
            ImmutableArray.Create(a1, a3, pOnly),
            ImmutableArray<EnchantmentUnitId>.Empty,
            ImmutableArray<EnchantmentUnitId>.Empty);
        var right = EnchantmentBlock.Create(
            ancestor.Hash,
            ImmutableArray.Create(a1, a2, sOnly),
            ImmutableArray<EnchantmentUnitId>.Empty,
            ImmutableArray<EnchantmentUnitId>.Empty);

        var merged = EnchantmentOps.ThreeWayMerge(ancestor, left, right);
        var swapped = EnchantmentOps.ThreeWayMerge(ancestor, right, left);
        Assert.Equal(new[] { 1UL, 10UL, 20UL }, merged.Volume.Select(u => u.Value).ToArray());
        Assert.Equal(merged.Hash, swapped.Hash);
        var expectedParent = string.CompareOrdinal(left.Hash, right.Hash) <= 0
            ? left.Hash
            : right.Hash;
        Assert.Equal(expectedParent, merged.ParentHash);
    }
}
