using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public static class MagicAgencySeed
{
    public static readonly NodeTypeId EnchantTypeId = new("enchant");
    public static readonly NodeTypeId SellTypeId = new("sell");

    public static readonly NodeId EnchantNodeId = new("enchant");
    public static readonly NodeId SellNodeId = new("sell");

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

        var moneyType = new SignalType(SignalTypes.Money);
        var enchantmentType = new SignalType(SignalTypes.Enchantment);

        var enchantType = new NodeType(
            EnchantTypeId,
            ImmutableDictionary<PortId, Port>.Empty
                .Add(EnchantmentPortId, new Port(EnchantmentPortId, enchantmentType))
                .Add(MoneyPortId, new Port(MoneyPortId, moneyType)),
            ImmutableDictionary<PortId, Port>.Empty.Add(
                EnchantmentPortId,
                new Port(EnchantmentPortId, enchantmentType)));

        var sellType = new NodeType(
            SellTypeId,
            ImmutableDictionary<PortId, Port>.Empty.Add(
                EnchantmentPortId,
                new Port(EnchantmentPortId, enchantmentType)),
            ImmutableDictionary<PortId, Port>.Empty.Add(
                MoneyPortId,
                new Port(MoneyPortId, moneyType)));

        var catalog = new NodeTypeCatalog(
            ImmutableDictionary<NodeTypeId, NodeType>.Empty
                .Add(EnchantTypeId, enchantType)
                .Add(SellTypeId, sellType));

        var graph = new NodeGraph(
            ImmutableDictionary<NodeId, Node>.Empty
                .Add(EnchantNodeId, new Node(EnchantNodeId, EnchantTypeId))
                .Add(SellNodeId, new Node(SellNodeId, SellTypeId)),
            ImmutableDictionary<EdgeId, Edge>.Empty
                .Add(
                    new EdgeId("enchantment-feedback"),
                    new Edge(
                        new PortReference(EnchantNodeId, EnchantmentPortId),
                        new PortReference(EnchantNodeId, EnchantmentPortId)))
                .Add(
                    new EdgeId("enchantment-to-sell"),
                    new Edge(
                        new PortReference(EnchantNodeId, EnchantmentPortId),
                        new PortReference(SellNodeId, EnchantmentPortId)))
                .Add(
                    new EdgeId("money"),
                    new Edge(
                        new PortReference(SellNodeId, MoneyPortId),
                        new PortReference(EnchantNodeId, MoneyPortId))));

        var assignments = ImmutableArray.Create(
            new Assignment(ActorId, EnchantNodeId),
            new Assignment(ActorId, SellNodeId));

        var signals = ImmutableDictionary<PortKey, SignalValue>.Empty
            .Add(
                new PortKey(EnchantNodeId, EnchantmentPortId),
                new SignalValue.Enchantment(Volume: 0, Darkness: 0, Fallacy: 0))
            .Add(
                new PortKey(EnchantNodeId, MoneyPortId),
                new SignalValue.Money(100));

        return new GameState(
            graph,
            catalog,
            signals,
            actors,
            assignments,
            nodeConfigs,
            ImmutableDictionary<NodeId, double>.Empty,
            Tick: 0);
    }
}
