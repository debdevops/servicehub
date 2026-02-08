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
}
