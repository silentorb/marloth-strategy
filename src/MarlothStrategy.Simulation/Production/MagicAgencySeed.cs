using System.Collections.Immutable;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

public static class MagicAgencySeed
{
    public static readonly NodeTypeId EnchantTypeId = new("enchant");
    public static readonly NodeTypeId TestingTypeId = new("testing");
    public static readonly NodeTypeId DesignTypeId = new("design");
    public static readonly NodeTypeId SellTypeId = new("sell");
    public static readonly NodeTypeId TreasuryTypeId = new("treasury");
    public static readonly NodeTypeId PayrollTypeId = new("payroll");
    public static readonly NodeTypeId MergeTypeId = new("merge");

    public static readonly NodeId EnchantNodeId = new("enchant");
    public static readonly NodeId TestingNodeId = new("testing");
    public static readonly NodeId DesignNodeId = new("design");
    public static readonly NodeId SellNodeId = new("sell");
    public static readonly NodeId TreasuryNodeId = new("treasury");
    public static readonly NodeId PayrollNodeId = new("payroll");
    public static readonly NodeId MergeNodeId = new("merge");

    public static readonly ActorId ActorId = new("intern");
    public static readonly ActorId BossActorId = new("boss");

    public static readonly PortId MoneyPortId = new("money");
    public static readonly PortId EnchantmentPortId = new("enchantment");
    public static readonly PortId PrimaryPortId = new("primary");
    public static readonly PortId SecondaryPortId = new("secondary");

    public static GameState CreateInitialState() =>
        CreateInitialState(
            NodeTypeConfigLoader.LoadFromBaseDirectory(),
            ActorConfigLoader.LoadFromBaseDirectory());

    public static GameState CreateInitialState(NodeTypeConfigs nodeConfigs) =>
        CreateInitialState(nodeConfigs, ActorConfigLoader.LoadFromBaseDirectory());

    public static GameState CreateInitialState(
        NodeTypeConfigs nodeConfigs,
        ImmutableDictionary<ActorId, Actor> actors) =>
        ScenarioBootstrap.CreateFromPreset(ScenarioPresetLoader.Lab01Name, nodeConfigs, actors);
}
