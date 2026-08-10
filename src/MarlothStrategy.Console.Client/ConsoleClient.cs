using MarlothStrategy.Simulation;

namespace MarlothStrategy.Console.Client;

public static class ConsoleClient
{
    public static void Run(GameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        System.Console.WriteLine("Marloth Strategy — console prototype");
    }
}
