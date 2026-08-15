namespace MarlothStrategy.Simulation.Production;

/// <summary>Numeric parameters for the enchant node type (loaded from JSON).</summary>
public sealed record EnchantNodeConfig(
    double Effort,
    double VolumeDelta,
    double DarknessDelta,
    double FallacyConstant,
    double DesignDarknessDelta);

/// <summary>Numeric parameters for the testing node type (loaded from JSON).</summary>
public sealed record TestingNodeConfig(
    double Effort,
    double FallacyReduction);

/// <summary>Numeric parameters for the sell node type (loaded from JSON).</summary>
public sealed record SellNodeConfig(
    double Effort,
    double PayoutFloor);

/// <summary>Numeric parameters for the treasury node type (loaded from JSON).</summary>
public sealed record TreasuryNodeConfig(double Effort);

/// <summary>Calendar schedule for opening and closing a payroll obligation window.</summary>
public sealed record PayrollScheduleConfig(
    string PeriodUnit,
    string PositionUnit,
    int StartLead,
    int DueDay);

/// <summary>Numeric parameters for the payroll node type (loaded from JSON).</summary>
public sealed record PayrollNodeConfig(
    PayrollScheduleConfig Schedule,
    double BaseEffort,
    double PerActorEffort);

/// <summary>Numeric parameters for the merge node type (loaded from JSON).</summary>
public sealed record MergeNodeConfig(double Effort);

/// <summary>Numeric parameters for the design node type (loaded from JSON).</summary>
public sealed record DesignNodeConfig(
    double Effort,
    double DesignDelta,
    double DarknessReduction);

/// <summary>Loaded per-type behavior numerics attached to <see cref="GameState"/>.</summary>
public sealed record NodeTypeConfigs(
    EnchantNodeConfig Enchant,
    TestingNodeConfig Testing,
    SellNodeConfig Sell,
    TreasuryNodeConfig Treasury,
    PayrollNodeConfig Payroll,
    MergeNodeConfig Merge,
    DesignNodeConfig Design);

/// <summary>Stat keys and defaults used when applying actor stats to nodes.</summary>
public static class ActorStatKeys
{
    public const string Enchanting = "enchanting";
    public const string Testing = "testing";
    public const string Sales = "sales";
    public const string Treasury = "treasury";
    public const string Payroll = "payroll";
    public const string Merging = "merging";
    public const string Designing = "designing";

    public const double DefaultEnchanting = 1;
    public const double DefaultTesting = 1;
    public const double DefaultSales = 1;
    public const double DefaultTreasury = 1;
    public const double DefaultPayroll = 1;
    public const double DefaultMerging = 1;
    public const double DefaultDesigning = 1;
}
