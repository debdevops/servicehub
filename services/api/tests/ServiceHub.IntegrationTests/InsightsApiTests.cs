using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Insights (unit 6.18): a real spike becomes a finding with its numbers, and clears when it stops being true.</summary>
public sealed class InsightsApiTests
{
    private static async Task Cycle(DeadLettersApiTests.Handle host, string id) =>
        await host.Services.GetServices<IAgent>().Single(a => a.Descriptor.Id == id).ExecuteCycleAsync(default);

    [Fact]
    public async Task A_spike_is_found_with_the_numbers_behind_it_narrated_as_a_suggestion_and_cleared_when_it_passes()
    {
        using var host = DeadLettersApiTests.Host();
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        for (var day = 1; day <= 4; day++)
        {
            await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 5, age: TimeSpan.FromDays(day).Add(TimeSpan.FromHours(1)), prefix: $"d{day}");
        }

        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 40, age: TimeSpan.FromHours(1), prefix: "today");
        var agents = host.Services.GetServices<IAgent>().Select(a => a.Descriptor).Where(d => d.Id.StartsWith("insights-")).ToList();
        agents.Should().HaveCount(4);
        agents.Should().OnlyContain(d => d.Authority == AgentAuthority.Observes, "a finding never acts");

        await Cycle(host, "insights-anomaly");
        await Cycle(host, "insights-narration");

        var body = JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/insights")).RootElement;
        var current = body.GetProperty("current").EnumerateArray().ToList();
        var spike = current.Single(f => f.GetProperty("kind").GetString() == "anomaly");
        spike.GetProperty("entityName").GetString().Should().Be("orders");
        spike.GetProperty("what").GetString().Should().Contain("spike");
        spike.GetProperty("metrics").GetProperty("currentCount").GetDouble().Should().BeGreaterThan(30);
        spike.GetProperty("suggestion").GetBoolean().Should().BeFalse();
        current.Where(f => f.GetProperty("kind").GetString() == "narration").Should().OnlyContain(f => f.GetProperty("suggestion").GetBoolean());

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            db.DlqMessages.RemoveRange(await db.DlqMessages.Where(m => m.MessageId.StartsWith("today")).ToListAsync());
            await db.SaveChangesAsync();
        }

        await Cycle(host, "insights-anomaly");
        var after = JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/insights?cleared=true")).RootElement;
        after.GetProperty("current").EnumerateArray().Should().NotContain(f => f.GetProperty("kind").GetString() == "anomaly");
        after.GetProperty("cleared").EnumerateArray().Should().Contain(f => f.GetProperty("kind").GetString() == "anomaly");
    }
}
