using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarlothStrategy.Simulation.Production;

/// <summary>
/// Loads heterogeneous node-type numeric configs from <c>config/node-types/*.json</c>
/// relative to <see cref="AppContext.BaseDirectory"/>.
/// </summary>
public static class NodeTypeConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static NodeTypeConfigs LoadFromBaseDirectory() =>
        LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "config", "node-types"));

    public static NodeTypeConfigs LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var enchant = ReadRequired<EnchantNodeConfigDto, EnchantNodeConfig>(
            Path.Combine(directory, "enchant.json"),
            dto => new EnchantNodeConfig(
                dto.Effort,
                dto.VolumeDelta,
                dto.DarknessDelta,
                dto.FallacyConstant,
                dto.DesignDarknessDelta));

        var testing = ReadRequired<TestingNodeConfigDto, TestingNodeConfig>(
            Path.Combine(directory, "testing.json"),
            dto => new TestingNodeConfig(dto.Effort, dto.FallacyReduction));

        var sell = ReadRequired<SellNodeConfigDto, SellNodeConfig>(
            Path.Combine(directory, "sell.json"),
            dto => new SellNodeConfig(dto.Effort, dto.PayoutFloor));

        var treasury = ReadRequired<TreasuryNodeConfigDto, TreasuryNodeConfig>(
            Path.Combine(directory, "treasury.json"),
            dto => new TreasuryNodeConfig(dto.Effort));

        var payroll = ReadRequired<PayrollNodeConfigDto, PayrollNodeConfig>(
            Path.Combine(directory, "payroll.json"),
            dto => new PayrollNodeConfig(dto.DefaultWage, dto.Period, dto.Effort));

        var merge = ReadRequired<MergeNodeConfigDto, MergeNodeConfig>(
            Path.Combine(directory, "merge.json"),
            dto => new MergeNodeConfig(dto.Effort));

        var design = ReadRequired<DesignNodeConfigDto, DesignNodeConfig>(
            Path.Combine(directory, "design.json"),
            dto => new DesignNodeConfig(dto.Effort, dto.DesignDelta, dto.DarknessReduction));

        return new NodeTypeConfigs(enchant, testing, sell, treasury, payroll, merge, design);
    }

    private static TConfig ReadRequired<TDto, TConfig>(
        string path,
        Func<TDto, TConfig> map)
        where TDto : class
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Node type config file not found: '{path}'.");
        }

        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<TDto>(json, JsonOptions)
                ?? throw new InvalidOperationException(
                    $"Node type config file is empty or null JSON: '{path}'.");
            return map(dto);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid node type config JSON in '{path}'.",
                ex);
        }
    }

    private sealed class EnchantNodeConfigDto
    {
        [JsonRequired]
        public double Effort { get; init; }

        [JsonRequired]
        public double VolumeDelta { get; init; }

        [JsonRequired]
        public double DarknessDelta { get; init; }

        [JsonRequired]
        public double FallacyConstant { get; init; }

        [JsonRequired]
        public double DesignDarknessDelta { get; init; }
    }

    private sealed class TestingNodeConfigDto
    {
        [JsonRequired]
        public double Effort { get; init; }

        [JsonRequired]
        public double FallacyReduction { get; init; }
    }

    private sealed class SellNodeConfigDto
    {
        [JsonRequired]
        public double Effort { get; init; }

        [JsonRequired]
        public double PayoutFloor { get; init; }
    }

    private sealed class TreasuryNodeConfigDto
    {
        [JsonRequired]
        public double Effort { get; init; }
    }

    private sealed class PayrollNodeConfigDto
    {
        [JsonRequired]
        public double DefaultWage { get; init; }

        [JsonRequired]
        public int Period { get; init; }

        [JsonRequired]
        public double Effort { get; init; }
    }

    private sealed class MergeNodeConfigDto
    {
        [JsonRequired]
        public double Effort { get; init; }
    }

    private sealed class DesignNodeConfigDto
    {
        [JsonRequired]
        public double Effort { get; init; }

        [JsonRequired]
        public double DesignDelta { get; init; }

        [JsonRequired]
        public double DarknessReduction { get; init; }
    }
}
