using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Core.Entities;

public sealed class NamespaceTests
{
    private const string ValidName = "test-namespace.servicebus.windows.net";
    private const string ValidConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123==";

    [Fact]
    public void Create_WithValidParameters_ShouldSucceed()
    {
        var result = Namespace.Create(ValidName, ValidConnectionString);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(ValidName.ToLowerInvariant());
        result.Value.ConnectionString.Should().Be(ValidConnectionString.Trim());
        result.Value.AuthType.Should().Be(ConnectionAuthType.ConnectionString);
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_WithDisplayNameAndDescription_ShouldStoreValues()
    {
        var displayName = "Test Namespace";
        var description = "Test description";

        var result = Namespace.Create(ValidName, ValidConnectionString, displayName, description);

        result.IsSuccess.Should().BeTrue();
        result.Value.DisplayName.Should().Be(displayName);
        result.Value.Description.Should().Be(description);
    }

    [Fact]
    public void Create_WithEmptyName_ShouldFail()
    {
        var result = Namespace.Create(string.Empty, ValidConnectionString);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Create_WithEmptyConnectionString_ShouldFail()
    {
        var result = Namespace.Create(ValidName, string.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Create_WithInvalidConnectionString_ShouldFail()
    {
        var result = Namespace.Create(ValidName, "invalid-connection-string");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Create_WithNameTooLong_ShouldFail()
    {
        var longName = new string('a', Namespace.MaxNameLength + 1);

        var result = Namespace.Create(longName, ValidConnectionString);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_WithDisplayNameTooLong_ShouldFail()
    {
        var longDisplayName = new string('a', Namespace.MaxDisplayNameLength + 1);

        var result = Namespace.Create(ValidName, ValidConnectionString, longDisplayName);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CreateWithManagedIdentity_WithValidParameters_ShouldSucceed()
    {
        var result = Namespace.CreateWithManagedIdentity(ValidName);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(ValidName.ToLowerInvariant());
        result.Value.ConnectionString.Should().BeNull();
        result.Value.AuthType.Should().Be(ConnectionAuthType.ManagedIdentity);
    }

    [Fact]
    public void CreateWithManagedIdentity_WithConnectionStringAuthType_ShouldFail()
    {
        var result = Namespace.CreateWithManagedIdentity(ValidName, ConnectionAuthType.ConnectionString);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void UpdateDisplayName_WithValidValue_ShouldSucceed()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString).Value;
        var newDisplayName = "New Display Name";

        var result = ns.UpdateDisplayName(newDisplayName);

        result.IsSuccess.Should().BeTrue();
        ns.DisplayName.Should().Be(newDisplayName);
        ns.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateDisplayName_WithTooLongValue_ShouldFail()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString).Value;
        var longName = new string('a', Namespace.MaxDisplayNameLength + 1);

        var result = ns.UpdateDisplayName(longName);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void UpdateDescription_WithValidValue_ShouldSucceed()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString).Value;
        var newDescription = "New description";

        var result = ns.UpdateDescription(newDescription);

        result.IsSuccess.Should().BeTrue();
        ns.Description.Should().Be(newDescription);
        ns.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateConnectionString_WithValidString_ShouldSucceed()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString).Value;
        var newConnectionString = "Endpoint=sb://new.servicebus.windows.net/;SharedAccessKey=xyz456==";

        var result = ns.UpdateConnectionString(newConnectionString);

        result.IsSuccess.Should().BeTrue();
        ns.ConnectionString.Should().Be(newConnectionString.Trim());
        ns.LastConnectionTestAt.Should().BeNull();
        ns.LastConnectionTestSucceeded.Should().BeNull();
    }

    [Fact]
    public void UpdateConnectionString_ForManagedIdentityNamespace_ShouldFail()
    {
        var ns = Namespace.CreateWithManagedIdentity(ValidName).Value;

        var result = ns.UpdateConnectionString(ValidConnectionString);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.BusinessRule);
    }

    [Fact]
    public void UpdateConnectionString_WithInvalidString_ShouldFail()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString).Value;

        var result = ns.UpdateConnectionString("invalid");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void RecordConnectionTest_ShouldUpdateTestResults()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString).Value;

        ns.RecordConnectionTest(true);

        ns.LastConnectionTestAt.Should().NotBeNull();
        ns.LastConnectionTestSucceeded.Should().BeTrue();
        ns.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void Activate_WhenInactive_ShouldSetActiveState()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString).Value;
        ns.Deactivate();

        ns.Activate();

        ns.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_WhenActive_ShouldSetInactiveState()
    {
        var ns = Namespace.Create(ValidName, ValidConnectionString).Value;

        ns.Deactivate();

        ns.IsActive.Should().BeFalse();
        ns.ModifiedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("test-namespace.servicebus.windows.net", true)]
    [InlineData("my-test-ns.servicebus.windows.net", true)]
    [InlineData("test-namespace.servicebus.chinacloudapi.cn", true)]
    [InlineData("sqs.us-east-1.amazonaws.com", true)]
    [InlineData("sqs.us-east-1.example.com", false)]
    [InlineData("test123", true)]
    [InlineData("test-ns-123", true)]
    [InlineData("invalid..name", false)]
    [InlineData("-invalid", false)]
    [InlineData("invalid-", false)]
    [InlineData("ab", false)]
    [InlineData("", false)]
    public void Create_WithVariousNameFormats_ShouldValidateCorrectly(string name, bool shouldSucceed)
    {
        var result = Namespace.Create(name, ValidConnectionString);

        result.IsSuccess.Should().Be(shouldSucceed);
    }

    [Theory]
    [InlineData("Endpoint=sb://test.servicebus.windows.net/;SharedAccessKey=abc==", true)]
    [InlineData("Endpoint=sb://test.servicebus.windows.net/;SharedAccessSignature=sig123", true)]
    [InlineData("invalid", false)]
    [InlineData("Endpoint=sb://test.servicebus.windows.net/", false)]
    [InlineData("SharedAccessKey=abc123", false)]
    public void Create_WithVariousConnectionStringFormats_ShouldValidateCorrectly(string connectionString, bool shouldSucceed)
    {
        var result = Namespace.Create(ValidName, connectionString);

        result.IsSuccess.Should().Be(shouldSucceed);
    }

    // ── Environment Tests ───────────────────────────────────

    [Fact]
    public void Create_DefaultEnvironment_ShouldBeDev()
    {
        var result = Namespace.Create(ValidName, ValidConnectionString);

        result.IsSuccess.Should().BeTrue();
        result.Value.Environment.Should().Be(EnvironmentType.Dev);
    }

    [Theory]
    [InlineData(EnvironmentType.Dev)]
    [InlineData(EnvironmentType.Uat)]
    [InlineData(EnvironmentType.Prod)]
    public void Create_WithExplicitEnvironment_ShouldStoreValue(EnvironmentType env)
    {
        var result = Namespace.Create(ValidName, ValidConnectionString, environment: env);

        result.IsSuccess.Should().BeTrue();
        result.Value.Environment.Should().Be(env);
    }

    [Fact]
    public void CreateWithManagedIdentity_DefaultEnvironment_ShouldBeDev()
    {
        var result = Namespace.CreateWithManagedIdentity(ValidName);

        result.IsSuccess.Should().BeTrue();
        result.Value.Environment.Should().Be(EnvironmentType.Dev);
    }

    [Theory]
    [InlineData(EnvironmentType.Dev)]
    [InlineData(EnvironmentType.Uat)]
    [InlineData(EnvironmentType.Prod)]
    public void CreateWithManagedIdentity_WithExplicitEnvironment_ShouldStoreValue(EnvironmentType env)
    {
        var result = Namespace.CreateWithManagedIdentity(ValidName, environment: env);

        result.IsSuccess.Should().BeTrue();
        result.Value.Environment.Should().Be(env);
    }

    // ── Sharing ──────────────────────────────────────────────────────────────

    private Namespace CreateTestNamespace(string ownerId = "owner-a") =>
        Namespace.Create(ValidName, ValidConnectionString, ownerId: ownerId).Value;

    [Fact]
    public void ShareWith_NewOwner_Succeeds()
    {
        var ns = CreateTestNamespace();

        var result = ns.ShareWith("owner-b");

        result.IsSuccess.Should().BeTrue();
        ns.SharedWithOwnerIds.Should().Contain("owner-b");
    }

    [Fact]
    public void ShareWith_OwnersOwnId_Fails()
    {
        var ns = CreateTestNamespace(ownerId: "owner-a");

        var result = ns.ShareWith("owner-a");

        result.IsFailure.Should().BeTrue();
        ns.SharedWithOwnerIds.Should().BeEmpty();
    }

    [Fact]
    public void ShareWith_EmptyOwnerId_Fails()
    {
        var ns = CreateTestNamespace();

        var result = ns.ShareWith("");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ShareWith_AlreadyShared_IsIdempotentSuccess()
    {
        var ns = CreateTestNamespace();
        ns.ShareWith("owner-b");

        var result = ns.ShareWith("owner-b");

        result.IsSuccess.Should().BeTrue();
        ns.SharedWithOwnerIds.Should().ContainSingle();
    }

    [Fact]
    public void ShareWith_ExceedsMaxSharedOwners_Fails()
    {
        var ns = CreateTestNamespace();
        for (var i = 0; i < Namespace.MaxSharedOwners; i++)
        {
            ns.ShareWith($"owner-{i}").IsSuccess.Should().BeTrue();
        }

        var result = ns.ShareWith("one-too-many");

        result.IsFailure.Should().BeTrue();
        ns.SharedWithOwnerIds.Should().HaveCount(Namespace.MaxSharedOwners);
    }

    [Fact]
    public void RevokeShare_SharedOwner_RemovesAccess()
    {
        var ns = CreateTestNamespace();
        ns.ShareWith("owner-b");

        ns.RevokeShare("owner-b");

        ns.SharedWithOwnerIds.Should().NotContain("owner-b");
    }

    [Fact]
    public void RevokeShare_OwnerNeverShared_IsIdempotentNoOp()
    {
        var ns = CreateTestNamespace();

        var act = () => ns.RevokeShare("never-shared");

        act.Should().NotThrow();
        ns.SharedWithOwnerIds.Should().BeEmpty();
    }

    [Fact]
    public void IsAccessibleBy_TrueOwner_ReturnsTrue()
    {
        var ns = CreateTestNamespace(ownerId: "owner-a");

        ns.IsAccessibleBy("owner-a").Should().BeTrue();
    }

    [Fact]
    public void IsAccessibleBy_SharedOwner_ReturnsTrue()
    {
        var ns = CreateTestNamespace(ownerId: "owner-a");
        ns.ShareWith("owner-b");

        ns.IsAccessibleBy("owner-b").Should().BeTrue();
    }

    [Fact]
    public void IsAccessibleBy_UnrelatedOwner_ReturnsFalse()
    {
        var ns = CreateTestNamespace(ownerId: "owner-a");
        ns.ShareWith("owner-b");

        ns.IsAccessibleBy("owner-c").Should().BeFalse();
    }

    [Fact]
    public void IsAccessibleBy_WithAllowList_NullAllowList_IsUnrestricted()
    {
        var ns = CreateTestNamespace(ownerId: "owner-a");

        ns.IsAccessibleBy("owner-a", allowedNamespaceIds: null).Should().BeTrue();
    }

    [Fact]
    public void IsAccessibleBy_WithAllowList_ContainsThisNamespace_ReturnsTrue()
    {
        var ns = CreateTestNamespace(ownerId: "owner-a");

        ns.IsAccessibleBy("owner-a", new HashSet<Guid> { ns.Id }).Should().BeTrue();
    }

    [Fact]
    public void IsAccessibleBy_WithAllowList_DoesNotContainThisNamespace_ReturnsFalse()
    {
        var ns = CreateTestNamespace(ownerId: "owner-a");

        ns.IsAccessibleBy("owner-a", new HashSet<Guid> { Guid.NewGuid() }).Should().BeFalse();
    }

    [Fact]
    public void IsAccessibleBy_WithAllowList_NarrowsButNeverWidensAccess()
    {
        // Owner-c isn't the owner and wasn't shared with — the allow-list containing this
        // namespace's ID must not grant access on its own.
        var ns = CreateTestNamespace(ownerId: "owner-a");

        ns.IsAccessibleBy("owner-c", new HashSet<Guid> { ns.Id }).Should().BeFalse();
    }

    [Fact]
    public void ShareWith_UpdatesModifiedAt()
    {
        var ns = CreateTestNamespace();

        ns.ShareWith("owner-b");

        ns.ModifiedAt.Should().NotBeNull();
    }
}
