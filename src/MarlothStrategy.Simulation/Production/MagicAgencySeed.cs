using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public static class MagicAgencySeed
{
    public static readonly NodeTypeId EnchantTypeId = new("enchant");
    public static readonly NodeTypeId TestingTypeId = new("testing");
    public static readonly NodeTypeId SellTypeId = new("sell");
    public static readonly NodeTypeId TreasuryTypeId = new("treasury");
    public static readonly NodeTypeId PayrollTypeId = new("payroll");

    public static readonly NodeId EnchantNodeId = new("enchant");
    public static readonly NodeId TestingNodeId = new("testing");
    public static readonly NodeId SellNodeId = new("sell");
    public static readonly NodeId TreasuryNodeId = new("treasury");
    public static readonly NodeId PayrollNodeId = new("payroll");

    public static readonly ActorId ActorId = new("intern");

    public static readonly PortId MoneyPortId = new("money");
    public static readonly PortId EnchantmentPortId = new("enchantment");

    public static GameState CreateInitialState() =>
        CreateInitialState(
            NodeTypeConfigLoader.LoadFromBaseDirectory(),
            ActorConfigLoader.LoadFromBaseDirectory());

    public static GameState CreateInitialState(NodeTypeConfigs nodeConfigs) =>
        CreateInitialState(nodeConfigs, ActorConfigLoader.LoadFromBaseDirectory());

    public static GameState CreateInitialState(
        NodeTypeConfigs nodeConfigs,
        ImmutableDictionary<ActorId, Actor> actors)
    {
        ArgumentNullException.ThrowIfNull(nodeConfigs);
        ArgumentNullException.ThrowIfNull(actors);

        if (!actors.ContainsKey(ActorId))
        {
            throw new InvalidOperationException(
                $"Magic agency seed requires actor '{ActorId}'.");
        }

        if (nodeConfigs.Payroll.Period <= 0)
        {
            throw new InvalidOperationException(
                "Payroll period must be a positive integer.");
        }

        var moneyType = new SignalType(SignalTypes.Money);
        var enchantmentType = new SignalType(SignalTypes.Enchantment);

        var enchantType = new NodeType(
            EnchantTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(EnchantmentPortId, new Port(EnchantmentPortId, enchantmentType)),
            ImmutableDictionary<PortId, Port>.Empty
                .Add(EnchantmentPortId, new Port(EnchantmentPortId, enchantmentType)));

        var testingType = new NodeType(
            TestingTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(EnchantmentPortId, new Port(EnchantmentPortId, enchantmentType)),
            ImmutableDictionary<PortId, Port>.Empty
                .Add(EnchantmentPortId, new Port(EnchantmentPortId, enchantmentType)));

        var sellType = new NodeType(
            SellTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(EnchantmentPortId, new Port(EnchantmentPortId, enchantmentType)),
            ImmutableDictionary<PortId, Port>.Empty.Add(
                MoneyPortId,
                new Port(MoneyPortId, moneyType)));

        var treasuryType = new NodeType(
            TreasuryTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(MoneyPortId, new Port(MoneyPortId, moneyType)),
            ImmutableDictionary<PortId, Port>.Empty);

        var payrollType = new NodeType(
            PayrollTypeId,
            ImmutableDictionary<PortId, Port>.Empty,
            ImmutableDictionary<PortId, Port>.Empty);

        var catalog = new NodeTypeCatalog(
            ImmutableDictionary<NodeTypeId, NodeType>.Empty
                .Add(EnchantTypeId, enchantType)
                .Add(TestingTypeId, testingType)
                .Add(SellTypeId, sellType)
                .Add(TreasuryTypeId, treasuryType)
                .Add(PayrollTypeId, payrollType));

        var graph = new NodeGraph(
            ImmutableDictionary<NodeId, Node>.Empty
                .Add(EnchantNodeId, new Node(EnchantNodeId, EnchantTypeId))
                .Add(TestingNodeId, new Node(TestingNodeId, TestingTypeId))
                .Add(SellNodeId, new Node(SellNodeId, SellTypeId))
                .Add(TreasuryNodeId, new Node(TreasuryNodeId, TreasuryTypeId))
                .Add(PayrollNodeId, new Node(PayrollNodeId, PayrollTypeId)),
            ImmutableDictionary<EdgeId, Edge>.Empty
                .Add(
                    new EdgeId("enchantment-feedback"),
                    new Edge(
                        new PortReference(EnchantNodeId, EnchantmentPortId),
                        new PortReference(EnchantNodeId, EnchantmentPortId)))
                .Add(
                    new EdgeId("enchantment-to-testing"),
                    new Edge(
                        new PortReference(EnchantNodeId, EnchantmentPortId),
                        new PortReference(TestingNodeId, EnchantmentPortId)))
                .Add(
                    new EdgeId("testing-to-sell"),
                    new Edge(
                        new PortReference(TestingNodeId, EnchantmentPortId),
                        new PortReference(SellNodeId, EnchantmentPortId)))
                .Add(
                    new EdgeId("money-to-treasury"),
                    new Edge(
                        new PortReference(SellNodeId, MoneyPortId),
                        new PortReference(TreasuryNodeId, MoneyPortId))));

        var assignments = ImmutableArray.Create(
            new Assignment(ActorId, EnchantNodeId),
            new Assignment(ActorId, TestingNodeId),
            new Assignment(ActorId, SellNodeId),
            new Assignment(ActorId, TreasuryNodeId),
            new Assignment(ActorId, PayrollNodeId));

        var signals = ImmutableDictionary<PortKey, SignalValue>.Empty
            .Add(
                new PortKey(EnchantNodeId, EnchantmentPortId),
                new SignalValue.Enchantment(Volume: 0, Darkness: 0, Fallacy: 0))
            .Add(
                new PortKey(TreasuryNodeId, MoneyPortId),
                new SignalValue.Money(100));

        var timers = ImmutableDictionary<NodeId, int>.Empty
            .Add(PayrollNodeId, nodeConfigs.Payroll.Period);

        return new GameState(
            graph,
            catalog,
            signals,
            actors,
            assignments,
            nodeConfigs,
            ImmutableDictionary<NodeId, double>.Empty,
            timers,
            ImmutableArray<PendingMoneyMove>.Empty,
            Tick: 0);
    }
}
