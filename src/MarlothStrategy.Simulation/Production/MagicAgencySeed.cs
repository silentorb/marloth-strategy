using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public static class MagicAgencySeed
{
    public static readonly NodeTypeId EnchantTypeId = new("enchant");
    public static readonly NodeTypeId SellTypeId = new("sell");

    public static readonly NodeId EnchantNodeId = new("enchant");
    public static readonly NodeId SellNodeId = new("sell");

    public static readonly ActorId ActorId = new("A1");

    public static readonly PortId MoneyPortId = new("money");
    public static readonly PortId EnchantmentsPortId = new("enchantments");

    public static GameState CreateInitialState()
    {
        var moneyType = new SignalType(SignalTypes.Money);
        var enchantmentsType = new SignalType(SignalTypes.Enchantments);

        var enchantType = new NodeType(
            EnchantTypeId,
            ImmutableDictionary<PortId, Port>.Empty.Add(
                MoneyPortId,
                new Port(MoneyPortId, moneyType)),
            ImmutableDictionary<PortId, Port>.Empty.Add(
                EnchantmentsPortId,
                new Port(EnchantmentsPortId, enchantmentsType)));

        var sellType = new NodeType(
            SellTypeId,
            ImmutableDictionary<PortId, Port>.Empty.Add(
                EnchantmentsPortId,
                new Port(EnchantmentsPortId, enchantmentsType)),
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
                    new EdgeId("enchantments"),
                    new Edge(
                        new PortReference(EnchantNodeId, EnchantmentsPortId),
                        new PortReference(SellNodeId, EnchantmentsPortId)))
                .Add(
                    new EdgeId("money"),
                    new Edge(
                        new PortReference(SellNodeId, MoneyPortId),
                        new PortReference(EnchantNodeId, MoneyPortId))));

        var actors = ImmutableDictionary<ActorId, Actor>.Empty.Add(
            ActorId,
            new Actor(ActorId, Capacity: 1.0m));

        var assignments = ImmutableArray.Create(
            new Assignment(ActorId, EnchantNodeId),
            new Assignment(ActorId, SellNodeId));

        var signals = ImmutableDictionary<PortKey, SignalValue>.Empty
            .Add(
                new PortKey(EnchantNodeId, MoneyPortId),
                new SignalValue.Money(100))
            .Add(
                new PortKey(SellNodeId, EnchantmentsPortId),
                new SignalValue.Enchantments(0));

        return new GameState(
            graph,
            catalog,
            signals,
            actors,
            assignments,
            Tick: 0);
    }
}
