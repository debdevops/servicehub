using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Enums;
using ServiceHub.IntegrationTests.Infrastructure;
using ServiceHub.Simulator;

namespace ServiceHub.IntegrationTests.Api;

/// <summary>
/// End-to-end tests for bulk replay/purge: real HTTP pipeline, real DI-wired
/// <see cref="ServiceHub.Core.Interfaces.IBulkOperationService"/> and background worker, against
/// the Simulator's in-memory Azure provider — no mocks below the HTTP boundary.
/// </summary>
[Trait("Category", "Simulator")]
public sealed class BulkOperationsControllerTests : IClassFixture<SimulatorWebApplicationFactory>
{
    private readonly SimulatorWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BulkOperationsControllerTests(SimulatorWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedDlqHistoryAsync(Guid namespaceId)
    {
        // Populates the persisted DlqMessages table from the simulator's live in-memory DLQ
        // data — the same manual-scan endpoint the DLQ History page's "Scan Now" button calls.
        var response = await _client.PostAsync($"/api/v1/dlq/scan/{namespaceId}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static void SetIntentHeaders(HttpRequestMessage request, string intent)
    {
        request.Headers.Add("X-ServiceHub-Intent", intent);
        request.Headers.Add("X-ServiceHub-Confirm", "true");
    }

    [Fact]
    public async Task Preview_MatchingActiveMessages_ReturnsCountAndSample()
    {
        _factory.ResetSimulator();
        var namespaceId = SimulatorDataSeeder.AzureNamespaceId;
        await SeedDlqHistoryAsync(namespaceId);

        var request = new BulkOperationPreviewRequest(
            BulkOperationType.Replay,
            new BulkOperationFilterRequest(namespaceId, null, null, null, DlqMessageStatus.Active, null));

        var response = await _client.PostAsJsonAsync("/api/v1/bulk-operations/preview", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalMatched").GetInt32().Should().BeGreaterThan(0);
        json.GetProperty("canExecute").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Create_WithoutIntentHeaders_Returns428()
    {
        _factory.ResetSimulator();
        var namespaceId = SimulatorDataSeeder.AzureNamespaceId;
        await SeedDlqHistoryAsync(namespaceId);

        var request = new BulkOperationCreateRequest(
            BulkOperationType.Replay,
            new BulkOperationFilterRequest(namespaceId, null, null, null, DlqMessageStatus.Active, null));

        var response = await _client.PostAsJsonAsync("/api/v1/bulk-operations", request);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task Create_ThenPoll_JobRunsToCompletionAgainstTheSimulatedProvider()
    {
        _factory.ResetSimulator();
        var namespaceId = SimulatorDataSeeder.AzureNamespaceId;
        await SeedDlqHistoryAsync(namespaceId);

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bulk-operations")
        {
            Content = JsonContent.Create(new BulkOperationCreateRequest(
                BulkOperationType.Replay,
                new BulkOperationFilterRequest(namespaceId, "orders", null, null, DlqMessageStatus.Active, null))),
        };
        SetIntentHeaders(createRequest, "bulk:replay");

        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = created.GetProperty("id").GetGuid();
        created.GetProperty("status").GetString().Should().BeOneOf("Pending", "Running");
        var totalMatched = created.GetProperty("totalMatched").GetInt32();
        totalMatched.Should().BeGreaterThan(0);

        // Poll for the background worker to finish — generous timeout for CI variance.
        JsonElement job = default;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var getResponse = await _client.GetAsync($"/api/v1/bulk-operations/{jobId}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            job = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
            var status = job.GetProperty("status").GetString();
            if (status is "Completed" or "CompletedWithErrors" or "Failed" or "Cancelled")
                break;

            await Task.Delay(250);
        }

        job.GetProperty("status").GetString().Should().BeOneOf("Completed", "CompletedWithErrors");
        job.GetProperty("processedCount").GetInt32().Should().Be(totalMatched);
        job.GetProperty("totalMatched").GetInt32().Should().Be(totalMatched);
    }

    [Fact]
    public async Task Cancel_PendingJob_TransitionsToCancelled()
    {
        _factory.ResetSimulator();
        var namespaceId = SimulatorDataSeeder.AzureNamespaceId;
        await SeedDlqHistoryAsync(namespaceId);

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bulk-operations")
        {
            Content = JsonContent.Create(new BulkOperationCreateRequest(
                BulkOperationType.Replay,
                new BulkOperationFilterRequest(namespaceId, "orders", null, null, DlqMessageStatus.Active, null))),
        };
        SetIntentHeaders(createRequest, "bulk:replay");
        var createResponse = await _client.SendAsync(createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = created.GetProperty("id").GetGuid();

        var cancelResponse = await _client.PostAsync($"/api/v1/bulk-operations/{jobId}/cancel", null);

        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<JsonElement>();
        cancelled.GetProperty("isCancellable").GetBoolean().Should().BeFalse();

        // Eventually settles as Cancelled (or finished first if the worker beat the cancel race).
        JsonElement final = default;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var getResponse = await _client.GetAsync($"/api/v1/bulk-operations/{jobId}");
            final = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
            var status = final.GetProperty("status").GetString();
            if (status is "Completed" or "CompletedWithErrors" or "Failed" or "Cancelled")
                break;
            await Task.Delay(250);
        }

        final.GetProperty("status").GetString().Should().BeOneOf(
            "Cancelled", "Completed", "CompletedWithErrors");
    }

    [Fact]
    public async Task List_ReturnsCreatedJobsForTheOwner()
    {
        _factory.ResetSimulator();
        var namespaceId = SimulatorDataSeeder.AzureNamespaceId;
        await SeedDlqHistoryAsync(namespaceId);

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bulk-operations")
        {
            Content = JsonContent.Create(new BulkOperationCreateRequest(
                BulkOperationType.Replay,
                new BulkOperationFilterRequest(namespaceId, "orders", null, null, DlqMessageStatus.Active, null))),
        };
        SetIntentHeaders(createRequest, "bulk:replay");
        await _client.SendAsync(createRequest);

        var response = await _client.GetAsync($"/api/v1/bulk-operations?namespaceId={namespaceId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Get_UnknownJob_Returns404()
    {
        _factory.ResetSimulator();

        var response = await _client.GetAsync($"/api/v1/bulk-operations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
