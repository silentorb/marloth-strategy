using DotNetEnv;
using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation;

try
{
    // Optional play-only overrides; missing .env is a no-op (production defaults).
    // Shell-set env vars win over file values. Not used by tests (App is not on the test graph).
    Env.NoClobber().TraversePath().Load();

    var config = new GameConfig();
    ConsoleClient.Run(config);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error: {ex.Message}");
    Environment.ExitCode = 1;
}
