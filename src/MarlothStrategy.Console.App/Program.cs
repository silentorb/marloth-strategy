using DotNetEnv;
using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation;

try
{
    // Optional play-only overrides; missing .env is a no-op (production defaults).
    // Shell-set env vars win over file values. Not used by tests (App is not on the test graph).
    Env.NoClobber().TraversePath().Load();

    var config = ReadGameConfig();
    ConsoleClient.Run(config, UserConfigStore.DefaultPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error: {ex.Message}");
    Environment.ExitCode = 1;
}

static GameConfig ReadGameConfig()
{
    var preset = Environment.GetEnvironmentVariable("SCENARIO_PRESET");
    if (string.IsNullOrWhiteSpace(preset))
    {
        preset = null;
    }
    else
    {
        preset = preset.Trim();
    }

    var seedText = Environment.GetEnvironmentVariable("SCENARIO_SEED");
    int seed;
    if (string.IsNullOrWhiteSpace(seedText))
    {
        seed = Random.Shared.Next();
    }
    else if (!int.TryParse(seedText.Trim(), out seed))
    {
        throw new InvalidOperationException(
            $"SCENARIO_SEED must be an integer; got '{seedText}'.");
    }

    return new GameConfig
    {
        ScenarioPreset = preset,
        ScenarioSeed = seed,
    };
}
