using FluentAssertions;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.AI;

namespace ServiceHub.UnitTests.Infrastructure.AI;

public sealed class FailureFingerprintBuilderTests
{
    private readonly FailureFingerprintBuilder _sut = new();

    private static FailureFeatures CreateFeatures(
        string deadLetterReason = "MaxDeliveryCountExceeded",
        string entityName = "test-queue",
        int deliveryCount = 1,
        string? exceptionType = null,
        string? failureCategory = null)
    {
        return new FailureFeatures
        {
            DeadLetterReason = deadLetterReason,
            EntityName = entityName,
            Provider = CloudProviderType.Azure,
            DeliveryCount = deliveryCount,
            ExceptionType = exceptionType,
            FailureCategory = failureCategory,
        };
    }

    [Fact]
    public async Task ComputeAsync_WithValidFeatures_ReturnsFingerprint()
    {
        var features = CreateFeatures();

        var result = await _sut.ComputeAsync(features);

        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(1);
        result.Value.Hash.Should().NotBeNullOrEmpty();
        result.Value.Features.Should().Be(features);
        result.Value.Confidence.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ComputeAsync_SameFeatures_ProducesSameHash()
    {
        var features = CreateFeatures();

        var result1 = await _sut.ComputeAsync(features);
        var result2 = await _sut.ComputeAsync(features);

        result1.Value.Hash.Should().Be(result2.Value.Hash);
    }

    [Fact]
    public async Task ComputeAsync_DifferentFeatures_ProducesDifferentHash()
    {
        var features1 = CreateFeatures(entityName: "queue1");
        var features2 = CreateFeatures(entityName: "queue2");

        var result1 = await _sut.ComputeAsync(features1);
        var result2 = await _sut.ComputeAsync(features2);

        result1.Value.Hash.Should().NotBe(result2.Value.Hash);
    }

    [Fact]
    public async Task ComputeAsync_IncludesTopTerms()
    {
        var features = CreateFeatures(
            deadLetterReason: "MaxDeliveryCountExceeded",
            entityName: "orders-queue",
            exceptionType: "TimeoutException");

        var result = await _sut.ComputeAsync(features);

        result.IsSuccess.Should().BeTrue();
        result.Value.TopTerms.Should().NotBeEmpty();
        result.Value.TopTerms.Should().Contain(t => t.Contains("entity:"));
        result.Value.TopTerms.Should().Contain(t => t.Contains("reason:"));
    }

    [Fact]
    public async Task ComputeAsync_WithExceptionType_IncreasesConfidence()
    {
        var featuresWithout = CreateFeatures(exceptionType: null);
        var featuresWith = CreateFeatures(exceptionType: "TimeoutException");

        var resultWithout = await _sut.ComputeAsync(featuresWithout);
        var resultWith = await _sut.ComputeAsync(featuresWith);

        resultWith.Value.Confidence.Should().BeGreaterThan(resultWithout.Value.Confidence);
    }

    [Fact]
    public async Task ComputeAsync_WithMultipleDeliveries_IncreasesConfidence()
    {
        var featuresLow = CreateFeatures(deliveryCount: 1);
        var featuresHigh = CreateFeatures(deliveryCount: 5);

        var resultLow = await _sut.ComputeAsync(featuresLow);
        var resultHigh = await _sut.ComputeAsync(featuresHigh);

        resultHigh.Value.Confidence.Should().BeGreaterThan(resultLow.Value.Confidence);
    }

    [Fact]
    public async Task ComputeAsync_ConfidenceNeverExceedsOne()
    {
        var features = CreateFeatures(
            deliveryCount: 100,
            exceptionType: "TimeoutException",
            failureCategory: "Transient");

        var result = await _sut.ComputeAsync(features);

        result.Value.Confidence.Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public async Task ComputeBatchAsync_WithMultipleFeatures_ReturnsFingerprints()
    {
        var featuresList = new[]
        {
            CreateFeatures(entityName: "queue1"),
            CreateFeatures(entityName: "queue2"),
            CreateFeatures(entityName: "queue3"),
        };

        var result = await _sut.ComputeBatchAsync(featuresList);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value[0].Features.EntityName.Should().Be("queue1");
        result.Value[1].Features.EntityName.Should().Be("queue2");
        result.Value[2].Features.EntityName.Should().Be("queue3");
    }

    [Fact]
    public async Task ComputeAsync_WithNullFeatures_Throws()
    {
        var act = () => _sut.ComputeAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ComputeBatchAsync_WithNullList_Throws()
    {
        var act = () => _sut.ComputeBatchAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void CurrentVersion_ReturnsVersion1()
    {
        _sut.CurrentVersion.Should().Be(1);
    }

    [Fact]
    public async Task FailureFingerprint_ToString_FormatsCorrectly()
    {
        var features = CreateFeatures();
        var result = await _sut.ComputeAsync(features);

        var fingerprint = result.Value;
        var str = fingerprint.ToString();

        str.Should().Contain("v1:");
        str.Should().Contain(fingerprint.Hash);
    }
}
