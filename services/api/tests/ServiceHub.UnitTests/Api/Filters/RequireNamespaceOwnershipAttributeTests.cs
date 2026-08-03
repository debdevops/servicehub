using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ServiceHub.Api.Filters;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Filters;

public sealed class RequireNamespaceOwnershipAttributeTests
{
    private static Namespace CreateTestNamespace(string ownerId) =>
        Namespace.Create(
            "test-ns.servicebus.windows.net",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            ownerId: ownerId).Value;

    private static ActionExecutingContext CreateContext(
        Mock<INamespaceRepository> repo,
        string? ownerId = "owner-a",
        Guid? routeNamespaceId = null,
        Guid? queryNamespaceId = null,
        IReadOnlySet<Guid>? allowedNamespaceIds = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo.Object);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

        if (ownerId is not null)
        {
            httpContext.Items["OwnerId"] = ownerId;
        }

        if (allowedNamespaceIds is not null)
        {
            httpContext.Items["AllowedNamespaceIds"] = allowedNamespaceIds;
        }

        if (queryNamespaceId is not null)
        {
            httpContext.Request.QueryString = new QueryString($"?namespaceId={queryNamespaceId}");
        }

        var routeData = new RouteData();
        if (routeNamespaceId is not null)
        {
            routeData.Values["namespaceId"] = routeNamespaceId.ToString();
        }

        var actionDescriptor = new ActionDescriptor();
        var actionContext = new ActionContext(httpContext, routeData, actionDescriptor);

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    private static (ActionExecutionDelegate Next, Func<bool> WasCalled) CreateNext()
    {
        var called = false;
        Task<ActionExecutedContext> Next()
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>(),
                controller: new object()));
        }

        return (Next, () => called);
    }

    [Fact]
    public async Task IdentifierInRoute_OwnerMatches_CallsNext()
    {
        var ns = CreateTestNamespace("owner-a");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));

        var context = CreateContext(repo, ownerId: "owner-a", routeNamespaceId: ns.Id);
        var (next, wasCalled) = CreateNext();
        var filter = new RequireNamespaceOwnershipAttribute();

        await filter.OnActionExecutionAsync(context, next);

        wasCalled().Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task IdentifierInQueryString_OwnerMatches_CallsNext()
    {
        var ns = CreateTestNamespace("owner-a");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));

        var context = CreateContext(repo, ownerId: "owner-a", queryNamespaceId: ns.Id);
        var (next, wasCalled) = CreateNext();
        var filter = new RequireNamespaceOwnershipAttribute();

        await filter.OnActionExecutionAsync(context, next);

        wasCalled().Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task RouteValueTakesPrecedenceOverQueryString()
    {
        // If both are present, the route value must win — this is the load-bearing ordering
        // that keeps Queues/Topics/Subscriptions (route-bound) and Messages (query-bound)
        // both covered without either accidentally shadowing the other.
        var routeNs = CreateTestNamespace("owner-a");
        var queryNs = CreateTestNamespace("owner-b");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(routeNs.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(routeNs));
        repo.Setup(r => r.GetByIdAsync(queryNs.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(queryNs));

        var context = CreateContext(repo, ownerId: "owner-a", routeNamespaceId: routeNs.Id, queryNamespaceId: queryNs.Id);
        var (next, wasCalled) = CreateNext();
        var filter = new RequireNamespaceOwnershipAttribute();

        await filter.OnActionExecutionAsync(context, next);

        wasCalled().Should().BeTrue();
        repo.Verify(r => r.GetByIdAsync(routeNs.Id, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetByIdAsync(queryNs.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoIdentifierPresent_PassesThroughUntouched()
    {
        var repo = new Mock<INamespaceRepository>();
        var context = CreateContext(repo, ownerId: "owner-a");
        var (next, wasCalled) = CreateNext();
        var filter = new RequireNamespaceOwnershipAttribute();

        await filter.OnActionExecutionAsync(context, next);

        wasCalled().Should().BeTrue();
        context.Result.Should().BeNull();
        repo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NamespaceNotFound_ShortCircuitsWith404()
    {
        var namespaceId = Guid.NewGuid();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("Namespace.NotFound", "not found")));

        var context = CreateContext(repo, ownerId: "owner-a", routeNamespaceId: namespaceId);
        var (next, wasCalled) = CreateNext();
        var filter = new RequireNamespaceOwnershipAttribute();

        await filter.OnActionExecutionAsync(context, next);

        wasCalled().Should().BeFalse();
        var objectResult = context.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task OwnerMismatch_ShortCircuitsWith404()
    {
        var ns = CreateTestNamespace("owner-a");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));

        var context = CreateContext(repo, ownerId: "owner-b", routeNamespaceId: ns.Id);
        var (next, wasCalled) = CreateNext();
        var filter = new RequireNamespaceOwnershipAttribute();

        await filter.OnActionExecutionAsync(context, next);

        wasCalled().Should().BeFalse();
        var objectResult = context.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task SharedWithOwner_CallsNext()
    {
        var ns = CreateTestNamespace("owner-a");
        ns.ShareWith("owner-b");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));

        var context = CreateContext(repo, ownerId: "owner-b", routeNamespaceId: ns.Id);
        var (next, wasCalled) = CreateNext();
        var filter = new RequireNamespaceOwnershipAttribute();

        await filter.OnActionExecutionAsync(context, next);

        wasCalled().Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task AllowListContainsNamespace_CallsNext()
    {
        var ns = CreateTestNamespace("owner-a");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));

        var context = CreateContext(
            repo, ownerId: "owner-a", routeNamespaceId: ns.Id,
            allowedNamespaceIds: new HashSet<Guid> { ns.Id });
        var (next, wasCalled) = CreateNext();
        var filter = new RequireNamespaceOwnershipAttribute();

        await filter.OnActionExecutionAsync(context, next);

        wasCalled().Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task AllowListExcludesNamespace_ShortCircuitsWith404EvenForTrueOwner()
    {
        var ns = CreateTestNamespace("owner-a");
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(ns));

        var context = CreateContext(
            repo, ownerId: "owner-a", routeNamespaceId: ns.Id,
            allowedNamespaceIds: new HashSet<Guid> { Guid.NewGuid() });
        var (next, wasCalled) = CreateNext();
        var filter = new RequireNamespaceOwnershipAttribute();

        await filter.OnActionExecutionAsync(context, next);

        wasCalled().Should().BeFalse();
        var objectResult = context.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }
}
