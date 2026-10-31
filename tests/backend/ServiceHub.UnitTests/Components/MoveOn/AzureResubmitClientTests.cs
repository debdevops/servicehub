using System.Net;
using Azure.Core;
using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Azure.Components;
using ServiceHub.Providers.Azure.MoveOn;

namespace ServiceHub.UnitTests.Components.MoveOn;

/// <summary>The one door that sends a POST to Azure: where it may go, how it signs, and that it never retries.</summary>
public sealed class AzureResubmitClientTests
{
    private const string Good = "https://management.azure.com/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders/triggers/manual/histories/run-1/resubmit?api-version=2016-06-01";

    private readonly RecordingHandler _handler = new();
    private readonly FakeCredential _credential = new();
    private readonly Namespace _ns = Namespace.CreateWithManagedIdentity("resubmit-test", ConnectionAuthType.ManagedIdentity, provider: CloudProviderType.Azure).Value;

    private AzureResubmitClient Client() => new(new HttpClient(_handler), _ => _credential);

    [Theory]
    [InlineData(Good, true)]
    [InlineData("https://MANAGEMENT.AZURE.COM/subscriptions/s/resourceGroups/rg/providers/Microsoft.Logic/workflows/w/triggers/t/histories/h/resubmit?api-version=2016-06-01", true)]
    [InlineData("http://management.azure.com/subscriptions/s/resourceGroups/rg/providers/Microsoft.Logic/workflows/w/triggers/t/histories/h/resubmit", false)]
    [InlineData("https://management.azure.com.evil.example/subscriptions/s/resourceGroups/rg/providers/Microsoft.Logic/workflows/w/triggers/t/histories/h/resubmit", false)]
    [InlineData("https://user:pw@management.azure.com/subscriptions/s/resourceGroups/rg/providers/Microsoft.Logic/workflows/w/triggers/t/histories/h/resubmit", false)]
    [InlineData("https://management.azure.com:8443/subscriptions/s/resourceGroups/rg/providers/Microsoft.Logic/workflows/w/triggers/t/histories/h/resubmit", false)]
    [InlineData("https://management.azure.com/subscriptions/s/resourceGroups/rg/providers/Microsoft.Logic/workflows/w/triggers/t/histories/h", false)]
    [InlineData("https://management.azure.com/subscriptions/s/resourceGroups/rg/providers/Microsoft.Logic/workflows/w/triggers/t/run", false)]
    [InlineData("https://management.azure.com/subscriptions/s/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm/triggers/t/histories/h/resubmit", false)]
    [InlineData("https://management.azure.com/subscriptions/s/resourceGroups/rg/providers/Microsoft.Logic/workflows/w/delete", false)]
    public void Only_a_logic_apps_resubmit_address_on_resource_manager_is_allowed(string url, bool allowed)
    {
        AzureResubmitClient.IsResubmitAddress(new Uri(url)).Should().Be(allowed);
    }

    [Fact]
    public async Task A_resubmit_is_one_post_with_no_body_and_a_bearer_token_for_the_management_scope()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Accepted);

        await Client().PostAsync(_ns, new Uri(Good), default);

        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsoluteUri.Should().Be(Good);
        request.Content.Should().BeNull("a resubmit carries no body; Azure replays the stored trigger");
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("token-1");
        _credential.Asked.Should().Equal("https://management.azure.com/.default");
    }

    [Fact]
    public async Task An_address_that_is_not_a_resubmit_is_refused_before_a_token_or_a_request()
    {
        var act = () => Client().PostAsync(_ns, new Uri("https://management.azure.com/subscriptions?api-version=1"), default);

        await act.Should().ThrowAsync<ArgumentException>();
        _handler.Requests.Should().BeEmpty();
        _credential.Asked.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Any_answer_but_a_success_is_thrown_with_its_status_and_is_never_retried(HttpStatusCode status)
    {
        _handler.Respond = _ => new HttpResponseMessage(status);

        var act = () => Client().PostAsync(_ns, new Uri(Good), default);

        (await act.Should().ThrowAsync<AzureRestException>()).Which.Status.Should().Be(status);
        _handler.Requests.Should().ContainSingle("Azure gives a resubmit no idempotency key, so a second POST would start a second run");
    }

    [Theory]
    [InlineData(ConnectionAuthType.ConnectionString)]
    [InlineData(ConnectionAuthType.ServicePrincipal)]
    public async Task A_connection_that_does_not_sign_in_with_entra_makes_no_request(ConnectionAuthType type)
    {
        var ns = type == ConnectionAuthType.ConnectionString
            ? Namespace.Create("rs-cs.servicebus.windows.net", "Endpoint=sb://rs-cs.servicebus.windows.net/;SharedAccessKeyName=app;SharedAccessKey=abc=").Value
            : Namespace.CreateWithManagedIdentity("rs-sp", ConnectionAuthType.ServicePrincipal, provider: CloudProviderType.Azure).Value;
        var client = new AzureResubmitClient(new HttpClient(_handler));

        var act = () => client.PostAsync(ns, new Uri(Good), default);

        await act.Should().ThrowAsync<AzureNotEntraException>();
        _handler.Requests.Should().BeEmpty();
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

        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.Accepted);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }
}
