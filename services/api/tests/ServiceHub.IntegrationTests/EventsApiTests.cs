using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace ServiceHub.IntegrationTests;

/// <summary>Unit 2.11 through the real host: an action in one client reaches a stream in another, within a second.</summary>
public sealed class EventsApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static async Task<HttpResponseMessage> OpenStream(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/v1/events/stream", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        return response;
    }

    private static async Task<string?> ReadUntilDataAsync(StreamReader reader, TimeSpan patience)
    {
        using var cts = new CancellationTokenSource(patience);
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null)
                {
                    return null;
                }

                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    return line["data: ".Length..];
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    [Fact]
    public async Task Connecting_a_namespace_in_one_client_is_announced_on_another_clients_stream()
    {
        using var factory = new ServiceHubApiFactory();
        using var watcher = factory.CreateClient();
        using var actor = factory.CreateClient();
        using var stream = await OpenStream(watcher);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync());

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/namespaces")
        {
            Content = JsonContent.Create(new
            {
                name = "orders-dev", provider = "azure", authType = "connectionString",
                connectionString = "Endpoint=sb://orders-dev.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=secret==",
            }),
        };
        request.Headers.Add("X-ServiceHub-Intent", "create-namespace");
        (await actor.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Created);

        var data = await ReadUntilDataAsync(reader, Patience);

        data.Should().NotBeNull("the event must arrive without polling");
        data.Should().Contain("servicehub.namespace.created.v1");
        data.Should().NotContain("secret", "an event is a hint, never a payload");
    }

    [Fact]
    public async Task A_stream_opens_and_says_it_is_connected_before_anything_happens()
    {
        using var factory = new ServiceHubApiFactory();
        using var client = factory.CreateClient();
        using var stream = await OpenStream(client);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync());

        (await reader.ReadLineAsync()).Should().Be(": connected");
    }
}
