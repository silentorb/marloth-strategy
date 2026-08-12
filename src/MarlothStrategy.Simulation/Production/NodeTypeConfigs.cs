namespace MarlothStrategy.Simulation.Production;

/// <summary>Numeric parameters for the enchant node type (loaded from JSON).</summary>
public sealed record EnchantNodeConfig(
    double BaseThroughput,
    double VolumeDelta,
    double DarknessDelta,
    double FallacyConstant);

/// <summary>Numeric parameters for the sell node type (loaded from JSON).</summary>
public sealed record SellNodeConfig(
    double BaseThroughput,
    double PayoutFloor);

/// <summary>Loaded per-type behavior numerics attached to <see cref="GameState"/>.</summary>
public sealed record NodeTypeConfigs(
    EnchantNodeConfig Enchant,
    SellNodeConfig Sell);
