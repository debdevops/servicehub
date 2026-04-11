using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceHub.Api.Logging;

namespace ServiceHub.UnitTests.Api.Logging;

public class RedactingLoggerProviderTests
{
    private static IConfiguration CreateConfig(string? level = null)
    {
        var dict = new Dictionary<string, string?>();
        if (level != null)
            dict["Logging:LogLevel:Default"] = level;
        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Fact]
    public void CreateLogger_ReturnsRedactingLogger()
    {
        using var provider = new RedactingLoggerProvider(CreateConfig());
        var logger = provider.CreateLogger("TestCategory");
        logger.Should().NotBeNull();
    }

    [Fact]
    public void CreateLogger_AfterDispose_ThrowsObjectDisposed()
    {
        var provider = new RedactingLoggerProvider(CreateConfig());
        provider.Dispose();
        var act = () => provider.CreateLogger("TestCategory");
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var provider = new RedactingLoggerProvider(CreateConfig());
        provider.Dispose();
        var act = () => provider.Dispose();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("Debug", LogLevel.Debug)]
    public void Constructor_ParsesConfiguredLevel(string configLevel, LogLevel expected)
    {
        using var provider = new RedactingLoggerProvider(CreateConfig(configLevel));
        var logger = provider.CreateLogger("Test");
        // If the config level is Warning, Information should be disabled
        // and Warning+ should be enabled
        logger.IsEnabled(expected).Should().BeTrue();
    }

    [Fact]
    public void Constructor_InvalidLevel_DefaultsToInformation()
    {
        using var provider = new RedactingLoggerProvider(CreateConfig("NotALevel"));
        var logger = provider.CreateLogger("Test");
        logger.IsEnabled(LogLevel.Information).Should().BeTrue();
        logger.IsEnabled(LogLevel.Trace).Should().BeFalse();
    }

    [Fact]
    public void Constructor_NoConfig_DefaultsToInformation()
    {
        using var provider = new RedactingLoggerProvider(CreateConfig());
        var logger = provider.CreateLogger("Test");
        logger.IsEnabled(LogLevel.Information).Should().BeTrue();
        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
    }
}

public class RedactingLoggerTests
{
    [Fact]
    public void IsEnabled_AboveMinimum_ReturnsTrue()
    {
        var logger = new RedactingLogger("Test", LogLevel.Information);
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_BelowMinimum_ReturnsFalse()
    {
        var logger = new RedactingLogger("Test", LogLevel.Warning);
        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
    }

    [Fact]
    public void BeginScope_ReturnsNonNullDisposable()
    {
        var logger = new RedactingLogger("Test", LogLevel.Information);
        using var scope = logger.BeginScope("test-scope");
        scope.Should().NotBeNull();
    }

    [Fact]
    public void Log_BelowMinimumLevel_DoesNothing()
    {
        var logger = new RedactingLogger("Test", LogLevel.Warning);
        // Should not throw — it just returns early
        var act = () => logger.Log(LogLevel.Debug, new EventId(0), "test", null, (s, e) => s);
        act.Should().NotThrow();
    }

    [Fact]
    public void Log_AtMinimumLevel_WritesToConsole()
    {
        var logger = new RedactingLogger("Test", LogLevel.Information);
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            logger.Log(LogLevel.Information, new EventId(0), "hello world", null, (s, e) => s);
            var output = sw.ToString();
            output.Should().Contain("INFORMATION");
            output.Should().Contain("hello world");
            output.Should().Contain("[Test]");
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void Log_WithException_IncludesExceptionInfo()
    {
        var logger = new RedactingLogger("Test", LogLevel.Error);
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            var ex = new InvalidOperationException("test-exception");
            logger.Log(LogLevel.Error, new EventId(0), "failure", ex, (s, e) => s);
            var output = sw.ToString();
            output.Should().Contain("test-exception");
            output.Should().Contain("ERROR");
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void Log_RedactsSensitiveData()
    {
        var logger = new RedactingLogger("Test", LogLevel.Information);
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            logger.Log(LogLevel.Information, new EventId(0), "key=SharedAccessKey=mysecret",
                null, (s, e) => s);
            var output = sw.ToString();
            output.Should().NotContain("mysecret");
            output.Should().Contain("REDACTED");
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }
}
