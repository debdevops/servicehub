using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Backup and restore (unit 6.13), end to end: a backup taken from a running server is staged, the server stops, a fresh one
/// starts on the same data directory, and what comes back is the backup — with its ledger chain still verifying.
/// </summary>
public sealed class BackupApiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"servicehub-backup-api-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static HttpRequestMessage Req(HttpMethod m, string url, string? intent = null, object? body = null)
    {
        var r = new HttpRequestMessage(m, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (intent is not null) r.Headers.Add("X-ServiceHub-Intent", intent);
        return r;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static async Task Stop(HttpClient client, bool on)
    {
        var body = on ? (object)new { active = true, reason = "drill", confirm = "STOP" } : new { active = false };
        (await client.SendAsync(Req(HttpMethod.Post, "/api/v1/settings/emergency-stop", "emergency-stop", body))).EnsureSuccessStatusCode();
    }

    private static async Task<long> LedgerEvents(HttpClient client) =>
        (await Json(await client.GetAsync("/api/v1/recovery/chain"))).GetProperty("eventsChecked").GetInt64();

    [Fact]
    public async Task A_backup_taken_live_is_staged_and_comes_back_on_the_next_start_with_its_chain_verifying()
    {
        string backupId;
        using (var first = ServiceHubApiFactory.Reusing(_dir))
        {
            var client = first.CreateClient();
            await Stop(client, on: true);
            await Stop(client, on: false);

            (await client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/backup"))).StatusCode.Should().Be((HttpStatusCode)428);
            var created = await client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/backup", "create-backup"));
            created.StatusCode.Should().Be(HttpStatusCode.OK);
            var manifest = await Json(created);
            backupId = manifest.GetProperty("backupId").GetString()!;
            manifest.GetRawText().Should().NotContain("\"key\"", "the backup carries the key's fingerprint, never the key");

            // Recorded after the backup — the restore must take this away.
            await Stop(client, on: true);
            (await LedgerEvents(client)).Should().Be(3);

            var check = await Json(await client.GetAsync($"/api/v1/admin/backup/{backupId}/check"));
            check.GetProperty("canRestore").GetBoolean().Should().BeTrue(check.GetRawText());

            (await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/backup/{backupId}/restore", "restore-backup", new { confirm = "yes" })))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest, "restoring must be deliberate: RESTORE typed");
            (await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/backup/{backupId}/restore", "restore-backup", new { confirm = "RESTORE" })))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var list = await Json(await client.GetAsync("/api/v1/admin/backup"));
            list.GetProperty("pending").GetProperty("backupId").GetString().Should().Be(backupId);
            (await LedgerEvents(client)).Should().Be(3, "nothing is swapped under a running server");
        }

        SqliteConnection.ClearAllPools();
        using var second = ServiceHubApiFactory.Reusing(_dir);
        var restarted = second.CreateClient();
        var chain = await Json(await restarted.GetAsync("/api/v1/recovery/chain"));
        chain.GetProperty("isValid").GetBoolean().Should().BeTrue();
        chain.GetProperty("eventsChecked").GetInt64().Should().Be(2, "the event recorded after the backup is gone");
        (await Json(await restarted.GetAsync("/api/v1/admin/backup"))).GetProperty("pending").ValueKind.Should().Be(JsonValueKind.Null);
        Directory.EnumerateFiles(_dir, "servicehub.db.before-restore-*").Should().NotBeEmpty("the replaced database is kept, never deleted");
    }

    [Fact]
    public async Task A_bundle_is_refused_when_changed_after_backup_made_with_another_key_or_holding_a_broken_chain()
    {
        using var host = ServiceHubApiFactory.Reusing(_dir);
        var client = host.CreateClient();
        await Stop(client, on: true);
        await Stop(client, on: false);
        var id = (await Json(await client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/backup", "create-backup")))).GetProperty("backupId").GetString()!;
        var bundle = Path.Combine(_dir, "backups", id);
        var db = Path.Combine(bundle, "servicehub-dlq.db");
        var manifestPath = Path.Combine(bundle, "manifest.json");
        var original = await File.ReadAllTextAsync(manifestPath);

        async Task<JsonElement> Failed()
        {
            var check = await Json(await client.GetAsync($"/api/v1/admin/backup/{id}/check"));
            check.GetProperty("canRestore").GetBoolean().Should().BeFalse();
            (await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/backup/{id}/restore", "restore-backup", new { confirm = "RESTORE" })))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);
            return check.GetProperty("checks").EnumerateArray().Last();
        }

        // Another key.
        await File.WriteAllTextAsync(manifestPath, original.Replace("\"encryptionKeyFingerprint\": \"", "\"encryptionKeyFingerprint\": \"other-"));
        var key = (await Json(await client.GetAsync($"/api/v1/admin/backup/{id}/check"))).GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString()!.Contains("encryption key"));
        key.GetProperty("passed").GetBoolean().Should().BeFalse();
        await Failed();
        await File.WriteAllTextAsync(manifestPath, original);

        // A broken chain: one ledger field changed, and the checksum recomputed so only the chain can catch it.
        await using (var c = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE RecoveryEvents SET ActorIdentity = 'someone-else' WHERE Seq = 1;";
            (await cmd.ExecuteNonQueryAsync()).Should().Be(1);
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(db)));
        var shaNow = JsonDocument.Parse(original).RootElement.GetProperty("sqlite").GetProperty("sha256").GetString()!;
        await File.WriteAllTextAsync(manifestPath, original.Replace(shaNow, sha));
        var chain = await Failed();
        chain.GetProperty("name").GetString().Should().Contain("ledger");
        chain.GetProperty("detail").GetString().Should().Contain("breaks at event 1");

        // Changed after backup: the checksum no longer matches.
        await File.WriteAllTextAsync(manifestPath, original);
        (await Failed()).GetProperty("name").GetString().Should().Contain("the one that was backed up");
    }
}
