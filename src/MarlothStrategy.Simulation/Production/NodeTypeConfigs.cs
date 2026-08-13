namespace MarlothStrategy.Simulation.Production;

/// <summary>Numeric parameters for the enchant node type (loaded from JSON).</summary>
public sealed record EnchantNodeConfig(
    double Effort,
    double VolumeDelta,
    double DarknessDelta,
    double FallacyConstant);

/// <summary>Numeric parameters for the sell node type (loaded from JSON).</summary>
public sealed record SellNodeConfig(
    double Effort,
    double PayoutFloor);

/// <summary>Numeric parameters for the payroll node type (loaded from JSON).</summary>
public sealed record PayrollNodeConfig(
    double DefaultWage,
    int Period);

/// <summary>Loaded per-type behavior numerics attached to <see cref="GameState"/>.</summary>
public sealed record NodeTypeConfigs(
    EnchantNodeConfig Enchant,
    SellNodeConfig Sell,
    PayrollNodeConfig Payroll);

/// <summary>Stat keys and defaults used when applying actor stats to nodes.</summary>
public static class ActorStatKeys
{
    public const string Enchanting = "enchanting";
    public const string Sales = "sales";

    public const double DefaultEnchanting = 1;
    public const double DefaultSales = 1;
}
