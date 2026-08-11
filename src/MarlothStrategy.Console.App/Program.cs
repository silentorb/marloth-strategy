using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation;

try
{
    var config = new GameConfig();
    ConsoleClient.Run(config);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error: {ex.Message}");
    Environment.ExitCode = 1;
}
