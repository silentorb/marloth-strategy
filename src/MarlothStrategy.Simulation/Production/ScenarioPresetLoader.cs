using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MarlothStrategy.Simulation.Graph;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Loads named scenario presets from <c>config/scenarios/{name}.json</c>
/// relative to <see cref="AppContext.BaseDirectory"/>.
/// </summary>
public static class ScenarioPresetLoader
{
    public const string Lab01Name = "lab01";

    private static readonly Regex PresetNamePattern = new(
        "^[a-zA-Z0-9][a-zA-Z0-9_-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string ScenariosDirectory =>
        Path.Combine(AppContext.BaseDirectory, "config", "scenarios");

    public static ScenarioSpec LoadFromBaseDirectory(string name) =>
        LoadFromDirectory(name, ScenariosDirectory);

    public static ScenarioSpec LoadFromDirectory(string name, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!PresetNamePattern.IsMatch(name))
        {
            throw new InvalidOperationException(
                $"Invalid scenario preset name '{name}'.");
        }

        var path = Path.Combine(directory, $"{name}.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Scenario preset file not found: '{path}'.");
        }

        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<ScenarioPresetDto>(json, JsonOptions)
                ?? throw new InvalidOperationException(
                    $"Scenario preset file is empty or null JSON: '{path}'.");

            return ToSpec(dto, path);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid scenario preset JSON in '{path}'.",
                ex);
        }
    }

    private static ScenarioSpec ToSpec(ScenarioPresetDto dto, string path)
    {
        if (dto.Actors is null || dto.Actors.Count == 0)
        {
            throw new InvalidOperationException(
                $"Scenario preset '{path}' must list at least one actor.");
        }

        var actorIds = ImmutableArray.CreateBuilder<ActorId>(dto.Actors.Count);
        var seenActors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in dto.Actors)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(
                    $"Scenario preset '{path}' has an empty actor id.");
            }

            if (!seenActors.Add(id))
            {
                throw new InvalidOperationException(
                    $"Scenario preset '{path}' has duplicate actor id '{id}'.");
            }

            actorIds.Add(new ActorId(id));
        }

        if (dto.Assignments is null || dto.Assignments.Count == 0)
        {
            throw new InvalidOperationException(
                $"Scenario preset '{path}' must list at least one assignment.");
        }

        var assignments = ImmutableArray.CreateBuilder<Assignment>(dto.Assignments.Count);
        var seenPairs = new HashSet<(string ActorId, string NodeId)>();
        foreach (var row in dto.Assignments)
        {
            if (string.IsNullOrWhiteSpace(row.ActorId) || string.IsNullOrWhiteSpace(row.NodeId))
            {
                throw new InvalidOperationException(
                    $"Scenario preset '{path}' has an assignment missing actorId or nodeId.");
            }

            if (!seenActors.Contains(row.ActorId))
            {
                throw new InvalidOperationException(
                    $"Scenario preset '{path}' assignment actor '{row.ActorId}' is not in the actors list.");
            }

            if (row.Weight <= 0)
            {
                throw new InvalidOperationException(
                    $"Scenario preset '{path}' assignment weight must be positive.");
            }

            if (!seenPairs.Add((row.ActorId, row.NodeId)))
            {
                throw new InvalidOperationException(
                    $"Scenario preset '{path}' has a duplicate assignment for '{row.ActorId}' → '{row.NodeId}'.");
            }

            assignments.Add(new Assignment(new ActorId(row.ActorId), new NodeId(row.NodeId), row.Weight));
        }

        return new ScenarioSpec(dto.IncludeTesting, actorIds.ToImmutable(), assignments.ToImmutable());
    }

    private sealed class ScenarioPresetDto
    {
        [JsonRequired]
        public bool IncludeTesting { get; init; }

        [JsonRequired]
        public List<string> Actors { get; init; } = [];

        [JsonRequired]
        public List<AssignmentDto> Assignments { get; init; } = [];
    }

    private sealed class AssignmentDto
    {
        [JsonRequired]
        public string ActorId { get; init; } = "";

        [JsonRequired]
        public string NodeId { get; init; } = "";

        public decimal Weight { get; init; } = 1m;
    }
}
