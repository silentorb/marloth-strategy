namespace MarlothStrategy.Simulation;

/// <summary>
/// Host-supplied configuration shared by Client and Simulation.
/// </summary>
public sealed class GameConfig
{
    /// <summary>
    /// Named scenario preset (e.g. <c>lab01</c>). Null/whitespace means generate a random scenario.
    /// </summary>
    public string? ScenarioPreset { get; init; }

    /// <summary>
    /// RNG seed used when <see cref="ScenarioPreset"/> is unset. Always resolved by the host before play.
    /// </summary>
    public int ScenarioSeed { get; init; }

    public string ScenarioLabel =>
        string.IsNullOrWhiteSpace(ScenarioPreset) ? "random" : ScenarioPreset.Trim();
}
