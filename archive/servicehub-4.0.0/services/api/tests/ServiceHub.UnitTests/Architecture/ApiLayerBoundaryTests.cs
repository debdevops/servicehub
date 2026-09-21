using System.Reflection;
using FluentAssertions;
using ServiceHub.Infrastructure.Routing;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Locks in a specific slice of the Api/Infrastructure boundary CLAUDE.md documents
/// ("Controllers depend on Core interfaces, not concrete Infrastructure types"): four
/// controllers (Queues, Topics, Subscriptions, Messages) used to take the concrete
/// <see cref="CloudProviderRouter"/> directly rather than the Core-owned
/// <c>ICloudProviderRouter</c> — this test fails loudly if that regresses.
/// <para>
/// Deliberately narrow, not a blanket "no Infrastructure type in Api" rule: <c>DlqDbContext</c>
/// and a couple of namespace-specific request DTOs are pre-existing, accepted exceptions to the
/// general principle and are out of scope here.
/// </para>
/// </summary>
public sealed class ApiLayerBoundaryTests
{
    [Fact]
    public void NoApiTypeConstructor_TakesConcreteCloudProviderRouter()
    {
        var apiAssembly = typeof(ServiceHub.Api.Controllers.V1.MessagesController).Assembly;

        var offendingConstructors = apiAssembly.GetTypes()
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            .Where(c => c.GetParameters().Any(p => p.ParameterType == typeof(CloudProviderRouter)))
            .Select(c => c.DeclaringType!.FullName)
            .ToList();

        offendingConstructors.Should().BeEmpty(
            "because Api-layer types must depend on ICloudProviderRouter (ServiceHub.Core), " +
            "not the concrete CloudProviderRouter (ServiceHub.Infrastructure) — inject " +
            "ICloudProviderRouter instead");
    }
}
