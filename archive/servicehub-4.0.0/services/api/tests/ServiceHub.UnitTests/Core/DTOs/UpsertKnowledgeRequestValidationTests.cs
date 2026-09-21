using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using ServiceHub.Core.DTOs.Requests;

namespace ServiceHub.UnitTests.Core.DTOs;

public sealed class UpsertKnowledgeRequestValidationTests
{
    private static IList<ValidationResult> Validate(UpsertKnowledgeRequest request)
    {
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);
        return results;
    }

    private static UpsertKnowledgeRequest Valid(string rootCause = "Some root cause") => new()
    {
        RootCause = rootCause,
    };

    [Fact]
    public void Validate_MinimalValidRequest_PassesValidation()
    {
        var results = Validate(Valid());

        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingRootCause_ReturnsValidationError()
    {
        var request = new UpsertKnowledgeRequest { RootCause = "" };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(UpsertKnowledgeRequest.RootCause)));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("just some text")]
    public void Validate_InvalidRunbookLink_ReturnsValidationError(string runbookLink)
    {
        var request = Valid() with { RunbookLink = runbookLink };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(UpsertKnowledgeRequest.RunbookLink)));
    }

    [Theory]
    [InlineData("https://wiki.example.com/runbooks/db-timeout")]
    [InlineData("http://internal-wiki/runbook")]
    public void Validate_ValidRunbookLink_PassesValidation(string runbookLink)
    {
        var request = Valid() with { RunbookLink = runbookLink };

        var results = Validate(request);

        results.Should().NotContain(r => r.MemberNames.Contains(nameof(UpsertKnowledgeRequest.RunbookLink)));
    }

    [Theory]
    [InlineData("Safe")]
    [InlineData("Unsafe")]
    [InlineData("Investigate")]
    public void Validate_AllowedReplayGuidance_PassesValidation(string replayGuidance)
    {
        var request = Valid() with { ReplayGuidance = replayGuidance };

        var results = Validate(request);

        results.Should().NotContain(r => r.MemberNames.Contains(nameof(UpsertKnowledgeRequest.ReplayGuidance)));
    }

    [Theory]
    [InlineData("safe")]
    [InlineData("Maybe")]
    [InlineData("SAFE")]
    public void Validate_InvalidReplayGuidance_ReturnsValidationError(string replayGuidance)
    {
        var request = Valid() with { ReplayGuidance = replayGuidance };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(UpsertKnowledgeRequest.ReplayGuidance)));
    }

    [Fact]
    public void Validate_RootCauseExceedsMaxLength_ReturnsValidationError()
    {
        var request = Valid() with { RootCause = new string('a', 4097) };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(UpsertKnowledgeRequest.RootCause)));
    }

    [Fact]
    public void Validate_TagsExceedsMaxLength_ReturnsValidationError()
    {
        var request = Valid() with { Tags = new string('a', 2049) };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(UpsertKnowledgeRequest.Tags)));
    }

    [Fact]
    public void Validate_ChangedByExceedsMaxLength_ReturnsValidationError()
    {
        var request = Valid() with { ChangedBy = new string('a', 257) };

        var results = Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(UpsertKnowledgeRequest.ChangedBy)));
    }

    [Fact]
    public void Validate_ChangedByOmitted_PassesValidation()
    {
        var results = Validate(Valid());

        results.Should().NotContain(r => r.MemberNames.Contains(nameof(UpsertKnowledgeRequest.ChangedBy)));
    }
}
