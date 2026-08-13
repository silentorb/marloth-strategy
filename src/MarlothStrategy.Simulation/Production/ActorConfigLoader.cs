using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Loads actor definitions from <c>config/actors/*.json</c>
/// relative to <see cref="AppContext.BaseDirectory"/>.
/// </summary>
public static class ActorConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ImmutableDictionary<ActorId, Actor> LoadFromBaseDirectory() =>
        LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "config", "actors"));

    public static ImmutableDictionary<ActorId, Actor> LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                $"Actor config directory not found: '{directory}'.");
        }

        var actors = ImmutableDictionary.CreateBuilder<ActorId, Actor>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var actor = ReadRequired(path);
            if (!actors.TryAdd(actor.Id, actor))
            {
                throw new InvalidOperationException(
                    $"Duplicate actor id '{actor.Id}' in '{path}'.");
            }
        }

        if (actors.Count == 0)
        {
            throw new InvalidOperationException(
                $"No actor config JSON files found in '{directory}'.");
        }

        return actors.ToImmutable();
    }

    private static Actor ReadRequired(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<ActorConfigDto>(json, JsonOptions)
                ?? throw new InvalidOperationException(
                    $"Actor config file is empty or null JSON: '{path}'.");

            if (string.IsNullOrWhiteSpace(dto.Id))
            {
                throw new InvalidOperationException(
                    $"Actor config '{path}' is missing a non-empty id.");
            }

            var stats = dto.Stats is null
                ? ImmutableDictionary<string, double>.Empty
                : dto.Stats.ToImmutableDictionary(StringComparer.Ordinal);

            return new Actor(new ActorId(dto.Id), dto.Capacity, stats, dto.Wage);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid actor config JSON in '{path}'.",
                ex);
        }
    }

    private sealed class ActorConfigDto
    {
        [JsonRequired]
        public string Id { get; init; } = "";

        [JsonRequired]
        public decimal Capacity { get; init; }

        public Dictionary<string, double>? Stats { get; init; }

        public double? Wage { get; init; }
    }
}
