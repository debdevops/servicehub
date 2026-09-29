using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Authority — and why (unit 6.8): the floor is counted with what holds each signature there.</summary>
public sealed class AuthorityApiTests
{
    [Fact]
    public async Task Signatures_at_the_floor_are_counted_with_the_reason_they_are_held_there()
    {
        using var host = DeadLettersApiTests.Host();
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");
        await DeadLettersApiTests.Seed(host, azure, CloudProviderType.Azure, 2, prefix: "az");
        await DeadLettersApiTests.Seed(host, aws, CloudProviderType.Aws, 2, prefix: "aw");
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            foreach (var m in await db.DlqMessages.ToListAsync()) m.SignatureHash = m.NamespaceId == azure ? "sig-azure" : "sig-aws";
            await db.SaveChangesAsync();
        }

        var body = JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/signatures/authority?days=7")).RootElement;
        body.GetProperty("total").GetInt32().Should().Be(2);
        body.GetProperty("approve").GetInt32().Should().Be(2);
        body.GetProperty("standing").GetInt32().Should().Be(0);
        var held = body.GetProperty("held").EnumerateArray().ToDictionary(h => h.GetProperty("reason").GetString()!, h => h.GetProperty("count").GetInt32());
        held.Should().Equal(new Dictionary<string, int> { ["needs_evidence"] = 1, ["cannot_verify"] = 1 });
    }
}
