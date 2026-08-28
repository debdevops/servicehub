using FluentAssertions;
using ServiceHub.Api.Authorization;

namespace ServiceHub.UnitTests.Api.Authorization;

public sealed class ApiKeyRolesTests
{
    [Fact]
    public void Expand_ViewerRole_ExpandsToReadOnlyScopes()
    {
        var result = ApiKeyRoles.Expand([ApiKeyRoles.Viewer]);

        result.Should().BeEquivalentTo(
        [
            ApiKeyScopes.NamespacesRead,
            ApiKeyScopes.QueuesRead,
            ApiKeyScopes.TopicsRead,
            ApiKeyScopes.SubscriptionsRead,
            ApiKeyScopes.MessagesPeek,
            ApiKeyScopes.DlqRead,
            ApiKeyScopes.AnomaliesRead,
            ApiKeyScopes.DriftFindingsRead,
            ApiKeyScopes.CorrelationFindingsRead,
        ]);
    }

    [Fact]
    public void Expand_OperatorRole_IncludesWriteScopesButNotAudit()
    {
        var result = ApiKeyRoles.Expand([ApiKeyRoles.Operator]);

        result.Should().Contain(ApiKeyScopes.MessagesSend);
        result.Should().Contain(ApiKeyScopes.DlqWrite);
        result.Should().NotContain(ApiKeyScopes.AuditRead);
    }

    [Fact]
    public void Expand_AuditorRole_IncludesAuditReadButNoWriteScopes()
    {
        var result = ApiKeyRoles.Expand([ApiKeyRoles.Auditor]);

        result.Should().Contain(ApiKeyScopes.AuditRead);
        result.Should().NotContain(ApiKeyScopes.MessagesSend);
        result.Should().NotContain(ApiKeyScopes.DlqWrite);
    }

    [Fact]
    public void Expand_RoleNameIsCaseInsensitive()
    {
        var result = ApiKeyRoles.Expand(["viewer"]);

        result.Should().Contain(ApiKeyScopes.NamespacesRead);
    }

    [Fact]
    public void Expand_LiteralScope_PassesThroughUnchanged()
    {
        var result = ApiKeyRoles.Expand([ApiKeyScopes.DlqWrite]);

        result.Should().BeEquivalentTo([ApiKeyScopes.DlqWrite]);
    }

    [Fact]
    public void Expand_MixOfRoleAndLiteralScope_ExpandsBoth()
    {
        var result = ApiKeyRoles.Expand([ApiKeyRoles.Viewer, ApiKeyScopes.AuditRead]);

        result.Should().Contain(ApiKeyScopes.AuditRead);
        result.Should().Contain(ApiKeyScopes.NamespacesRead);
    }

    [Fact]
    public void Expand_UnrecognisedValue_PassesThroughUnchanged()
    {
        var result = ApiKeyRoles.Expand(["not-a-real-role-or-scope"]);

        result.Should().BeEquivalentTo(["not-a-real-role-or-scope"]);
    }

    [Fact]
    public void Expand_DuplicateScopesAcrossRoles_AreDeduplicated()
    {
        var result = ApiKeyRoles.Expand([ApiKeyRoles.Viewer, ApiKeyRoles.Operator]);

        result.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Expand_EmptyInput_ReturnsEmpty()
    {
        var result = ApiKeyRoles.Expand([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_AdminScope_PassesThroughUnchanged_NoRedundantAdminRole()
    {
        // "admin" is a raw ApiKeyScopes value, not a role — Grants() already treats it as a
        // wildcard, so there's no separate "Admin" role bundle to expand it into.
        var result = ApiKeyRoles.Expand([ApiKeyScopes.Admin]);

        result.Should().BeEquivalentTo([ApiKeyScopes.Admin]);
    }
}
