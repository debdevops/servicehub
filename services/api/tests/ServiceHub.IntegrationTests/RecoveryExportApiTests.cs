using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Evidence export (unit 6.12): the file leaves ServiceHub and is checked by the offline verifier, which never imports
/// ServiceHub code. The test runs that script for real — PASS as exported, FAIL after one byte is changed.
/// </summary>
public sealed class RecoveryExportApiTests
{
    private static async Task RecordThreeEvents(HttpClient client)
    {
        foreach (var body in new object[] { new { active = true, reason = "drill", confirm = "STOP" }, new { active = false }, new { active = true, reason = "drill 2", confirm = "STOP" } })
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "/api/v1/settings/emergency-stop") { Content = JsonContent.Create(body) };
            r.Headers.Add("X-ServiceHub-Intent", "emergency-stop");
            (await client.SendAsync(r)).EnsureSuccessStatusCode();
            await Task.Delay(20);
        }
    }

    private static (int Exit, string Output) Verify(string json)
    {
        var dir = Directory.CreateTempSubdirectory("sh-evidence-");
        var file = Path.Combine(dir.FullName, "evidence.json");
        File.WriteAllText(file, json);
        var script = FindScript();
        using var p = Process.Start(new ProcessStartInfo("python3", $"\"{script}\" \"{file}\"") { RedirectStandardOutput = true, RedirectStandardError = true })!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        dir.Delete(recursive: true);
        return (p.ExitCode, output);
    }

    private static string FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "verify-recovery-chain.py");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("scripts/verify-recovery-chain.py not found above the test output.");
    }

    [Fact]
    public async Task An_export_passes_the_offline_verifier_and_one_changed_byte_makes_it_fail()
    {
        using var host = DeadLettersApiTests.Host();
        await RecordThreeEvents(host.Client);

        var response = await host.Client.GetAsync("/api/v1/recovery/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().StartWith("servicehub-evidence-");
        var json = await response.Content.ReadAsStringAsync();

        var doc = JsonNode.Parse(json)!;
        doc["manifest"]!["partial"]!.GetValue<bool>().Should().BeFalse();
        doc["events"]!.AsArray().Should().HaveCount(3);
        doc["events"]![0]!["eventType"]!.GetValue<string>().Should().MatchRegex("^[A-Z]", "the verifier hashes the enum's own name, not camelCase");

        var pass = Verify(json);
        pass.Exit.Should().Be(0, pass.Output);

        var forged = json.Replace("drill 2", "drill 3");
        forged.Should().NotBe(json);
        var fail = Verify(forged);
        fail.Exit.Should().Be(1, fail.Output);
        fail.Output.Should().Contain("EntryHash mismatch");
    }

    [Fact]
    public async Task A_range_is_an_unbroken_run_of_the_chain_and_says_it_is_partial()
    {
        using var host = DeadLettersApiTests.Host();
        await RecordThreeEvents(host.Client);
        var all = JsonNode.Parse(await host.Client.GetStringAsync("/api/v1/recovery/export"))!["events"]!.AsArray();
        var second = Uri.EscapeDataString(all[1]!["occurredAt"]!.GetValue<string>());

        var json = await host.Client.GetStringAsync($"/api/v1/recovery/export?from={second}");
        var doc = JsonNode.Parse(json)!;
        doc["manifest"]!["partial"]!.GetValue<bool>().Should().BeTrue();
        doc["manifest"]!["chain"]!["firstSeq"]!.GetValue<long>().Should().Be(2);
        doc["events"]!.AsArray().Select(e => e!["seq"]!.GetValue<long>()).Should().Equal(2, 3);
        Verify(json).Exit.Should().Be(0);

        (await host.Client.GetAsync($"/api/v1/recovery/export?from={second}&to=2000-01-01T00:00:00Z")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
