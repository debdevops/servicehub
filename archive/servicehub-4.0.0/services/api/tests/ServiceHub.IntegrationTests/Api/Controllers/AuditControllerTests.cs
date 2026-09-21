using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Api.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Controllers;

public sealed class AuditControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string BaseUrl = "/api/v1/audit";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public AuditControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedOldAuditLogAsync(string userIdentity, DateTimeOffset timestamp)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DlqDbContext>();
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = timestamp,
            OwnerId = "__spa__",
            UserIdentity = userIdentity,
            Action = "Test.Seeded",
            Outcome = "Success",
        });
        await db.SaveChangesAsync();
    }

    private async Task<bool> LogExistsAsync(string userIdentity)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DlqDbContext>();
        return await db.AuditLogs.AnyAsync(l => l.UserIdentity == userIdentity);
    }

    [Fact]
    public async Task Purge_MissingIntentHeaders_ShouldReturn428()
    {
        var response = await _client.PostAsJsonAsync($"{BaseUrl}/purge", new PurgeAuditLogsRequest(90));

        response.StatusCode.Should().Be((HttpStatusCode)428);
    }

    [Fact]
    public async Task Purge_WithIntentHeaders_DeletesOnlyExpiredEntries()
    {
        var oldIdentity = $"old-{Guid.NewGuid():N}@test.com";
        var recentIdentity = $"recent-{Guid.NewGuid():N}@test.com";
        await SeedOldAuditLogAsync(oldIdentity, DateTimeOffset.UtcNow.AddDays(-100));
        await SeedOldAuditLogAsync(recentIdentity, DateTimeOffset.UtcNow.AddDays(-1));

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/purge")
        {
            Content = JsonContent.Create(new PurgeAuditLogsRequest(90))
        };
        request.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentPurgeAuditLogs);
        request.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PurgeAuditLogsResponse>(JsonOptions);
        result!.DeletedCount.Should().BeGreaterThanOrEqualTo(1);

        (await LogExistsAsync(oldIdentity)).Should().BeFalse();
        (await LogExistsAsync(recentIdentity)).Should().BeTrue();
    }

    [Fact]
    public async Task Purge_InvalidOlderThanDays_ShouldReturnBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/purge")
        {
            Content = JsonContent.Create(new PurgeAuditLogsRequest(0))
        };
        request.Headers.Add(IntentHeaders.IntentHeaderName, IntentHeaders.IntentPurgeAuditLogs);
        request.Headers.Add(IntentHeaders.ConfirmHeaderName, "true");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
