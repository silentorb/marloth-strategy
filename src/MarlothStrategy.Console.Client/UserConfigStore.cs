using System.Text.Json;
using System.Text.Json.Serialization;
using MarlothStrategy.Simulation.Time;

namespace MarlothStrategy.Console.Client;

/// <summary>
/// Persistent player preferences stored outside committed game data.
/// </summary>
public sealed record UserConfig(string StepResolution);

/// <summary>
/// Loads and saves <see cref="UserConfig"/> as camelCase JSON.
/// Missing files use the time-partition default without creating a file.
/// Invalid files and write failures throw <see cref="InvalidOperationException"/>.
/// </summary>
public static class UserConfigStore
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DefaultPath =>
        Path.Combine(Directory.GetCurrentDirectory(), "config", "user.json");

    public static UserConfig LoadOrDefault(string path, TimePartitionConfig timePartitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(timePartitions);

        if (!File.Exists(path))
        {
            return new UserConfig(timePartitions.DefaultStepResolution);
        }

        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<UserConfigDto>(json, ReadOptions)
                ?? throw new InvalidOperationException(
                    $"User config file is empty or null JSON: '{path}'.");

            if (string.IsNullOrWhiteSpace(dto.StepResolution))
            {
                throw new InvalidOperationException(
                    $"User config '{path}' must declare stepResolution.");
            }

            var stepResolution = dto.StepResolution.Trim();
            if (!timePartitions.TicksPerUnit.ContainsKey(stepResolution))
            {
                throw new InvalidOperationException(
                    $"User config '{path}' stepResolution '{stepResolution}' is not available in the configured time scales.");
            }

            return new UserConfig(stepResolution);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid user config JSON in '{path}'.",
                ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Failed to read user config '{path}'.",
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"Failed to read user config '{path}'.",
                ex);
        }
    }

    public static void Save(string path, UserConfig config, TimePartitionConfig timePartitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(timePartitions);

        if (string.IsNullOrWhiteSpace(config.StepResolution))
        {
            throw new InvalidOperationException(
                $"User config stepResolution must be a non-empty time unit.");
        }

        var stepResolution = config.StepResolution.Trim();
        if (!timePartitions.TicksPerUnit.ContainsKey(stepResolution))
        {
            throw new InvalidOperationException(
                $"Cannot save user config '{path}': stepResolution '{stepResolution}' is not available in the configured time scales.");
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException(
                $"User config path '{path}' has no parent directory.");
        }

        var dto = new UserConfigDto { StepResolution = stepResolution };
        var json = JsonSerializer.Serialize(dto, WriteOptions);
        var tempPath = path + $".{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            throw new InvalidOperationException(
                $"Failed to write user config '{path}'.",
                ex);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp write artifact.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of a temp write artifact.
        }
    }

    private sealed class UserConfigDto
    {
        [JsonRequired]
        public string? StepResolution { get; init; }
    }
}
