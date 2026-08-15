using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarlothStrategy.Simulation.Time;

/// <summary>
/// Loads nested time partition config from <c>config/time-partitions.json</c>
/// relative to <see cref="AppContext.BaseDirectory"/>.
/// </summary>
public static class TimePartitionConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static TimePartitionConfig LoadFromBaseDirectory() =>
        LoadFromFile(Path.Combine(AppContext.BaseDirectory, "config", "time-partitions.json"));

    public static TimePartitionConfig LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Time partition config file not found: '{path}'.");
        }

        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<TimePartitionsDto>(json, JsonOptions)
                ?? throw new InvalidOperationException(
                    $"Time partition config file is empty or null JSON: '{path}'.");
            return ValidateAndBuild(dto, path);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid time partition config JSON in '{path}'.",
                ex);
        }
    }

    private static TimePartitionConfig ValidateAndBuild(TimePartitionsDto dto, string path)
    {
        if (dto.Units is null || dto.Units.Count == 0)
        {
            throw new InvalidOperationException(
                $"Time partition config '{path}' must declare at least one unit.");
        }

        if (string.IsNullOrWhiteSpace(dto.AdvanceUnit))
        {
            throw new InvalidOperationException(
                $"Time partition config '{path}' must declare advanceUnit.");
        }

        var advanceUnit = dto.AdvanceUnit.Trim();
        if (advanceUnit.Equals(TimePartitionConfig.TickUnitName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Time partition config '{path}' advanceUnit must not be '{TimePartitionConfig.TickUnitName}'.");
        }

        var units = new List<TimePartitionUnit>(dto.Units.Count);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unitDto in dto.Units)
        {
            if (unitDto is null)
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' contains a null unit entry.");
            }

            if (string.IsNullOrWhiteSpace(unitDto.Name))
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' has a unit with an empty name.");
            }

            if (string.IsNullOrWhiteSpace(unitDto.Of))
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' unit '{unitDto.Name}' has an empty 'of' reference.");
            }

            if (unitDto.Contains <= 0)
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' unit '{unitDto.Name}' contains must be a positive integer.");
            }

            var name = unitDto.Name.Trim();
            var of = unitDto.Of.Trim();

            if (name.Equals(TimePartitionConfig.TickUnitName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' must not declare a unit named '{TimePartitionConfig.TickUnitName}'.");
            }

            if (!seenNames.Add(name))
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' declares duplicate unit name '{name}'.");
            }

            units.Add(new TimePartitionUnit(name, unitDto.Contains, of));
        }

        // Resolve a single connected acyclic chain rooted at tick.
        var byName = units.ToDictionary(u => u.Name, StringComparer.Ordinal);
        TimePartitionUnit? root = null;
        foreach (var unit in units)
        {
            if (unit.Of.Equals(TimePartitionConfig.TickUnitName, StringComparison.Ordinal))
            {
                if (root is not null)
                {
                    throw new InvalidOperationException(
                        $"Time partition config '{path}' must have exactly one unit with of '{TimePartitionConfig.TickUnitName}'.");
                }

                root = unit;
            }
            else if (!byName.ContainsKey(unit.Of))
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' unit '{unit.Name}' references unknown unit '{unit.Of}'.");
            }
        }

        if (root is null)
        {
            throw new InvalidOperationException(
                $"Time partition config '{path}' must have exactly one unit with of '{TimePartitionConfig.TickUnitName}'.");
        }

        // Each non-tick name may be referenced by at most one parent (single chain).
        var childToParent = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            if (unit.Of.Equals(TimePartitionConfig.TickUnitName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!childToParent.TryAdd(unit.Of, unit.Name))
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' unit '{unit.Of}' is referenced by more than one parent.");
            }
        }

        var ordered = ImmutableArray.CreateBuilder<TimePartitionUnit>(units.Count);
        var ticksPerUnit = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        ticksPerUnit.Add(TimePartitionConfig.TickUnitName, 1);

        var current = root;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (!visited.Add(current.Name))
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' contains a cycle at unit '{current.Name}'.");
            }

            int childTicks;
            if (current.Of.Equals(TimePartitionConfig.TickUnitName, StringComparison.Ordinal))
            {
                childTicks = 1;
            }
            else if (!ticksPerUnit.TryGetValue(current.Of, out childTicks))
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' unit '{current.Name}' is not reachable from '{TimePartitionConfig.TickUnitName}'.");
            }

            long product = (long)childTicks * current.Contains;
            if (product > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Time partition config '{path}' unit '{current.Name}' tick duration overflows Int32.");
            }

            ticksPerUnit[current.Name] = (int)product;
            ordered.Add(current);

            if (!childToParent.TryGetValue(current.Name, out var parentName))
            {
                break;
            }

            current = byName[parentName];
        }

        if (ordered.Count != units.Count)
        {
            var missing = units.Select(u => u.Name).First(n => !visited.Contains(n));
            throw new InvalidOperationException(
                $"Time partition config '{path}' unit '{missing}' is not on the single chain from '{TimePartitionConfig.TickUnitName}'.");
        }

        if (!ticksPerUnit.ContainsKey(advanceUnit))
        {
            throw new InvalidOperationException(
                $"Time partition config '{path}' advanceUnit '{advanceUnit}' is not a declared unit.");
        }

        return new TimePartitionConfig(ordered.MoveToImmutable(), advanceUnit, ticksPerUnit.ToImmutable());
    }

    private sealed class TimePartitionsDto
    {
        [JsonRequired]
        public List<TimePartitionUnitDto?>? Units { get; init; }

        [JsonRequired]
        public string? AdvanceUnit { get; init; }
    }

    private sealed class TimePartitionUnitDto
    {
        [JsonRequired]
        public string? Name { get; init; }

        [JsonRequired]
        public int Contains { get; init; }

        [JsonRequired]
        public string? Of { get; init; }
    }
}
