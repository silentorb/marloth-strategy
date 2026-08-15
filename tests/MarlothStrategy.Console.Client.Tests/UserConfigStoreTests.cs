using System.Text;
using MarlothStrategy.Console.Client;
using MarlothStrategy.Simulation.Time;

namespace MarlothStrategy.Console.Client.Tests;

public sealed class UserConfigStoreTests
{
    [Fact]
    public void LoadOrDefault_MissingFile_UsesTimePartitionDefault()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "config", "user.json");
        var partitions = TimePartitionConfigLoader.LoadFromBaseDirectory();

        var config = UserConfigStore.LoadOrDefault(path, partitions);

        Assert.Equal(partitions.DefaultStepResolution, config.StepResolution);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsStepResolution()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "config", "user.json");
        var partitions = TimePartitionConfigLoader.LoadFromBaseDirectory();

        UserConfigStore.Save(path, new UserConfig("day"), partitions);
        var loaded = UserConfigStore.LoadOrDefault(path, partitions);

        Assert.True(File.Exists(path));
        Assert.Equal("day", loaded.StepResolution);
        var json = File.ReadAllText(path);
        Assert.Contains("\"stepResolution\": \"day\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_CreatesConfigDirectory()
    {
        using var temp = new TempDir();
        var directory = Path.Combine(temp.Path, "config");
        var path = Path.Combine(directory, "user.json");
        var partitions = TimePartitionConfigLoader.LoadFromBaseDirectory();

        Assert.False(Directory.Exists(directory));
        UserConfigStore.Save(path, new UserConfig("month"), partitions);
        Assert.True(Directory.Exists(directory));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void LoadOrDefault_MalformedJson_FailsFastWithPath()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "user.json");
        File.WriteAllText(path, "{ not json", Encoding.UTF8);
        var partitions = TimePartitionConfigLoader.LoadFromBaseDirectory();

        var ex = Assert.Throws<InvalidOperationException>(
            () => UserConfigStore.LoadOrDefault(path, partitions));
        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadOrDefault_UnknownProperty_FailsFastWithPath()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "user.json");
        File.WriteAllText(
            path,
            """
            {
              "stepResolution": "week",
              "extra": true
            }
            """,
            Encoding.UTF8);
        var partitions = TimePartitionConfigLoader.LoadFromBaseDirectory();

        var ex = Assert.Throws<InvalidOperationException>(
            () => UserConfigStore.LoadOrDefault(path, partitions));
        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadOrDefault_UnavailableScale_FailsFastWithPath()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "user.json");
        File.WriteAllText(
            path,
            """
            {
              "stepResolution": "century"
            }
            """,
            Encoding.UTF8);
        var partitions = TimePartitionConfigLoader.LoadFromBaseDirectory();

        var ex = Assert.Throws<InvalidOperationException>(
            () => UserConfigStore.LoadOrDefault(path, partitions));
        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
        Assert.Contains("century", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_UnavailableScale_FailsFastWithoutWriting()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "config", "user.json");
        var partitions = TimePartitionConfigLoader.LoadFromBaseDirectory();

        var ex = Assert.Throws<InvalidOperationException>(
            () => UserConfigStore.Save(path, new UserConfig("century"), partitions));
        Assert.Contains(path, ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"user-config-{Guid.NewGuid():N}");

        public TempDir()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup for CI hosts.
            }
        }
    }
}
