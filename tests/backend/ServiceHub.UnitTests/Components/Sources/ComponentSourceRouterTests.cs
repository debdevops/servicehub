using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Components;

namespace ServiceHub.UnitTests.Components.Sources;

public sealed class ComponentSourceRouterTests
{
    private sealed class Stub(string key, CloudProviderType provider) : IComponentSource
    {
        public string ComponentKey => key;
        public CloudProviderType Provider => provider;
        public ComponentSourceCapabilities Capabilities => new(false, false);
        public Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<SourcePlacesPage>> ListPlacesAsync(Namespace ns, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<FailedItemsPage>> ListFailedAsync(Namespace ns, string? placeId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<FailedItemDetail?>> GetFailedAsync(Namespace ns, string itemId, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public void It_finds_a_source_by_component_and_adapter()
    {
        var aws = new Stub("step-functions", CloudProviderType.Aws);
        var router = new ComponentSourceRouter([aws, new Stub("workflows", CloudProviderType.Gcp)]);

        router.Find("step-functions", CloudProviderType.Aws).Should().BeSameAs(aws);
        router.All.Should().HaveCount(2);
    }

    [Fact]
    public void A_component_with_no_source_or_on_another_adapter_is_null()
    {
        var router = new ComponentSourceRouter([new Stub("step-functions", CloudProviderType.Aws)]);

        router.Find("step-functions", CloudProviderType.Azure).Should().BeNull();
        router.Find("nope", CloudProviderType.Aws).Should().BeNull();
    }

    [Fact]
    public void No_sources_is_a_working_empty_router()
    {
        var router = new ComponentSourceRouter([]);

        router.All.Should().BeEmpty();
        router.Find("step-functions", CloudProviderType.Aws).Should().BeNull();
    }

    [Fact]
    public void Two_sources_for_the_same_component_and_adapter_fail_at_start_up()
    {
        var act = () => new ComponentSourceRouter([new Stub("step-functions", CloudProviderType.Aws), new Stub("step-functions", CloudProviderType.Aws)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*step-functions/Aws*");
    }

    [Fact]
    public void The_same_component_key_on_two_adapters_is_allowed()
    {
        var act = () => new ComponentSourceRouter([new Stub("events", CloudProviderType.Aws), new Stub("events", CloudProviderType.Gcp)]);

        act.Should().NotThrow();
    }
}
