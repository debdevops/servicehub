using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Configuration;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Api.Configuration;

public class ProductionConfigurationValidatorTests
{
    private readonly Mock<IHostEnvironment> _environment;
    private readonly Mock<ILogger> _logger;

    public ProductionConfigurationValidatorTests()
    {
        _environment = new Mock<IHostEnvironment>();
        _environment.Setup(e => e.EnvironmentName).Returns("Production");
        _logger = new Mock<ILogger>();
    }

    private static Dictionary<string, string?> BaselineValidConfig() => new()
    {
        ["AllowedHosts"] = "servicehub.example.com",
        ["Security:EncryptionKey"] = new string('a', 64),
        ["SiteUrl"] = "https://servicehub.example.com",
        ["Security:SpaToken:Secret"] = new string('b', 32),
        ["Security:Authentication:Enabled"] = "true",
    };

    private void Validate(Dictionary<string, string?> overrides)
    {
        var dict = BaselineValidConfig();
        foreach (var (key, value) in overrides)
        {
            dict[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        ProductionConfigurationValidator.ValidateProduction(configuration, _environment.Object, _logger.Object);
    }

    [Fact]
    public void ValidateProduction_NamedLegacyApiKey_IsRecognizedAsConfiguredAuthentication()
    {
        // Regression test: HasConfiguredApiKeys used to bind Security:Authentication:ApiKeys via
        // Get<string[]>(), which silently produced no usable value for a named
        // ({ "Key": ..., "Description": ... }) entry — causing production startup to fail with
        // "no usable authentication method configured" even though a valid key was present.
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:Authentication:ApiKeys:0:Key"] = "named-admin-key-12345",
            ["Security:Authentication:ApiKeys:0:Description"] = "Ops bootstrap key",
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateProduction_PlainLegacyApiKey_IsRecognizedAsConfiguredAuthentication()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:Authentication:ApiKeys:0"] = "plain-admin-key-12345",
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateProduction_NoAuthenticationMethodConfigured_Throws()
    {
        var act = () => Validate([]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateProduction_SpaTokenEnabledAlone_Throws()
    {
        // SpaTokenProvider's own doc comment says the SPA token confirms same-origin HTML delivery
        // (CSRF mitigation) but does not identify or authenticate a user — anyone who can fetch the
        // index page can read and replay it. It must never satisfy production auth validation on its
        // own; regression guard for the F2 finding.
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:SpaToken:Enabled"] = "true",
        });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateProduction_EasyAuthEnabled_DoesNotThrow()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:EasyAuth:Enabled"] = "true",
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateProduction_OidcConfigured_DoesNotThrow()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:Oidc:Enabled"] = "true",
            ["Security:Oidc:Authority"] = "https://issuer.example.com",
            ["Security:Oidc:Audience"] = "servicehub-api",
        });

        act.Should().NotThrow();
    }

    // ── RecoveryEvidence:ObservationWindowHours floor (roadmap W1.1) ────────

    [Fact]
    public void ValidateProduction_ObservationWindowBelowFloor_Throws()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:EasyAuth:Enabled"] = "true",
            ["RecoveryEvidence:ObservationWindowHours"] = "0.5",
        });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateProduction_ObservationWindowAtFloor_DoesNotThrow()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:EasyAuth:Enabled"] = "true",
            ["RecoveryEvidence:ObservationWindowHours"] = "1",
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateProduction_ObservationWindowNotConfigured_DefaultsToTwentyFourHours_DoesNotThrow()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:EasyAuth:Enabled"] = "true",
        });

        act.Should().NotThrow();
    }

    // ── CircuitBreakerSuccessRateFloor production floor ─────────────────────
    //
    // The master roadmap §4 calls the success-rate circuit breaker non-configurable-off. It was
    // not: a verified success rate is always >= 0, so a configured 0.0 disabled the breaker
    // outright while every log line and dashboard still described it as active. These four pin
    // the startup policy that closes that.

    [Fact]
    public void ValidateProduction_CircuitBreakerFloorOfZero_Throws()
    {
        // The exact configuration that silently disabled the breaker before this floor existed.
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:EasyAuth:Enabled"] = "true",
            ["RecoveryEvidence:CircuitBreakerSuccessRateFloor"] = "0",
        });

        act.Should().Throw<InvalidOperationException>();

        // The thrown message is deliberately generic ("see logs above"), so assert on the log —
        // an operator who hits this needs to be told which key is wrong, not just that one is.
        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("CircuitBreakerSuccessRateFloor")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ValidateProduction_CircuitBreakerFloorBelowMinimum_Throws()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:EasyAuth:Enabled"] = "true",
            ["RecoveryEvidence:CircuitBreakerSuccessRateFloor"] = "0.1",
        });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateProduction_CircuitBreakerFloorAtMinimum_DoesNotThrow()
    {
        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:EasyAuth:Enabled"] = "true",
            ["RecoveryEvidence:CircuitBreakerSuccessRateFloor"] =
                AutonomyEvaluationWorker.MinimumProductionCircuitBreakerSuccessRateFloor
                    .ToString(CultureInfo.InvariantCulture),
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateProduction_CircuitBreakerFloorNotConfigured_DefaultsAboveTheMinimum_DoesNotThrow()
    {
        // Guards the floor and the default against drifting past each other: a default below the
        // minimum would make every unconfigured production deployment fail to start.
        AutonomyEvaluationWorker.DefaultCircuitBreakerSuccessRateFloor
            .Should().BeGreaterThanOrEqualTo(AutonomyEvaluationWorker.MinimumProductionCircuitBreakerSuccessRateFloor);

        var act = () => Validate(new Dictionary<string, string?>
        {
            ["Security:EasyAuth:Enabled"] = "true",
        });

        act.Should().NotThrow();
    }

    // ── WarnIfCircuitBreakerFloorNonDefault ──────────────────────────────────

    [Fact]
    public void WarnIfCircuitBreakerFloorNonDefault_NonDefaultOutsideDevelopment_LogsWarning()
    {
        _environment.Setup(e => e.EnvironmentName).Returns("Staging");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RecoveryEvidence:CircuitBreakerSuccessRateFloor"] = "0.1",
        });

        ProductionConfigurationValidator.WarnIfCircuitBreakerFloorNonDefault(configuration, _environment.Object, _logger.Object);

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void WarnIfCircuitBreakerFloorNonDefault_DefaultOutsideDevelopment_DoesNotWarn()
    {
        _environment.Setup(e => e.EnvironmentName).Returns("Production");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RecoveryEvidence:CircuitBreakerSuccessRateFloor"] =
                AutonomyEvaluationWorker.DefaultCircuitBreakerSuccessRateFloor
                    .ToString(CultureInfo.InvariantCulture),
        });

        ProductionConfigurationValidator.WarnIfCircuitBreakerFloorNonDefault(configuration, _environment.Object, _logger.Object);

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    // ── WarnIfObservationWindowNonDefault (roadmap W1.1) ─────────────────────

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void WarnIfObservationWindowNonDefault_DevelopmentEnvironment_DoesNotWarn()
    {
        _environment.Setup(e => e.EnvironmentName).Returns("Development");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RecoveryEvidence:ObservationWindowHours"] = "1",
        });

        ProductionConfigurationValidator.WarnIfObservationWindowNonDefault(configuration, _environment.Object, _logger.Object);

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void WarnIfObservationWindowNonDefault_NonDefaultOutsideDevelopment_LogsWarning()
    {
        // Staging, not just Production — the warning applies more broadly than the hard floor.
        _environment.Setup(e => e.EnvironmentName).Returns("Staging");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RecoveryEvidence:ObservationWindowHours"] = "2",
        });

        ProductionConfigurationValidator.WarnIfObservationWindowNonDefault(configuration, _environment.Object, _logger.Object);

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void WarnIfObservationWindowNonDefault_DefaultOutsideDevelopment_DoesNotWarn()
    {
        _environment.Setup(e => e.EnvironmentName).Returns("Production");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RecoveryEvidence:ObservationWindowHours"] = RecoveryLedgerService.DefaultObservationWindowHours.ToString(),
        });

        ProductionConfigurationValidator.WarnIfObservationWindowNonDefault(configuration, _environment.Object, _logger.Object);

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
