using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.IO.Compression;
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
        ServiceHubApiFactory.ClearPoolFor(_dir);
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
        var body = on ? (object)new { active = true, reason = "drill", confirm = "STOP" } : new { active = false, reason = "drill over", confirm = "LIFT" };
        var response = await client.SendAsync(Req(HttpMethod.Post, "/api/v1/settings/emergency-stop", "emergency-stop", body));
        // Say what the server said: an intermittent 500 seen once in a full run (2026-09-29) could not be diagnosed from the status alone.
        response.IsSuccessStatusCode.Should().BeTrue($"emergency stop {(on ? "on" : "off")} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
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

        ServiceHubApiFactory.ClearPoolFor(_dir);
        using var second = ServiceHubApiFactory.Reusing(_dir);
        var restarted = second.CreateClient();
        var chain = await Json(await restarted.GetAsync("/api/v1/recovery/chain"));
        chain.GetProperty("isValid").GetBoolean().Should().BeTrue();
        chain.GetProperty("eventsChecked").GetInt64().Should().Be(2, "the event recorded after the backup is gone");
        (await Json(await restarted.GetAsync("/api/v1/admin/backup"))).GetProperty("pending").ValueKind.Should().Be(JsonValueKind.Null);
        Directory.EnumerateFiles(_dir, "servicehub.db.before-restore-*").Should().NotBeEmpty("the replaced database is kept, never deleted");
    }

    [Fact]
    public async Task A_backup_can_be_downloaded_and_a_staged_restore_can_be_cancelled_before_the_next_start()
    {
        using var host = ServiceHubApiFactory.Reusing(_dir);
        var client = host.CreateClient();
        await Stop(client, on: true);
        await Stop(client, on: false);
        var id = (await Json(await client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/backup", "create-backup")))).GetProperty("backupId").GetString()!;

        var download = await client.GetAsync($"/api/v1/admin/backup/{id}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.sqlite3");
        (await download.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
        (await client.GetAsync("/api/v1/admin/backup/no-such-backup/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.SendAsync(Req(HttpMethod.Delete, "/api/v1/admin/backup/pending", "restore-backup"))).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "nothing is staged yet");
        (await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/backup/{id}/restore", "restore-backup", new { confirm = "RESTORE" }))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.SendAsync(Req(HttpMethod.Delete, "/api/v1/admin/backup/pending"))).StatusCode.Should().Be((HttpStatusCode)428);
        (await client.SendAsync(Req(HttpMethod.Delete, "/api/v1/admin/backup/pending", "restore-backup"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Json(await client.GetAsync("/api/v1/admin/backup"))).GetProperty("pending").ValueKind.Should().Be(JsonValueKind.Null, "a cancelled restore is not applied at the next start");
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

    [Fact]
    public async Task A_manifest_that_names_a_file_outside_its_own_folder_is_refused_even_when_that_files_checksum_matches()
    {
        // A backup folder is trusted input only as far as its own files go: a manifest whose `sqlite.fileName` is an absolute path (or climbs out
        // with `..`) must not make the restore check read — and then stage — some other SQLite file on the machine.
        using var host = ServiceHubApiFactory.Reusing(_dir);
        var client = host.CreateClient();
        var id = (await Json(await client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/backup", "create-backup")))).GetProperty("backupId").GetString()!;
        var bundle = Path.Combine(_dir, "backups", id);
        var manifestPath = Path.Combine(bundle, "manifest.json");
        var original = await File.ReadAllTextAsync(manifestPath);
        var elsewhere = Path.Combine(_dir, "elsewhere.db");
        File.Copy(Path.Combine(bundle, "servicehub-dlq.db"), elsewhere);
        var sha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(elsewhere)));
        var shaNow = JsonDocument.Parse(original).RootElement.GetProperty("sqlite").GetProperty("sha256").GetString()!;

        foreach (var name in new[] { elsewhere, "../../elsewhere.db" })
        {
            await File.WriteAllTextAsync(manifestPath, original.Replace("\"fileName\": \"servicehub-dlq.db\"", $"\"fileName\": {JsonSerializer.Serialize(name)}").Replace(shaNow, sha));
            var check = await Json(await client.GetAsync($"/api/v1/admin/backup/{id}/check"));
            check.GetProperty("canRestore").GetBoolean().Should().BeFalse($"a manifest naming '{name}' must be refused");
            (await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/backup/{id}/restore", "restore-backup", new { confirm = "RESTORE" })))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task Start_fresh_backs_up_first_begins_empty_at_the_next_start_and_the_backup_brings_everything_back()
    {
        string backupId;
        using (var first = ServiceHubApiFactory.Reusing(_dir))
        {
            var client = first.CreateClient();
            await Stop(client, on: true);
            await Stop(client, on: false);

            (await client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/backup/start-fresh", body: new { confirm = "START FRESH" })))
                .StatusCode.Should().Be((HttpStatusCode)428);
            (await client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/backup/start-fresh", "start-fresh", new { confirm = "yes" })))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest, "starting fresh must be deliberate");
            var staged = await client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/backup/start-fresh", "start-fresh", new { confirm = "START FRESH" }));
            staged.StatusCode.Should().Be(HttpStatusCode.OK);
            backupId = (await Json(staged)).GetProperty("backupId").GetString()!;

            var list = await Json(await client.GetAsync("/api/v1/admin/backup"));
            list.GetProperty("pending").GetProperty("kind").GetString().Should().Be("fresh");
            list.GetProperty("backups").EnumerateArray().Select(b => b.GetProperty("backupId").GetString()).Should().Contain(backupId);
            (await LedgerEvents(client)).Should().Be(2, "nothing is swapped under a running server");
        }

        ServiceHubApiFactory.ClearPoolFor(_dir);
        using (var second = ServiceHubApiFactory.Reusing(_dir))
        {
            var client = second.CreateClient();
            (await LedgerEvents(client)).Should().Be(0, "it started as a new installation");
            Directory.EnumerateFiles(_dir, "servicehub.db.before-fresh-*").Should().NotBeEmpty("the old database is kept, never deleted");
            var list = await Json(await client.GetAsync("/api/v1/admin/backup"));
            list.GetProperty("pending").ValueKind.Should().Be(JsonValueKind.Null);
            list.GetProperty("backups").EnumerateArray().Select(b => b.GetProperty("backupId").GetString()).Should().Contain(backupId, "backups survive a fresh start");

            var restore = await client.SendAsync(Req(HttpMethod.Post, $"/api/v1/admin/backup/{backupId}/restore", "restore-backup", new { confirm = "RESTORE" }));
            restore.StatusCode.Should().Be(HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
            (await Json(restore)).GetProperty("checks").EnumerateArray().Last().GetProperty("name").GetString()
                .Should().Contain("backed up first", "a restore keeps what it replaces as a backup too");
        }

        ServiceHubApiFactory.ClearPoolFor(_dir);
        using var third = ServiceHubApiFactory.Reusing(_dir);
        var chain = await Json(await third.CreateClient().GetAsync("/api/v1/recovery/chain"));
        chain.GetProperty("isValid").GetBoolean().Should().BeTrue();
        chain.GetProperty("eventsChecked").GetInt64().Should().Be(2, "the backup taken before the fresh start came back");
    }

    [Fact]
    public async Task An_exported_backup_imports_as_a_new_restorable_backup_and_a_tampered_one_is_refused()
    {
        using var host = ServiceHubApiFactory.Reusing(_dir);
        var client = host.CreateClient();
        await Stop(client, on: true);
        var id = (await Json(await client.SendAsync(Req(HttpMethod.Post, "/api/v1/admin/backup", "create-backup")))).GetProperty("backupId").GetString()!;

        var export = await client.GetAsync($"/api/v1/admin/backup/{id}/export");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        var zipBytes = await export.Content.ReadAsByteArrayAsync();

        async Task<HttpResponseMessage> Import(byte[] bytes, string? intent = "import-backup")
        {
            var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "backup.zip" } };
            var r = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/backup/import") { Content = form };
            if (intent is not null) r.Headers.Add("X-ServiceHub-Intent", intent);
            return await client.SendAsync(r);
        }

        (await Import(zipBytes, intent: null)).StatusCode.Should().Be((HttpStatusCode)428);
        (await Import([1, 2, 3])).StatusCode.Should().Be(HttpStatusCode.BadRequest, "not a zip");

        var imported = await Import(zipBytes);
        imported.StatusCode.Should().Be(HttpStatusCode.OK, await imported.Content.ReadAsStringAsync());
        var check = await Json(imported);
        check.GetProperty("backupId").GetString().Should().StartWith("imported-");
        check.GetProperty("canRestore").GetBoolean().Should().BeTrue(check.GetRawText());
        (await Json(await client.GetAsync("/api/v1/admin/backup"))).GetProperty("backups").GetArrayLength().Should().Be(2);

        // One byte of the database changed after export: the checksum in the manifest no longer matches, so nothing is kept.
        using var tampered = new MemoryStream();
        using (var src = new ZipArchive(new MemoryStream(zipBytes)))
        using (var dst = new ZipArchive(tampered, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var e in src.Entries)
            {
                using var buf = new MemoryStream();
                await using (var s = e.Open()) await s.CopyToAsync(buf);
                var data = buf.ToArray();
                if (e.Name.EndsWith(".db")) data[^1] ^= 0xFF;
                await using var o = dst.CreateEntry(e.Name).Open();
                await o.WriteAsync(data);
            }
        }

        var refused = await Import(tampered.ToArray());
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(await client.GetAsync("/api/v1/admin/backup"))).GetProperty("backups").GetArrayLength().Should().Be(2, "a refused import leaves nothing behind");
        Directory.EnumerateDirectories(Path.Combine(_dir, "backups")).Should().HaveCount(2);
    }
}
