using System.Text;
using MarlothStrategy.Simulation.Time;

namespace MarlothStrategy.Simulation.Tests;

public sealed class TimePartitionConfigTests
{
    [Fact]
    public void LoadFromBaseDirectory_MatchesCommittedDayWeekMonthDefaults()
    {
        var config = TimePartitionConfigLoader.LoadFromBaseDirectory();

        Assert.Equal(3, config.Units.Length);
        Assert.Equal("day", config.Units[0].Name);
        Assert.Equal(1, config.Units[0].Contains);
        Assert.Equal("tick", config.Units[0].Of);
        Assert.Equal("week", config.Units[1].Name);
        Assert.Equal(7, config.Units[1].Contains);
        Assert.Equal("day", config.Units[1].Of);
        Assert.Equal("month", config.Units[2].Name);
        Assert.Equal(4, config.Units[2].Contains);
        Assert.Equal("week", config.Units[2].Of);
        Assert.Equal("week", config.AdvanceUnit);
        Assert.Equal(1, config.TicksPer("day"));
        Assert.Equal(7, config.TicksPer("week"));
        Assert.Equal(28, config.TicksPer("month"));
        Assert.Equal(7, config.AdvanceTickCount);
    }

    [Fact]
    public void PositionsAt_Tick0_StartsEveryUnitAtOne()
    {
        var config = TimePartitionConfigLoader.LoadFromBaseDirectory();
        var positions = config.PositionsAt(0);

        Assert.Equal(
            new[]
            {
                ("day", 1, (int?)7),
                ("week", 1, 4),
                ("month", 1, null),
            },
            positions.Select(p => (p.Name, p.Index, p.OfParent)).ToArray());
    }

    [Fact]
    public void PositionsAt_ExactWeekMultiple_RollsDayAndWeek()
    {
        var config = TimePartitionConfigLoader.LoadFromBaseDirectory();
        var positions = config.PositionsAt(7);

        Assert.Equal(1, positions[0].Index); // day 1/7
        Assert.Equal(2, positions[1].Index); // week 2/4
        Assert.Equal(1, positions[2].Index); // month 1
    }

    [Fact]
    public void PositionsAt_ExactMonthMultiple_RollsAllUnits()
    {
        var config = TimePartitionConfigLoader.LoadFromBaseDirectory();
        var positions = config.PositionsAt(28);

        Assert.Equal(1, positions[0].Index); // day
        Assert.Equal(1, positions[1].Index); // week
        Assert.Equal(2, positions[2].Index); // month
    }

    [Fact]
    public void BoundariesCrossed_WeekAdvanceFromTick0_IncludesDayAndWeek()
    {
        var config = TimePartitionConfigLoader.LoadFromBaseDirectory();
        var crossed = config.BoundariesCrossed(0, 7);

        Assert.Equal(new[] { "day", "week" }, crossed.ToArray());
    }

    [Fact]
    public void BoundariesCrossed_MonthBoundary_IncludesDayWeekMonth()
    {
        var config = TimePartitionConfigLoader.LoadFromBaseDirectory();
        var crossed = config.BoundariesCrossed(27, 28);

        Assert.Equal(new[] { "day", "week", "month" }, crossed.ToArray());
    }

    [Fact]
    public void BoundariesCrossed_EmptyRange_IsEmpty()
    {
        var config = TimePartitionConfigLoader.LoadFromBaseDirectory();
        Assert.Empty(config.BoundariesCrossed(3, 3));
    }

    [Fact]
    public void BoundariesCrossed_MultipleWeeks_StillListsEachUnitOnce()
    {
        var config = TimePartitionConfigLoader.LoadFromBaseDirectory();
        var crossed = config.BoundariesCrossed(0, 14);

        Assert.Equal(new[] { "day", "week" }, crossed.ToArray());
    }

    [Fact]
    public void Loader_ArbitraryNesting_ComputesTickDurations()
    {
        var path = WriteTempConfig(
            """
            {
              "units": [
                { "name": "beat", "contains": 2, "of": "tick" },
                { "name": "bar", "contains": 4, "of": "beat" },
                { "name": "phrase", "contains": 3, "of": "bar" }
              ],
              "advanceUnit": "bar"
            }
            """);

        var config = TimePartitionConfigLoader.LoadFromFile(path);
        Assert.Equal(2, config.TicksPer("beat"));
        Assert.Equal(8, config.TicksPer("bar"));
        Assert.Equal(24, config.TicksPer("phrase"));
        Assert.Equal(8, config.AdvanceTickCount);
    }

    [Theory]
    [InlineData(
        """
        {
          "units": [
            { "name": "day", "contains": 0, "of": "tick" }
          ],
          "advanceUnit": "day"
        }
        """)]
    [InlineData(
        """
        {
          "units": [
            { "name": "day", "contains": 1, "of": "tick" },
            { "name": "day", "contains": 7, "of": "day" }
          ],
          "advanceUnit": "day"
        }
        """)]
    [InlineData(
        """
        {
          "units": [
            { "name": "week", "contains": 7, "of": "day" }
          ],
          "advanceUnit": "week"
        }
        """)]
    [InlineData(
        """
        {
          "units": [
            { "name": "day", "contains": 1, "of": "tick" },
            { "name": "week", "contains": 7, "of": "day" },
            { "name": "alt", "contains": 2, "of": "day" }
          ],
          "advanceUnit": "week"
        }
        """)]
    [InlineData(
        """
        {
          "units": [
            { "name": "day", "contains": 1, "of": "tick" }
          ],
          "advanceUnit": "tick"
        }
        """)]
    [InlineData(
        """
        {
          "units": [
            { "name": "day", "contains": 1, "of": "tick" }
          ],
          "advanceUnit": "missing"
        }
        """)]
    public void Loader_InvalidHierarchy_FailsFast(string json)
    {
        var path = WriteTempConfig(json);
        var ex = Assert.Throws<InvalidOperationException>(
            () => TimePartitionConfigLoader.LoadFromFile(path));
        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
    }

    private static string WriteTempConfig(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"time-partitions-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json, Encoding.UTF8);
        return path;
    }
}
