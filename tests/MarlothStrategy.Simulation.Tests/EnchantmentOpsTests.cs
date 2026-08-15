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
        FallacyConstant: 1,
        DesignDarknessDelta: 0.3);

    private static readonly DesignNodeConfig DesignConfig = new(
        Effort: 3,
        DesignDelta: 1,
        DarknessReduction: 0.9);

    [Fact]
    public void ComputeHash_IsStableForSameStructuralContent()
    {
        var a = EnchantmentBlock.Create(
            null,
            ImmutableArray.Create(new EnchantmentUnitId(1), new EnchantmentUnitId(2)),
            ImmutableArray.Create(new EnchantmentUnitId(3)),
            darkness: 1.5,
            fallacy: 0.25);
        var b = EnchantmentBlock.Create(
            null,
            ImmutableArray.Create(new EnchantmentUnitId(2), new EnchantmentUnitId(1)),
            ImmutableArray.Create(new EnchantmentUnitId(3)),
            darkness: 9.9,
            fallacy: 4.4);

        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(7, a.AbbreviatedHash.Length);
        Assert.Equal(1.5, a.Darkness);
        Assert.Equal(9.9, b.Darkness);
    }

    [Fact]
    public void ComputeHash_IgnoresScalarOnlyDifferences_WithSameParent()
    {
        var volume = ImmutableArray.Create(new EnchantmentUnitId(1));
        var designs = ImmutableArray<EnchantmentUnitId>.Empty;
        var left = EnchantmentBlock.Create("parent", volume, designs, darkness: 1, fallacy: 2);
        var right = EnchantmentBlock.Create("parent", volume, designs, darkness: 8, fallacy: 0.1);
        Assert.Equal(left.Hash, right.Hash);
    }

    [Fact]
    public void Mutate_AppendsVolumeAndScalarDarknessFallacy()
    {
        var genesis = EnchantmentBlock.CreateGenesis();
        var (once, next) = EnchantmentOps.Mutate(genesis, EnchantConfig, nextUnitId: 1);

        Assert.Equal(10, once.VolumeCount);
        Assert.Equal(0, once.DesignsCount);
        Assert.Equal(10.0, once.Darkness);
        Assert.Equal(1.0, once.Fallacy);
        Assert.Equal(genesis.Hash, once.ParentHash);
        Assert.Equal(11UL, next);

        var (twice, _) = EnchantmentOps.Mutate(once, EnchantConfig, next);
        Assert.Equal(20, twice.VolumeCount);
        Assert.Equal(20.0, twice.Darkness);
        Assert.Equal(12.0, twice.Fallacy);
    }

    [Fact]
    public void Mutate_PrefersUnusedDesignUnits_AtReducedDarkness()
    {
        var genesis = EnchantmentBlock.CreateGenesis();
        var (designed, nextAfterDesign) = EnchantmentOps.ApplyDesign(
            genesis,
            DesignConfig,
            nextUnitId: 1,
            applications: 4);
        Assert.Equal(4, designed.DesignsCount);
        Assert.Equal(0.0, designed.Darkness);

        var config = EnchantConfig with { DarknessDelta = 2 };
        var (mutated, next) = EnchantmentOps.Mutate(designed, config, nextAfterDesign);

        Assert.Equal(10, mutated.VolumeCount);
        Assert.Equal(4, mutated.DesignsCount);
        Assert.Equal(
            designed.Designs.Select(u => u.Value).OrderBy(v => v).ToArray(),
            mutated.Designs.Select(u => u.Value).OrderBy(v => v).ToArray());
        Assert.True(designed.Designs.All(id => mutated.Volume.Contains(id)));
        // Design darkness is an absolute delta, independent of the regular delta.
        Assert.Equal(4 * 0.3 + 6 * 2.0, mutated.Darkness, 6);
        Assert.Equal(1.0, mutated.Fallacy);
        Assert.Equal(11UL, next);
    }

    [Fact]
    public void Mutate_DoesNotRemoveDesignUnits()
    {
        var genesis = EnchantmentBlock.CreateGenesis();
        var (designed, nextId) = EnchantmentOps.ApplyDesign(
            genesis,
            DesignConfig,
            nextUnitId: 1,
            applications: 10);
        var (first, nextAfterFirst) = EnchantmentOps.Mutate(designed, EnchantConfig, nextId);
        var (second, _) = EnchantmentOps.Mutate(first, EnchantConfig, nextAfterFirst);

        Assert.Equal(10, first.DesignsCount);
        Assert.Equal(10, second.DesignsCount);
        Assert.Equal(10, first.VolumeCount);
        Assert.Equal(20, second.VolumeCount);
        Assert.Equal(10 * 0.3, first.Darkness, 6);
        Assert.Equal(10 * 0.3 + 10 * 1.0, second.Darkness, 6);
    }

    [Fact]
    public void ApplyDesign_AddsUnitsAndReducesDarkness()
    {
        var (block, _) = EnchantmentOps.FromCounts(
            volume: 0,
            designs: 0,
            darkness: 2.0,
            fallacy: 1.0,
            nextUnitId: 1);
        var (designed, next) = EnchantmentOps.ApplyDesign(block, DesignConfig, 1, applications: 2);

        Assert.Equal(2, designed.DesignsCount);
        Assert.Equal(2.0 - 1.8, designed.Darkness, 6);
        Assert.Equal(1.0, designed.Fallacy);
        Assert.Equal(block.Hash, designed.ParentHash);
        Assert.Equal(3UL, next);
    }

    [Fact]
    public void ApplyDesign_UsesDesignDeltaPerApplication()
    {
        var config = DesignConfig with { DesignDelta = 3 };
        var genesis = EnchantmentBlock.CreateGenesis();
        var (designed, next) = EnchantmentOps.ApplyDesign(
            genesis,
            config,
            nextUnitId: 1,
            applications: 2);

        Assert.Equal(6, designed.DesignsCount);
        Assert.Equal(0.0, designed.Darkness);
        Assert.Equal(7UL, next);
    }

    [Fact]
    public void ReduceFallacy_SubtractsScalarAmount()
    {
        var (block, _) = EnchantmentOps.FromCounts(
            volume: 2,
            designs: 0,
            darkness: 0,
            fallacy: 5.5,
            nextUnitId: 1);

        var reduced = EnchantmentOps.ReduceFallacy(block, removeAmount: 2.25);
        Assert.Equal(3.25, reduced.Fallacy);
        Assert.Equal(block.Hash, reduced.ParentHash);

        var cleared = EnchantmentOps.ReduceFallacy(reduced, removeAmount: 99);
        Assert.Equal(0, cleared.Fallacy);
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
        var (left, next) = EnchantmentOps.FromCounts(1, 0, 0, 0, nextUnitId: 1);
        var (right, _) = EnchantmentOps.FromCounts(1, 0, 0, 0, nextUnitId: next);
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
        var (unrelated, _) = EnchantmentOps.FromCounts(1, 0, 0, 0, nextUnitId: 100);
        var blocks = ImmutableDictionary<string, EnchantmentBlock>.Empty
            .Add(genesis.Hash, genesis)
            .Add(child.Hash, child)
            .Add(unrelated.Hash, unrelated);

        Assert.Null(EnchantmentOps.TryCombine([genesis, child, unrelated], blocks.ToBuilder()));
    }

    [Fact]
    public void TryCombine_DivergentTips_MergesUnitsAndScalarDeltas()
    {
        var a1 = new EnchantmentUnitId(1);
        var a2 = new EnchantmentUnitId(2);
        var a3 = new EnchantmentUnitId(3);
        var pOnly = new EnchantmentUnitId(10);
        var sOnly = new EnchantmentUnitId(20);

        var ancestor = EnchantmentBlock.Create(
            null,
            ImmutableArray.Create(a1, a2, a3),
            ImmutableArray.Create(a1),
            darkness: 1.0,
            fallacy: 2.0);
        var left = EnchantmentBlock.Create(
            ancestor.Hash,
            ImmutableArray.Create(a1, a3, pOnly),
            ImmutableArray.Create(a1, pOnly),
            darkness: 1.5,
            fallacy: 2.0);
        var right = EnchantmentBlock.Create(
            ancestor.Hash,
            ImmutableArray.Create(a1, a2, sOnly),
            ImmutableArray.Create(a1, sOnly),
            darkness: 1.0,
            fallacy: 3.25);
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
        Assert.Equal(new[] { 1UL, 10UL, 20UL }, ab.Designs.Select(u => u.Value).ToArray());
        Assert.Equal(1.5, ab.Darkness);
        Assert.Equal(3.25, ab.Fallacy);
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
            darkness: 0,
            fallacy: 0);
        var left = EnchantmentBlock.Create(
            ancestor.Hash,
            ImmutableArray.Create(a1, a3, pOnly),
            ImmutableArray<EnchantmentUnitId>.Empty,
            darkness: 0,
            fallacy: 0);
        var right = EnchantmentBlock.Create(
            ancestor.Hash,
            ImmutableArray.Create(a1, a2, sOnly),
            ImmutableArray<EnchantmentUnitId>.Empty,
            darkness: 0,
            fallacy: 0);

        var merged = EnchantmentOps.ThreeWayMerge(ancestor, left, right);
        var swapped = EnchantmentOps.ThreeWayMerge(ancestor, right, left);
        Assert.Equal(new[] { 1UL, 10UL, 20UL }, merged.Volume.Select(u => u.Value).ToArray());
        Assert.Equal(merged.Hash, swapped.Hash);
        var expectedParent = string.CompareOrdinal(left.Hash, right.Hash) <= 0
            ? left.Hash
            : right.Hash;
        Assert.Equal(expectedParent, merged.ParentHash);
    }

    [Fact]
    public void MergeScalar_AddsBranchDeltasAndClampsAtZero()
    {
        Assert.Equal(1.5, EnchantmentOps.MergeScalar(1.0, 1.5, 1.0));
        Assert.Equal(0.0, EnchantmentOps.MergeScalar(2.0, 1.0, 0.5));
    }
}
