using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.Components;
using ServiceHub.Core.Interfaces;
using ServiceHub.UnitTests.Architecture;

namespace ServiceHub.UnitTests.Components;

/// <summary><c>GET /api/v1/fleet/coverage</c>, and the copy of it the web demo answers from.</summary>
public sealed class CoverageEndpointTests
{
    /// <summary>The demo's copy of the catalogue. A test, not a person, keeps it equal to the real one.</summary>
    private const string SnapshotPath = "apps/servicehub/src/lib/demo/world/coverage.json";

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void The_endpoint_returns_the_whole_catalogue()
    {
        var result = new FleetController(Mock.Of<IFleetOverviewService>()).Coverage();

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(CoverageResponse.Current);
    }

    /// <summary>
    /// Run with <c>SERVICEHUB_UPDATE_SNAPSHOTS=1</c> to rewrite the file after changing the catalogue; without it, a
    /// difference fails. This is what makes the demo, the app and the API one list instead of two.
    /// </summary>
    [Fact]
    public void The_web_demos_copy_equals_the_catalogue()
    {
        var path = Path.Combine(RepositoryRoot.Directory.FullName, SnapshotPath);
        var expected = JsonSerializer.Serialize(CoverageResponse.Current, Wire).ReplaceLineEndings("\n") + "\n";

        if (Environment.GetEnvironmentVariable("SERVICEHUB_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(path, expected);
        }

        File.Exists(path).Should().BeTrue($"{SnapshotPath} is the demo's coverage data; create it with SERVICEHUB_UPDATE_SNAPSHOTS=1");
        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(expected,
            "the catalogue changed: rerun this test with SERVICEHUB_UPDATE_SNAPSHOTS=1 and commit the file");
    }
}
