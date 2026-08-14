using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Loads the random-generation actor pool from <c>config/scenarios/actor-pool.json</c>.
/// Pool membership is an explicit id list, not every file under <c>config/actors/</c>.
/// </summary>
public static class ActorPoolLoader
{
    public const string FileName = "actor-pool.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ImmutableArray<ActorId> LoadFromBaseDirectory() =>
        LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "config", "scenarios"),
            ActorConfigLoader.LoadFromBaseDirectory());

    public static ImmutableArray<ActorId> LoadFromDirectory(
        string directory,
        ImmutableDictionary<ActorId, Actor> actors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(actors);

        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Actor pool file not found: '{path}'.");
        }

        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<ActorPoolDto>(json, JsonOptions)
                ?? throw new InvalidOperationException(
                    $"Actor pool file is empty or null JSON: '{path}'.");

            if (dto.Actors is null || dto.Actors.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Actor pool '{path}' must list at least one actor id.");
            }

            var ids = ImmutableArray.CreateBuilder<ActorId>(dto.Actors.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in dto.Actors)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new InvalidOperationException(
                        $"Actor pool '{path}' has an empty actor id.");
                }

                if (!seen.Add(id))
                {
                    throw new InvalidOperationException(
                        $"Actor pool '{path}' has duplicate actor id '{id}'.");
                }

                var actorId = new ActorId(id);
                if (!actors.ContainsKey(actorId))
                {
                    throw new InvalidOperationException(
                        $"Actor pool '{path}' refers to unknown actor '{id}'.");
                }

                ids.Add(actorId);
            }

            return ids.ToImmutable();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid actor pool JSON in '{path}'.",
                ex);
        }
    }

    private sealed class ActorPoolDto
    {
        [JsonRequired]
        public List<string> Actors { get; init; } = [];
    }
}
