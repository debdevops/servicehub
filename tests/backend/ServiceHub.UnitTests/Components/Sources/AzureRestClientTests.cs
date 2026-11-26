using System.Net;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Azure.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>The one HTTP door the Azure sources read through: where it may go, how it signs, and what it does with an answer.</summary>
public sealed class AzureRestClientTests
{
    private readonly RecordingHandler _handler = new();
    private readonly FakeCredential _credential = new();
    private readonly Namespace _ns = Namespace.CreateWithManagedIdentity("rest-test", ConnectionAuthType.ManagedIdentity, provider: CloudProviderType.Azure).Value;

    private AzureRestClient Client() => new(new HttpClient(_handler), _ => _credential);

    [Theory]
    [InlineData("https://management.azure.com/subscriptions?api-version=1", AzureAudience.Management, true)]
    [InlineData("https://MANAGEMENT.AZURE.COM/subscriptions", AzureAudience.Management, true)]
    [InlineData("http://management.azure.com/subscriptions", AzureAudience.Management, false)]
    [InlineData("https://management.azure.com.evil.example/subscriptions", AzureAudience.Management, false)]
    [InlineData("https://evil.example/management.azure.com", AzureAudience.Management, false)]
    [InlineData("https://user:pw@management.azure.com/subscriptions", AzureAudience.Management, false)]
    [InlineData("https://stg.blob.core.windows.net/dl?restype=container", AzureAudience.Storage, true)]
    [InlineData("https://stg.blob.core.windows.net.evil.example/dl", AzureAudience.Storage, false)]
    [InlineData("https://blob.core.windows.net/dl", AzureAudience.Storage, false)]
    [InlineData("http://stg.blob.core.windows.net/dl", AzureAudience.Storage, false)]
    [InlineData("https://management.azure.com/subscriptions", AzureAudience.Storage, false)]
    [InlineData("https://stg.blob.core.windows.net/dl", AzureAudience.Management, false)]
    public void Only_https_on_the_host_of_the_audience_is_allowed(string url, AzureAudience audience, bool allowed)
    {
        AzureRestClient.IsAllowed(new Uri(url), audience).Should().Be(allowed);
    }

    [Fact]
    public async Task A_request_to_a_host_the_audience_does_not_allow_is_refused_before_a_token_or_a_request()
    {
        var act = () => Client().GetTextAsync(_ns, new Uri("https://evil.example/x"), AzureAudience.Management, null, default);

        await act.Should().ThrowAsync<ArgumentException>();
        _handler.Requests.Should().BeEmpty();
        _credential.Asked.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ConnectionAuthType.ConnectionString)]
    [InlineData(ConnectionAuthType.ServicePrincipal)]
    [InlineData(ConnectionAuthType.AwsAccessKey)]
    public async Task Only_managed_identity_and_default_credential_connections_get_a_token_the_default_wiring(ConnectionAuthType type)
    {
        var ns = type == ConnectionAuthType.ConnectionString
            ? Namespace.Create("rest-cs.servicebus.windows.net", "Endpoint=sb://rest-cs.servicebus.windows.net/;SharedAccessKeyName=app;SharedAccessKey=abc=").Value
            : type == ConnectionAuthType.ServicePrincipal
                ? Namespace.CreateWithManagedIdentity("rest-sp", ConnectionAuthType.ServicePrincipal, provider: CloudProviderType.Azure).Value
                : Namespace.Create("aws-name", "AKIAIOSFODNN7EXAMPLE:secretsecretsecretsecretsecretsecretsecr", provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;
        var client = new AzureRestClient(new HttpClient(_handler));

        var act = () => client.GetTextAsync(ns, new Uri("https://management.azure.com/subscriptions"), AzureAudience.Management, null, default);

        (await act.Should().ThrowAsync<AzureNotEntraException>()).Which.AuthType.Should().Be(ns.AuthType);
        _handler.Requests.Should().BeEmpty("no request is made on a connection that cannot sign in with Entra");
    }

    [Fact]
    public async Task A_resource_manager_read_is_a_get_with_a_bearer_token_for_the_management_scope()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}") };

        var body = await Client().GetTextAsync(_ns, new Uri("https://management.azure.com/subscriptions?api-version=1"), AzureAudience.Management, null, default);

        body.Should().Be("{\"value\":[]}");
        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("token-1");
        request.Headers.Contains("x-ms-version").Should().BeFalse();
        request.Content.Should().BeNull();
        _credential.Asked.Should().ContainSingle().Which.Should().Be("https://management.azure.com/.default");
    }

    [Fact]
    public async Task A_blob_read_uses_the_storage_scope_and_the_storage_api_version_header()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<x/>") };

        await Client().GetTextAsync(_ns, new Uri("https://stg.blob.core.windows.net/dl?restype=container&comp=list"), AzureAudience.Storage, null, default);

        _handler.Requests.Single().Headers.GetValues("x-ms-version").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        _credential.Asked.Should().ContainSingle().Which.Should().Be("https://storage.azure.com/.default");
    }

    [Fact]
    public async Task A_range_read_asks_for_the_leading_bytes_only_and_never_returns_more_than_the_cap()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StringContent(new string('a', 1000)) };

        var body = await Client().GetTextAsync(_ns, new Uri("https://stg.blob.core.windows.net/dl/a"), AzureAudience.Storage, 100, default);

        _handler.Requests.Single().Headers.Range!.Ranges.Single().Should().Match<System.Net.Http.Headers.RangeItemHeaderValue>(r => r.From == 0 && r.To == 99);
        body!.Length.Should().Be(100, "a server that ignores the range cannot make the client hold more than the cap");
    }

    [Fact]
    public async Task A_range_read_of_a_small_blob_returns_all_of_it()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("short") };

        (await Client().GetTextAsync(_ns, new Uri("https://stg.blob.core.windows.net/dl/a"), AzureAudience.Storage, 100, default)).Should().Be("short");
    }

    [Fact]
    public async Task A_404_is_null_not_an_error()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        (await Client().GetTextAsync(_ns, new Uri("https://management.azure.com/x"), AzureAudience.Management, null, default)).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Any_other_error_status_is_an_exception_carrying_the_status_and_no_body(HttpStatusCode status)
    {
        _handler.Respond = _ => new HttpResponseMessage(status) { Content = new StringContent("secret-body-text") };

        var act = () => Client().GetTextAsync(_ns, new Uri("https://management.azure.com/x"), AzureAudience.Management, null, default);

        var thrown = (await act.Should().ThrowAsync<AzureRestException>()).Which;
        thrown.Status.Should().Be(status);
        thrown.Message.Should().NotContain("secret-body-text");
    }

    [Fact]
    public async Task A_cancelled_caller_gets_a_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Client().GetTextAsync(_ns, new Uri("https://management.azure.com/x"), AzureAudience.Management, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task The_credential_is_made_once_per_connection_not_once_per_request()
    {
        var made = 0;
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        var client = new AzureRestClient(new HttpClient(_handler), _ => { made++; return _credential; });

        await client.GetTextAsync(_ns, new Uri("https://management.azure.com/a"), AzureAudience.Management, null, default);
        await client.GetTextAsync(_ns, new Uri("https://management.azure.com/b"), AzureAudience.Management, null, default);

        made.Should().Be(1);
    }

    [Fact]
    public void A_missing_client_or_credential_source_is_refused()
    {
        ((Action)(() => _ = new AzureRestClient(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new AzureRestClient(new HttpClient(), null!))).Should().Throw<ArgumentNullException>();
    }

    private sealed class FakeCredential : TokenCredential
    {
        private int _count;

        public List<string> Asked { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Issue(requestContext);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(Issue(requestContext));

        private AccessToken Issue(TokenRequestContext context)
        {
            Asked.AddRange(context.Scopes);
            return new AccessToken($"token-{++_count}", DateTimeOffset.UtcNow.AddHours(1));
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }
}

/// <summary>Every way an Azure read can fail becomes one error the screen can show, and none of them leaks a secret.</summary>
public sealed class AzureFailuresTests
{
    [Fact]
    public void A_connection_that_is_not_entra_is_not_enabled_and_says_what_to_use_instead()
    {
        var error = AzureFailures.ToError(new AzureNotEntraException(ConnectionAuthType.ConnectionString), "Azure.X", "Reader");

        error.Message.Should().StartWith("Not enabled").And.Contain("Entra").And.Contain("Managed Identity");
        error.Code.Should().Be("Azure.X.NotEnabled");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void A_refusal_is_not_enabled_and_names_what_is_needed(HttpStatusCode status)
    {
        var error = AzureFailures.ToError(new AzureRestException(status, "x"), "Azure.X", "the Reader role");

        error.Message.Should().StartWith("Not enabled").And.Contain("the Reader role");
        error.Code.Should().Be("Azure.X.NotEnabled");
    }

    [Fact]
    public void A_login_that_cannot_be_made_is_not_enabled()
    {
        AzureFailures.ToError(new CredentialUnavailableException("none"), "Azure.X", "r").Message.Should().StartWith("Not enabled").And.Contain("sign in");
        AzureFailures.ToError(new AuthenticationFailedException("bad"), "Azure.X", "r").Message.Should().StartWith("Not enabled");
    }

    [Fact]
    public void Any_other_failure_is_an_external_error_with_the_secret_removed()
    {
        var error = AzureFailures.ToError(new AzureRestException(HttpStatusCode.BadGateway, "bad gateway (Password=hunter2-not-for-display)"), "Azure.X", "r");

        error.Code.Should().Be("Azure.X.ReadFailed");
        error.Message.Should().NotContain("hunter2");
    }

    [Fact]
    public void A_message_with_control_characters_is_cleaned()
    {
        var error = AzureFailures.ToError(new InvalidOperationException("line1\r\nline2"), "Azure.X", "r");

        error.Message.Should().NotContain("\r").And.NotContain("\n");
    }
}
