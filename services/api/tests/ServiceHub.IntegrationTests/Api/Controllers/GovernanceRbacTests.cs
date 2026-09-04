using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using ServiceHub.Api.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Controllers;

/// <summary>
/// Executable specification for roadmap item W3.2 ("make the negative test runnable"): "Viewer is
/// denied an Operator action on the same data" — the one negative RBAC assertion the 2026-08-30
/// E2E campaign could not exercise, now running in CI on every build.
/// <para>
/// This is also the executable proof for W3.1 ("separate identity from owner scope"): the admin
/// and viewer credentials below share one owner partition (<see cref="ServiceHub.Core.Entities.Namespace.SpaOwnerId"/>)
/// and one namespace, differentiated only by their <see cref="ServiceHub.Core.Entities.GovernanceGrant"/> —
/// exactly the "two credentials, two roles, one owner scope" case the roadmap names as
/// structurally unproven.
/// </para>
/// </summary>
public sealed class GovernanceRbacTests : IDisposable
{
    private const string QueueName = "governance-rbac-test-queue";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Deliberately not an IClassFixture: each test grants a fleet-wide Admin role for the same
    // grantee identity, which would conflict (409, "an active grant already exists for the exact
    // same scope") against a factory shared across test methods. xUnit already creates one test
    // class instance per test method, so a fresh factory here means a fresh, isolated data
    // directory and an empty GovernanceGrants table per test — no shared-fixture contamination.
    private readonly GovernanceRbacWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Viewer_IsDeniedAnOperatorAction_OnTheSameNamespace_ThatAdminCanReach()
    {
        using var adminClient = _factory.CreateAdminClient();
        using var viewerClient = _factory.CreateViewerClient();

        var namespaceId = await CreateNamespaceAsync(adminClient);

        // Both grants are created while GovernanceGrants is still empty for this fresh owner —
        // GovernanceAccessEvaluator treats an owner with zero grants as "not yet activated" and
        // lets every caller through (the documented bootstrap-safety behavior), which is exactly
        // what lets the very first Admin grant be self-service. The second call (viewer's grant)
        // now runs under an *active* Governance owner and is authorized by the Admin grant the
        // first call just created — proving the grant sequence itself, not just its end state.
        await GrantAsync(adminClient, GovernanceRbacWebApplicationFactory.AdminGranteeIdentity, GranteeKind.ApiKey, GovernanceRole.Admin, namespaceId: null, pillarKind: null);
        await GrantAsync(adminClient, GovernanceRbacWebApplicationFactory.ViewerGranteeIdentity, GranteeKind.ApiKey, GovernanceRole.Viewer, namespaceId, PillarKind.Recover);

        // Same data (this exact namespace), same endpoint, same request shape — only the calling
        // identity's Governance role differs.
        var viewerResponse = await SendReplayRequestAsync(viewerClient, namespaceId);
        viewerResponse.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            because: "a Viewer-only grant must not satisfy an endpoint requiring GovernanceRole.Operator");

        // The Governance layer's own denial message (GovernanceAccessEvaluator.EvaluateAsync),
        // not just any 403 — ties this failure to the RBAC check specifically rather than an
        // unrelated Forbidden (e.g. a missing API-key scope) that happens to share the status code.
        var viewerBody = await viewerResponse.Content.ReadAsStringAsync();
        viewerBody.Should().ContainAll("Governance role", "Viewer", "Operator");

        var adminResponse = await SendReplayRequestAsync(adminClient, namespaceId);
        adminResponse.StatusCode.Should().NotBe(
            HttpStatusCode.Forbidden,
            because: "the Admin grant must satisfy the same Operator-gated endpoint over the same namespace — " +
                     "a 403 here would mean the check fails closed for everyone, not just under-privileged callers");
    }

    [Fact]
    public async Task Viewer_IsDeniedAnOperatorAction_EvenWhenOwnerHasASeededFleetWideAdminGrant()
    {
        // Reproduces the real shape GovernanceGrantSeeder creates on every deployment's first
        // boot: a fleet-wide Admin grant whose GranteeIdentity equals the shared OwnerId itself
        // ("__spa__" in production; Namespace.SpaOwnerId here too, since it's a hardcoded
        // constant, not per-test-instance data). The two tests above never seed this shape — their
        // "admin" grant names the admin credential's own resolved identity, not OwnerId — so they
        // could not have caught the live-verified 2026-09-04 defect where this fleet-wide grant
        // silently topped up a Viewer-only credential to Admin, making per-identity restriction a
        // no-op for every real deployment's primary owner.
        using var adminClient = _factory.CreateAdminClient();
        using var viewerClient = _factory.CreateViewerClient();

        var namespaceId = await CreateNamespaceAsync(adminClient);

        await GrantAsync(adminClient, ServiceHub.Core.Entities.Namespace.SpaOwnerId, GranteeKind.User, GovernanceRole.Admin, namespaceId: null, pillarKind: null);
        await GrantAsync(adminClient, GovernanceRbacWebApplicationFactory.ViewerGranteeIdentity, GranteeKind.ApiKey, GovernanceRole.Viewer, namespaceId, PillarKind.Recover);

        var viewerResponse = await SendReplayRequestAsync(viewerClient, namespaceId);
        viewerResponse.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            because: "a Viewer-only grant for this specific identity must not be topped up by the seeded owner-level Admin grant");
    }

    [Fact]
    public async Task Viewer_CanStillReadTheSameNamespace_OperatorGrantOnlyRestrictsWrites()
    {
        using var adminClient = _factory.CreateAdminClient();
        using var viewerClient = _factory.CreateViewerClient();

        var namespaceId = await CreateNamespaceAsync(adminClient);
        await GrantAsync(adminClient, GovernanceRbacWebApplicationFactory.AdminGranteeIdentity, GranteeKind.ApiKey, GovernanceRole.Admin, namespaceId: null, pillarKind: null);
        await GrantAsync(adminClient, GovernanceRbacWebApplicationFactory.ViewerGranteeIdentity, GranteeKind.ApiKey, GovernanceRole.Viewer, namespaceId, PillarKind.Recover);

        // Namespace detail carries no [RequireGovernanceRole] at all (read routes are gated by
        // scope/ownership only) — a Viewer grant must not accidentally block it. Read access to
        // the same data both credentials share is the point being demonstrated, not a specific
        // status code, so this only needs to prove the Viewer isn't blocked the way it was above.
        var response = await viewerClient.GetAsync($"/api/v1/namespaces/{namespaceId}");
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            because: "a Governance role differentiates write actions, not visibility into data the credential's owner scope already covers");
    }

    [Fact]
    public async Task Viewer_IsStillDenied_WhenAdminGrantsTheApiKeyGranteeWithoutTheApiKeyPrefix()
    {
        // Reproduces the live-verified 2026-09-04 defect: the Governance Grants UI's free-text
        // "Grantee identity" field tells the admin to type "ApiKey:name" but enforces nothing —
        // typing just the bare key Description (exactly what an admin reading the key list would
        // naturally type) created a grant that looked correctly scoped in the UI but never matched
        // ActorIdentityResolver's "ApiKey:{name}"-prefixed identity at evaluation time. Critically,
        // this must also seed the real production shape — a fleet-wide Admin grant whose
        // GranteeIdentity equals OwnerId itself (GovernanceGrantSeeder's convention, reproduced by
        // the sibling test above) — because without it, the malformed grant's non-match falls
        // through to "no applicable grant" and is denied by accident, not by the fix. With the
        // seed present, a non-matching viewer grant instead falls through to the owner-level Admin
        // grant and replays a real message, exactly as it did live: the danger isn't "no grant
        // found," it's "the wrong, much more permissive grant found." GovernanceGrantService.GrantAsync
        // now normalizes an ApiKey-kind grantee to always carry the prefix regardless of what the
        // caller typed, so this must still deny even with that seed present.
        using var adminClient = _factory.CreateAdminClient();
        using var viewerClient = _factory.CreateViewerClient();

        var namespaceId = await CreateNamespaceAsync(adminClient);

        await GrantAsync(adminClient, ServiceHub.Core.Entities.Namespace.SpaOwnerId, GranteeKind.User, GovernanceRole.Admin, namespaceId: null, pillarKind: null);
        // Deliberately the bare Description, not GovernanceRbacWebApplicationFactory.ViewerGranteeIdentity
        // (which is already correctly prefixed) — this is the exact malformed input a human admin
        // produces, not a pre-corrected test fixture.
        await GrantAsync(adminClient, "Governance RBAC test - viewer", GranteeKind.ApiKey, GovernanceRole.Viewer, namespaceId, PillarKind.Recover);

        var viewerResponse = await SendReplayRequestAsync(viewerClient, namespaceId);
        viewerResponse.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            because: "an ApiKey-kind grant submitted without the 'ApiKey:' prefix must be normalized " +
                     "server-side to still match and restrict this credential — falling through to the " +
                     "seeded owner-level Admin grant instead would silently escalate, not merely fail open");
    }

    private static async Task<HttpResponseMessage> SendReplayRequestAsync(HttpClient client, Guid namespaceId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/messages/replay?namespaceId={namespaceId}&sequenceNumber=1&entityName={QueueName}");
        request.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentReplayMessage);
        request.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");
        return await client.SendAsync(request);
    }

    private static async Task GrantAsync(
        HttpClient client, string granteeIdentity, GranteeKind granteeKind, GovernanceRole role,
        Guid? namespaceId, PillarKind? pillarKind)
    {
        var request = new GrantGovernanceRoleRequest(granteeIdentity, granteeKind, role, namespaceId, pillarKind);
        var response = await client.PostAsJsonAsync("/api/v1/governance/grants", request, JsonOptions);
        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            because: $"seeding the '{role}' grant for '{granteeIdentity}' must succeed for the rest of this test to be meaningful");
    }

    private static async Task<Guid> CreateNamespaceAsync(HttpClient adminClient)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var request = new CreateNamespaceRequest(
            Name: $"governance-rbac-{unique}.servicebus.windows.net",
            ConnectionString: $"Endpoint=sb://governance-rbac-{unique}.servicebus.windows.net/;SharedAccessKeyName=ServiceHubPolicy;SharedAccessKey=testkey123456789=",
            AuthType: ConnectionAuthType.ConnectionString);

        var createResponse = await adminClient.PostAsJsonAsync("/api/v1/namespaces", request);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, "namespace seeding must succeed for the test to be meaningful");

        var created = await createResponse.Content.ReadFromJsonAsync<NamespaceResponse>(JsonOptions);
        return created!.Id;
    }
}
