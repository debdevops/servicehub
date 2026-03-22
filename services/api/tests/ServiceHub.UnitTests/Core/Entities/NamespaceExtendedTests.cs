using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.UnitTests.Core.Entities;

public sealed class NamespaceExtendedTests
{
    private const string ValidConnString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=TestPolicy;SharedAccessKey=abc=";

    // ═══════════════════════════════════════════════════════════════
    // Create - connection string validation
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_NullOrEmptyName_ReturnsFailure(string? name)
    {
        var result = Namespace.Create(name!, ValidConnString);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_NameTooLong_ReturnsFailure()
    {
        var longName = new string('a', 257);
        var result = Namespace.Create(longName, ValidConnString);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_InvalidNameFormat_ReturnsFailure()
    {
        var result = Namespace.Create("invalid name with spaces!", ValidConnString);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_FQDNName_ReturnsSuccess()
    {
        var result = Namespace.Create("myns.servicebus.windows.net", ValidConnString);
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("myns.servicebus.windows.net");
    }

    [Fact]
    public void Create_ShortName_ReturnsSuccess()
    {
        var result = Namespace.Create("testns", ValidConnString);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_NameWithHyphens_ReturnsSuccess()
    {
        var result = Namespace.Create("test-ns-123", ValidConnString);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_NameStartsWithHyphen_ReturnsFailure()
    {
        var result = Namespace.Create("-testns", ValidConnString);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_NameEndsWithHyphen_ReturnsFailure()
    {
        var result = Namespace.Create("testns-", ValidConnString);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_NameTooShort_ReturnsFailure()
    {
        var result = Namespace.Create("ab", ValidConnString);
        result.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_NullOrEmptyConnectionString_ReturnsFailure(string? cs)
    {
        var result = Namespace.Create("test-ns", cs!);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_InvalidConnectionString_ReturnsFailure()
    {
        var result = Namespace.Create("test-ns", "not a connection string");
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_EncryptedConnectionString_ReturnsSuccess()
    {
        var result = Namespace.Create("test-ns", "PROTECTED:encrypted-data");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_EncV1ConnectionString_ReturnsSuccess()
    {
        var result = Namespace.Create("test-ns", "ENC[v1]:some-encrypted-data");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_EncV2ConnectionString_ReturnsSuccess()
    {
        var result = Namespace.Create("test-ns", "ENC:V2:some-encrypted-data");
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_ValidParams_SetsAllProperties()
    {
        var result = Namespace.Create("test-ns", ValidConnString, "Display", "Description");

        result.IsSuccess.Should().BeTrue();
        var ns = result.Value;
        ns.Id.Should().NotBeEmpty();
        ns.Name.Should().Be("test-ns");
        ns.DisplayName.Should().Be("Display");
        ns.Description.Should().Be("Description");
        ns.AuthType.Should().Be(ConnectionAuthType.ConnectionString);
        ns.IsActive.Should().BeTrue();
        ns.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_DisplayNameTooLong_ReturnsFailure()
    {
        var longDisplay = new string('a', 101);
        var result = Namespace.Create("test-ns", ValidConnString, longDisplay);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_DescriptionTooLong_ReturnsFailure()
    {
        var longDesc = new string('a', 501);
        var result = Namespace.Create("test-ns", ValidConnString, description: longDesc);
        result.IsFailure.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // CreateWithManagedIdentity
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CreateManagedIdentity_ReturnsSuccess()
    {
        var result = Namespace.CreateWithManagedIdentity("test-ns");
        result.IsSuccess.Should().BeTrue();
        result.Value.AuthType.Should().Be(ConnectionAuthType.ManagedIdentity);
        result.Value.ConnectionString.Should().BeNull();
        result.Value.HasManagePermission.Should().BeTrue();
    }

    [Fact]
    public void CreateManagedIdentity_ConnectionStringAuthType_ReturnsFailure()
    {
        var result = Namespace.CreateWithManagedIdentity("test-ns", ConnectionAuthType.ConnectionString);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CreateManagedIdentity_ServicePrincipal_ReturnsSuccess()
    {
        var result = Namespace.CreateWithManagedIdentity("test-ns", ConnectionAuthType.ServicePrincipal);
        result.IsSuccess.Should().BeTrue();
        result.Value.AuthType.Should().Be(ConnectionAuthType.ServicePrincipal);
    }

    [Fact]
    public void CreateManagedIdentity_DefaultAzureCredential_ReturnsSuccess()
    {
        var result = Namespace.CreateWithManagedIdentity("test-ns", ConnectionAuthType.DefaultAzureCredential);
        result.IsSuccess.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // Update methods
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UpdateDisplayName_Valid_ReturnsSuccess()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        var result = ns.UpdateDisplayName("New Display");

        result.IsSuccess.Should().BeTrue();
        ns.DisplayName.Should().Be("New Display");
        ns.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateDisplayName_TooLong_ReturnsFailure()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        var result = ns.UpdateDisplayName(new string('a', 101));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void UpdateDisplayName_Null_ReturnsSuccess()
    {
        var ns = Namespace.Create("test-ns", ValidConnString, "Initial").Value;
        var result = ns.UpdateDisplayName(null);

        result.IsSuccess.Should().BeTrue();
        ns.DisplayName.Should().BeNull();
    }

    [Fact]
    public void UpdateDescription_Valid_ReturnsSuccess()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        var result = ns.UpdateDescription("New description");

        result.IsSuccess.Should().BeTrue();
        ns.Description.Should().Be("New description");
    }

    [Fact]
    public void UpdateDescription_TooLong_ReturnsFailure()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        var result = ns.UpdateDescription(new string('a', 501));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void UpdateConnectionString_ValidNewString_ReturnsSuccess()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        var newCs = "Endpoint=sb://other.servicebus.windows.net/;SharedAccessKeyName=OtherPolicy;SharedAccessKey=xyz=";
        var result = ns.UpdateConnectionString(newCs);

        result.IsSuccess.Should().BeTrue();
        ns.ConnectionString.Should().Be(newCs);
        ns.LastConnectionTestAt.Should().BeNull();
    }

    [Fact]
    public void UpdateConnectionString_EmptyString_ReturnsFailure()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        var result = ns.UpdateConnectionString("");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void UpdateConnectionString_InvalidFormat_ReturnsFailure()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        var result = ns.UpdateConnectionString("not-valid");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void UpdateConnectionString_ManagedIdentityNamespace_ReturnsFailure()
    {
        var ns = Namespace.CreateWithManagedIdentity("test-ns").Value;
        var result = ns.UpdateConnectionString(ValidConnString);

        result.IsFailure.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // RecordConnectionTest, Activate, Deactivate
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RecordConnectionTest_Succeeded_SetsProperties()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        ns.RecordConnectionTest(true);

        ns.LastConnectionTestAt.Should().NotBeNull();
        ns.LastConnectionTestSucceeded.Should().BeTrue();
    }

    [Fact]
    public void RecordConnectionTest_Failed_SetsProperties()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        ns.RecordConnectionTest(false);

        ns.LastConnectionTestSucceeded.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_ActiveNamespace_SetsInactive()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        ns.IsActive.Should().BeTrue();

        ns.Deactivate();
        ns.IsActive.Should().BeFalse();
        ns.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void Deactivate_AlreadyInactive_NoChange()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        ns.Deactivate();
        var mod1 = ns.ModifiedAt;

        ns.Deactivate();
        ns.ModifiedAt.Should().Be(mod1);
    }

    [Fact]
    public void Activate_InactiveNamespace_SetsActive()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        ns.Deactivate();

        ns.Activate();
        ns.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Activate_AlreadyActive_NoChange()
    {
        var ns = Namespace.Create("test-ns", ValidConnString).Value;
        var mod1 = ns.ModifiedAt;

        ns.Activate();
        ns.ModifiedAt.Should().Be(mod1);
    }

    // ═══════════════════════════════════════════════════════════════
    // Permission detection
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Create_ListenOnlyKeyName_SetsListenPermission()
    {
        var cs = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=ListenPolicy;SharedAccessKey=abc=";
        var ns = Namespace.Create("test-ns", cs).Value;

        ns.HasListenPermission.Should().BeTrue();
        ns.HasSendPermission.Should().BeFalse();
        ns.HasManagePermission.Should().BeFalse();
    }

    [Fact]
    public void Create_SendOnlyKeyName_SetsSendPermission()
    {
        var cs = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=SendPolicy;SharedAccessKey=abc=";
        var ns = Namespace.Create("test-ns", cs).Value;

        ns.HasListenPermission.Should().BeTrue();
        ns.HasSendPermission.Should().BeTrue();
        ns.HasManagePermission.Should().BeFalse();
    }

    [Fact]
    public void Create_ManageKeyName_SetsAllPermissions()
    {
        var cs = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=ManagePolicy;SharedAccessKey=abc=";
        var ns = Namespace.Create("test-ns", cs).Value;

        ns.HasManagePermission.Should().BeTrue();
        ns.HasSendPermission.Should().BeTrue();
        ns.HasListenPermission.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // Domain name validation variants
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Create_ChinaCloudFQDN_ReturnsSuccess()
    {
        var result = Namespace.Create("myns.servicebus.chinacloudapi.cn", ValidConnString);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_GovCloudFQDN_ReturnsSuccess()
    {
        var result = Namespace.Create("myns.servicebus.usgovcloudapi.net", ValidConnString);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_GermanCloudFQDN_ReturnsSuccess()
    {
        var result = Namespace.Create("myns.servicebus.cloudapi.de", ValidConnString);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_InvalidDomainSuffix_ReturnsFailure()
    {
        var result = Namespace.Create("myns.servicebus.invalid.net", ValidConnString);
        result.IsFailure.Should().BeTrue();
    }
}
