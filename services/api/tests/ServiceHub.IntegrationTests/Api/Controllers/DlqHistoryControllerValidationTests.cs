using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.IntegrationTests.Infrastructure;

namespace ServiceHub.IntegrationTests.Api.Controllers;

/// <summary>
/// Regression coverage for the DLQ history status/notes endpoints' record-DTO validation.
/// Prior to the fix, `UpdateDlqStatusRequest`/`UpdateDlqNotesRequest` carried their
/// `[StringLength]` attribute on the record's synthesized property (`[property: ...]`)
/// rather than the primary constructor parameter. ASP.NET Core's `[ApiController]` model
/// binding validates records via their constructor parameters, so that attribute placement
/// was silently ignored — over-long `Notes` values were accepted instead of failing
/// automatic model validation with a 400.
/// </summary>
public sealed class DlqHistoryControllerValidationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DlqHistoryControllerValidationTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UpdateStatus_WithNotesOverMaxLength_ReturnsBadRequest()
    {
        var request = new UpdateDlqStatusRequest(DlqMessageStatus.Archived, new string('x', 4097));

        var response = await _client.PostAsJsonAsync("/api/v1/dlq/history/1/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateNotes_WithNotesOverMaxLength_ReturnsBadRequest()
    {
        var request = new UpdateDlqNotesRequest(new string('x', 4097));

        var response = await _client.PostAsJsonAsync("/api/v1/dlq/history/1/notes", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
