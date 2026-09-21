using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Persistence;

public sealed class SqliteInstanceLockTests : IDisposable
{
    private readonly string _dataDir;

    public SqliteInstanceLockTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), $"servicehub-instancelock-tests-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DlqDatabase:DataDirectory"] = _dataDir
            })
            .Build();

    [Fact]
    public void Constructor_FirstInstance_AcquiresLockAndCreatesDataDirectory()
    {
        using var instanceLock = new SqliteInstanceLock(BuildConfiguration());

        Directory.Exists(_dataDir).Should().BeTrue();
        File.Exists(Path.Combine(_dataDir, ".instance.lock")).Should().BeTrue();
    }

    [Fact]
    public void Constructor_SecondInstanceAgainstSameDirectory_ThrowsWithClearMessage()
    {
        using var first = new SqliteInstanceLock(BuildConfiguration());

        var act = () => new SqliteInstanceLock(BuildConfiguration());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{_dataDir}*")
            .Which.Message.Should().Contain("single writer process");
    }

    [Fact]
    public void Dispose_ThenSecondInstance_AcquiresLockSuccessfully()
    {
        var first = new SqliteInstanceLock(BuildConfiguration());
        first.Dispose();

        var act = () =>
        {
            using var second = new SqliteInstanceLock(BuildConfiguration());
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_DifferentDataDirectories_BothAcquireIndependently()
    {
        var otherDir = Path.Combine(Path.GetTempPath(), $"servicehub-instancelock-tests-{Guid.NewGuid():N}");
        try
        {
            using var first = new SqliteInstanceLock(BuildConfiguration());

            var otherConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DlqDatabase:DataDirectory"] = otherDir
                })
                .Build();

            var act = () =>
            {
                using var second = new SqliteInstanceLock(otherConfiguration);
            };

            act.Should().NotThrow();
        }
        finally
        {
            if (Directory.Exists(otherDir))
            {
                Directory.Delete(otherDir, recursive: true);
            }
        }
    }
}
