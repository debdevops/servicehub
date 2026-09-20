using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ServiceHub.Core.Models.Backup;
using FluentAssertions;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Controllers;

/// <summary>Integration tests for on-demand backup creation and listing (roadmap F2).</summary>
public sealed class BackupControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private const string BaseUrl = "/api/v1/admin/backup";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public BackupControllerTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateBackup_ReturnsManifest_WithSqliteSnapshotAndNoKeyMaterial()
    {
        var response = await _client.PostAsync(BaseUrl, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var manifest = await response.Content.ReadFromJsonAsync<BackupManifest>(JsonOptions);

        manifest.Should().NotBeNull();
        manifest!.IntegrityCheck.Should().Be("ok");
        manifest.Sqlite.SizeBytes.Should().BeGreaterThan(0);
        manifest.EncryptionKeyFingerprint.Should().StartWith("sha256:");
        manifest.EncryptionKeyFingerprint.Should().NotContain("test-encryption-key-for-integration-tests-minimum-32bytes");
        manifest.ConsistencyNote.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ListBackups_AfterCreate_IncludesTheNewBackup()
    {
        var createResponse = await _client.PostAsync(BaseUrl, content: null);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.Content.ReadFromJsonAsync<BackupManifest>(JsonOptions);

        var listResponse = await _client.GetAsync(BaseUrl);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var summaries = await listResponse.Content.ReadFromJsonAsync<List<BackupSummary>>(JsonOptions);

        summaries.Should().NotBeNull();
        summaries!.Should().Contain(s => s.BackupId == created!.BackupId);
    }
}
