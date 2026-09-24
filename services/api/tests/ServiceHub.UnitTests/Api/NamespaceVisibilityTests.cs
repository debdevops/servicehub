using FluentAssertions;
using Moq;
using ServiceHub.Api.Controllers;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Results;

namespace ServiceHub.UnitTests.Api;

/// <summary>
/// Step 3 of unit 1.6: a restricted caller must not be able to read — or even confirm the
/// existence of — a namespace outside its allow-list or owner.
/// </summary>
public sealed class NamespaceVisibilityTests
{
    private sealed class Probe(string owner, IReadOnlySet<Guid>? allowed) : ApiControllerBase
    {
        protected override string OwnerId => owner;

        protected override IReadOnlySet<Guid>? AllowedNamespaceIds => allowed;

        public Task<Result<Namespace>> Visible(INamespaceRepository repository, Guid id) =>
            GetVisibleNamespaceAsync(repository, id, CancellationToken.None);
    }

    private static Namespace Make(string owner) =>
        Namespace.Create(
            "acme.servicebus.windows.net",
            "Endpoint=sb://acme.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v==",
            ownerId: owner).Value;

    private static INamespaceRepository Repository(Namespace ns) =>
        Mock.Of<INamespaceRepository>(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()) == Task.FromResult(Result.Success(ns)));

    [Fact]
    public async Task The_owner_with_no_restriction_sees_it()
    {
        var ns = Make("owner1");

        (await new Probe("owner1", null).Visible(Repository(ns), ns.Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Another_owner_gets_not_found_not_forbidden()
    {
        var ns = Make("owner1");

        var result = await new Probe("owner2", null).Visible(Repository(ns), ns.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task A_restricted_caller_gets_not_found_for_its_own_owners_namespace_outside_the_allow_list()
    {
        var ns = Make("owner1");

        var result = await new Probe("owner1", new HashSet<Guid> { Guid.NewGuid() }).Visible(Repository(ns), ns.Id);

        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task A_restricted_caller_sees_what_is_on_its_allow_list()
    {
        var ns = Make("owner1");

        (await new Probe("owner1", new HashSet<Guid> { ns.Id }).Visible(Repository(ns), ns.Id)).IsSuccess.Should().BeTrue();
    }
}
