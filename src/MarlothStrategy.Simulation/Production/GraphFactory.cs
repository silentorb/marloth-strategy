using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Builds the magic-agency node catalog and graph from the essential baseline
/// (including an enchant self-loop), optionally adding the testing variation
/// that fans testing output back onto enchant alongside that self-loop.
/// </summary>
public static class GraphFactory
{
    public static (NodeGraph Graph, NodeTypeCatalog Catalog) Create(bool includeTesting)
    {
        var catalog = CreateCatalog();
        var graph = includeTesting ? CreateTestingGraph() : CreateEssentialGraph();
        return (graph, catalog);
    }

    public static NodeTypeCatalog CreateCatalog()
    {
        var moneyType = new SignalType(SignalTypes.Money);
        var enchantmentType = new SignalType(SignalTypes.Enchantment);

        var enchantType = new NodeType(
            MagicAgencySeed.EnchantTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MagicAgencySeed.EnchantmentPortId, new Port(MagicAgencySeed.EnchantmentPortId, enchantmentType)),
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MagicAgencySeed.EnchantmentPortId, new Port(MagicAgencySeed.EnchantmentPortId, enchantmentType)));

        var testingType = new NodeType(
            MagicAgencySeed.TestingTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MagicAgencySeed.EnchantmentPortId, new Port(MagicAgencySeed.EnchantmentPortId, enchantmentType)),
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MagicAgencySeed.EnchantmentPortId, new Port(MagicAgencySeed.EnchantmentPortId, enchantmentType)));

        var sellType = new NodeType(
            MagicAgencySeed.SellTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MagicAgencySeed.EnchantmentPortId, new Port(MagicAgencySeed.EnchantmentPortId, enchantmentType)),
            ImmutableDictionary<PortId, Port>.Empty.Add(
                MagicAgencySeed.MoneyPortId,
                new Port(MagicAgencySeed.MoneyPortId, moneyType)));

        var treasuryType = new NodeType(
            MagicAgencySeed.TreasuryTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MagicAgencySeed.MoneyPortId, new Port(MagicAgencySeed.MoneyPortId, moneyType)),
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MagicAgencySeed.MoneyPortId, new Port(MagicAgencySeed.MoneyPortId, moneyType)));

        var payrollType = new NodeType(
            MagicAgencySeed.PayrollTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MagicAgencySeed.MoneyPortId, new Port(MagicAgencySeed.MoneyPortId, moneyType)),
            ImmutableDictionary<PortId, Port>.Empty);

        var mergeType = new NodeType(
            MagicAgencySeed.MergeTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MagicAgencySeed.PrimaryPortId, new Port(MagicAgencySeed.PrimaryPortId, enchantmentType))
                .Add(MagicAgencySeed.SecondaryPortId, new Port(MagicAgencySeed.SecondaryPortId, enchantmentType)),
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MagicAgencySeed.EnchantmentPortId, new Port(MagicAgencySeed.EnchantmentPortId, enchantmentType)));

        return new NodeTypeCatalog(
            ImmutableDictionary<NodeTypeId, NodeType>.Empty
                .Add(MagicAgencySeed.EnchantTypeId, enchantType)
                .Add(MagicAgencySeed.TestingTypeId, testingType)
                .Add(MagicAgencySeed.SellTypeId, sellType)
                .Add(MagicAgencySeed.TreasuryTypeId, treasuryType)
                .Add(MagicAgencySeed.PayrollTypeId, payrollType)
                .Add(MagicAgencySeed.MergeTypeId, mergeType));
    }

    /// <summary>
    /// Essential baseline: enchant self-loop plus the money spine through sell/treasury/payroll.
    /// </summary>
    public static NodeGraph CreateEssentialGraph()
    {
        return new NodeGraph(
            ImmutableDictionary<NodeId, Node>.Empty
                .Add(MagicAgencySeed.EnchantNodeId, new Node(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantTypeId))
                .Add(MagicAgencySeed.SellNodeId, new Node(MagicAgencySeed.SellNodeId, MagicAgencySeed.SellTypeId))
                .Add(MagicAgencySeed.TreasuryNodeId, new Node(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.TreasuryTypeId))
                .Add(MagicAgencySeed.PayrollNodeId, new Node(MagicAgencySeed.PayrollNodeId, MagicAgencySeed.PayrollTypeId)),
            ImmutableDictionary<EdgeId, Edge>.Empty
                .Add(
                    new EdgeId("enchantment-to-enchant"),
                    new Edge(
                        new PortReference(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId)))
                .Add(
                    new EdgeId("enchantment-to-sell"),
                    new Edge(
                        new PortReference(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId)))
                .Add(
                    new EdgeId("money-to-treasury"),
                    new Edge(
                        new PortReference(MagicAgencySeed.SellNodeId, MagicAgencySeed.MoneyPortId),
                        new PortReference(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId)))
                .Add(
                    new EdgeId("treasury-to-payroll"),
                    new Edge(
                        new PortReference(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                        new PortReference(MagicAgencySeed.PayrollNodeId, MagicAgencySeed.MoneyPortId))));
    }

    /// <summary>
    /// Testing variation: keep the enchant self-loop, insert testing between enchant and sell,
    /// and fan testing output back onto enchant (port-level <c>+</c> with the self-loop).
    /// </summary>
    public static NodeGraph CreateTestingGraph()
    {
        return new NodeGraph(
            ImmutableDictionary<NodeId, Node>.Empty
                .Add(MagicAgencySeed.EnchantNodeId, new Node(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantTypeId))
                .Add(MagicAgencySeed.TestingNodeId, new Node(MagicAgencySeed.TestingNodeId, MagicAgencySeed.TestingTypeId))
                .Add(MagicAgencySeed.SellNodeId, new Node(MagicAgencySeed.SellNodeId, MagicAgencySeed.SellTypeId))
                .Add(MagicAgencySeed.TreasuryNodeId, new Node(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.TreasuryTypeId))
                .Add(MagicAgencySeed.PayrollNodeId, new Node(MagicAgencySeed.PayrollNodeId, MagicAgencySeed.PayrollTypeId)),
            ImmutableDictionary<EdgeId, Edge>.Empty
                .Add(
                    new EdgeId("enchantment-to-enchant"),
                    new Edge(
                        new PortReference(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId)))
                .Add(
                    new EdgeId("enchantment-to-testing"),
                    new Edge(
                        new PortReference(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId)))
                .Add(
                    new EdgeId("testing-to-sell"),
                    new Edge(
                        new PortReference(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId)))
                .Add(
                    new EdgeId("testing-to-enchant"),
                    new Edge(
                        new PortReference(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId)))
                .Add(
                    new EdgeId("money-to-treasury"),
                    new Edge(
                        new PortReference(MagicAgencySeed.SellNodeId, MagicAgencySeed.MoneyPortId),
                        new PortReference(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId)))
                .Add(
                    new EdgeId("treasury-to-payroll"),
                    new Edge(
                        new PortReference(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                        new PortReference(MagicAgencySeed.PayrollNodeId, MagicAgencySeed.MoneyPortId))));
    }

    /// <summary>
    /// Graph that includes a merge node. Not used by scenarios; tests use it for merge
    /// behavior and dual-port layout.
    /// </summary>
    public static NodeGraph CreateGraphWithMergeNode()
    {
        return new NodeGraph(
            ImmutableDictionary<NodeId, Node>.Empty
                .Add(MagicAgencySeed.EnchantNodeId, new Node(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantTypeId))
                .Add(MagicAgencySeed.TestingNodeId, new Node(MagicAgencySeed.TestingNodeId, MagicAgencySeed.TestingTypeId))
                .Add(MagicAgencySeed.SellNodeId, new Node(MagicAgencySeed.SellNodeId, MagicAgencySeed.SellTypeId))
                .Add(MagicAgencySeed.TreasuryNodeId, new Node(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.TreasuryTypeId))
                .Add(MagicAgencySeed.PayrollNodeId, new Node(MagicAgencySeed.PayrollNodeId, MagicAgencySeed.PayrollTypeId))
                .Add(MagicAgencySeed.MergeNodeId, new Node(MagicAgencySeed.MergeNodeId, MagicAgencySeed.MergeTypeId)),
            ImmutableDictionary<EdgeId, Edge>.Empty
                .Add(
                    new EdgeId("enchantment-to-testing"),
                    new Edge(
                        new PortReference(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId)))
                .Add(
                    new EdgeId("enchantment-to-merge-primary"),
                    new Edge(
                        new PortReference(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.MergeNodeId, MagicAgencySeed.PrimaryPortId)))
                .Add(
                    new EdgeId("testing-to-sell"),
                    new Edge(
                        new PortReference(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.SellNodeId, MagicAgencySeed.EnchantmentPortId)))
                .Add(
                    new EdgeId("testing-to-merge-secondary"),
                    new Edge(
                        new PortReference(MagicAgencySeed.TestingNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.MergeNodeId, MagicAgencySeed.SecondaryPortId)))
                .Add(
                    new EdgeId("merge-to-enchant"),
                    new Edge(
                        new PortReference(MagicAgencySeed.MergeNodeId, MagicAgencySeed.EnchantmentPortId),
                        new PortReference(MagicAgencySeed.EnchantNodeId, MagicAgencySeed.EnchantmentPortId)))
                .Add(
                    new EdgeId("money-to-treasury"),
                    new Edge(
                        new PortReference(MagicAgencySeed.SellNodeId, MagicAgencySeed.MoneyPortId),
                        new PortReference(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId)))
                .Add(
                    new EdgeId("treasury-to-payroll"),
                    new Edge(
                        new PortReference(MagicAgencySeed.TreasuryNodeId, MagicAgencySeed.MoneyPortId),
                        new PortReference(MagicAgencySeed.PayrollNodeId, MagicAgencySeed.MoneyPortId))));
    }
}
