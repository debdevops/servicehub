using FluentAssertions;
using ServiceHub.Api.Authorization;

namespace ServiceHub.UnitTests.Api.Authorization;

public class ApiKeyScopesTests
{
    [Fact]
    public void Grants_AdminScope_ShouldGrantAnything()
    {
        ApiKeyScopes.Grants(ApiKeyScopes.Admin, ApiKeyScopes.NamespacesRead).Should().BeTrue();
        ApiKeyScopes.Grants(ApiKeyScopes.Admin, ApiKeyScopes.NamespacesWrite).Should().BeTrue();
        ApiKeyScopes.Grants(ApiKeyScopes.Admin, ApiKeyScopes.MessagesSend).Should().BeTrue();
        ApiKeyScopes.Grants(ApiKeyScopes.Admin, ApiKeyScopes.QueuesRead).Should().BeTrue();
        ApiKeyScopes.Grants(ApiKeyScopes.Admin, ApiKeyScopes.DlqRead).Should().BeTrue();
        ApiKeyScopes.Grants(ApiKeyScopes.Admin, ApiKeyScopes.DlqWrite).Should().BeTrue();
    }

    [Fact]
    public void Grants_ExactMatch_ShouldBeTrue()
    {
        ApiKeyScopes.Grants(ApiKeyScopes.NamespacesRead, ApiKeyScopes.NamespacesRead).Should().BeTrue();
        ApiKeyScopes.Grants(ApiKeyScopes.MessagesSend, ApiKeyScopes.MessagesSend).Should().BeTrue();
        ApiKeyScopes.Grants(ApiKeyScopes.DlqRead, ApiKeyScopes.DlqRead).Should().BeTrue();
    }

    [Fact]
    public void Grants_NonMatchingScope_ShouldBeFalse()
    {
        ApiKeyScopes.Grants(ApiKeyScopes.NamespacesRead, ApiKeyScopes.NamespacesWrite).Should().BeFalse();
        ApiKeyScopes.Grants(ApiKeyScopes.MessagesSend, ApiKeyScopes.MessagesPeek).Should().BeFalse();
        ApiKeyScopes.Grants(ApiKeyScopes.DlqRead, ApiKeyScopes.DlqWrite).Should().BeFalse();
    }

    [Fact]
    public void Grants_CaseInsensitive_ShouldBeTrue()
    {
        ApiKeyScopes.Grants("ADMIN", ApiKeyScopes.NamespacesRead).Should().BeTrue();
        ApiKeyScopes.Grants("Admin", ApiKeyScopes.TopicsRead).Should().BeTrue();
    }

    [Fact]
    public void ScopeConstants_ShouldHaveExpectedValues()
    {
        ApiKeyScopes.NamespacesRead.Should().Be("namespaces:read");
        ApiKeyScopes.NamespacesWrite.Should().Be("namespaces:write");
        ApiKeyScopes.MessagesSend.Should().Be("messages:send");
        ApiKeyScopes.MessagesPeek.Should().Be("messages:peek");
        ApiKeyScopes.QueuesRead.Should().Be("queues:read");
        ApiKeyScopes.TopicsRead.Should().Be("topics:read");
        ApiKeyScopes.SubscriptionsRead.Should().Be("subscriptions:read");
        ApiKeyScopes.AnomaliesRead.Should().Be("anomalies:read");
        ApiKeyScopes.DlqRead.Should().Be("dlq:read");
        ApiKeyScopes.DlqWrite.Should().Be("dlq:write");
        ApiKeyScopes.Admin.Should().Be("admin");
    }
}

public class ApiKeyConfigurationTests
{
    [Fact]
    public void HasScope_NullScopes_ShouldBeAdmin()
    {
        var config = new ApiKeyConfiguration
        {
            Key = "test-key-12345678",
            Scopes = null,
            Description = "Admin key"
        };

        config.HasScope(ApiKeyScopes.NamespacesRead).Should().BeTrue();
        config.HasScope(ApiKeyScopes.NamespacesWrite).Should().BeTrue();
        config.HasScope(ApiKeyScopes.DlqWrite).Should().BeTrue();
    }

    [Fact]
    public void HasScope_EmptyScopes_ShouldBeAdmin()
    {
        var config = new ApiKeyConfiguration
        {
            Key = "test-key-12345678",
            Scopes = [],
            Description = "Admin key"
        };

        config.HasScope(ApiKeyScopes.NamespacesRead).Should().BeTrue();
    }

    [Fact]
    public void HasScope_SpecificScopes_ShouldOnlyGrantMatching()
    {
        var config = new ApiKeyConfiguration
        {
            Key = "test-key-12345678",
            Scopes = [ApiKeyScopes.NamespacesRead, ApiKeyScopes.QueuesRead]
        };

        config.HasScope(ApiKeyScopes.NamespacesRead).Should().BeTrue();
        config.HasScope(ApiKeyScopes.QueuesRead).Should().BeTrue();
        config.HasScope(ApiKeyScopes.NamespacesWrite).Should().BeFalse();
        config.HasScope(ApiKeyScopes.DlqWrite).Should().BeFalse();
    }

    [Fact]
    public void HasScope_AdminInScopes_ShouldGrantAll()
    {
        var config = new ApiKeyConfiguration
        {
            Key = "test-key-12345678",
            Scopes = [ApiKeyScopes.Admin]
        };

        config.HasScope(ApiKeyScopes.NamespacesRead).Should().BeTrue();
        config.HasScope(ApiKeyScopes.DlqWrite).Should().BeTrue();
    }

    [Fact]
    public void GetSafeKey_LongKey_ShouldMaskAfter8Chars()
    {
        var config = new ApiKeyConfiguration
        {
            Key = "abcdefghijklmnop"
        };

        config.GetSafeKey().Should().Be("abcdefgh***");
    }

    [Fact]
    public void GetSafeKey_ShortKey_ShouldReturnMask()
    {
        var config = new ApiKeyConfiguration
        {
            Key = "abc"
        };

        config.GetSafeKey().Should().Be("***");
    }
}
