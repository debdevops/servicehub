using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Identity;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using Handle = ServiceHub.IntegrationTests.DeadLettersApiTests.Handle;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// The restart-recovery safety fix, end to end. A replay opens its ledger entry, calls the cloud, then records the answer. If
/// ServiceHub stops after the cloud accepted the send but before the answer is written, the message may already be back in the
/// queue and the dead letter still looks active — so nothing may send it again until a person says what happened.
/// </summary>
/// <remarks>
/// Each "process death" below is a real one as far as the data goes: the ledger is wrapped so that the write which would have
/// recorded the answer throws before reaching the database, the host is then thrown away with everything it held in memory,
/// and a <b>second host</b> is started over the same data directory. A thrown exception handled by the same live host would
/// not model that, and none of these tests relies on one.
/// </remarks>
public sealed class RestartRecoverySafetyApiTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"servicehub-api-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        ServiceHubApiFactory.ClearPoolFor(_directory);
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    public enum Dies { Never, AfterTheEntryIsOpened, AfterTheCloudAcceptedBeforeTheAnswerIsRecorded }

    /// <summary>Stands in for the process stopping at that instant: nothing after it runs.</summary>
    private sealed class ProcessStopped : Exception
    {
        public ProcessStopped(string where) : base($"The process stopped {where}.") { }
    }

    /// <summary>The real ledger, except that at one named instant the "process" stops before anything more is written.</summary>
    public class StoppingLedger : DispatchProxy
    {
        public IRecoveryLedger Target { get; set; } = null!;

        public Dies Dies { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            if (Dies == Dies.AfterTheCloudAcceptedBeforeTheAnswerIsRecorded && targetMethod.Name == nameof(IRecoveryLedger.RecordExecutionAsync))
            {
                throw new ProcessStopped("after the cloud accepted the send and before the answer was recorded");
            }

            object? result;
            try
            {
                result = targetMethod.Invoke(Target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }

            if (Dies == Dies.AfterTheEntryIsOpened && targetMethod.Name == nameof(IRecoveryLedger.BeginEntryAsync) && result is Task opened)
            {
                opened.GetAwaiter().GetResult(); // the entry is written...
                throw new ProcessStopped("right after the entry was opened, before the cloud was called");
            }

            return result;
        }
    }

    /// <summary>One run of ServiceHub over the shared data directory, with a fake Azure whose replays are counted.</summary>
    private Handle Start(PeekLog azure, Dies dies = Dies.Never)
    {
        var root = ServiceHubApiFactory.Reusing(_directory);
        var factory = root.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider)).ToList())
            {
                services.Remove(d);
            }

            services.AddSingleton<ICloudMessagingProvider>(new PeekableProvider(CloudProviderType.Azure, ProviderCapabilities.Azure, azure));
            services.AddSingleton<ICloudMessagingProvider>(new PeekableProvider(CloudProviderType.Aws, ProviderCapabilities.Aws, new PeekLog()));

            if (dies != Dies.Never)
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(IRecoveryLedger)).ToList())
                {
                    services.Remove(d);
                }

                services.AddScoped(sp =>
                {
                    var proxy = DispatchProxy.Create<IRecoveryLedger, StoppingLedger>();
                    var stopping = (StoppingLedger)(object)proxy;
                    stopping.Target = ActivatorUtilities.CreateInstance<RecoveryLedgerService>(sp);
                    stopping.Dies = dies;
                    return proxy;
                });
            }
        }));
        return new Handle(root, factory);
    }

    private static async Task<HttpResponseMessage> TryReplay(Handle host, long id)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/dead-letters/{id}/replay");
        request.Headers.Add("X-ServiceHub-Intent", "replay-message");
        try
        {
            return await host.Client.SendAsync(request);
        }
        catch (Exception)
        {
            // The host may surface the stopped "process" as an exception to the caller; either way it is the end of that run.
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<List<RecoveryLedgerEntry>> Entries(Handle host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        return await db.RecoveryLedgerEntries.AsNoTracking().ToListAsync();
    }

    /// <summary>Connects Azure, seeds one dead letter and returns its id. Run in the first host only; the second finds it on disk.</summary>
    private static async Task<(Guid Ns, long Id)> SeedOne(Handle host)
    {
        // The fake cloud holds no dead letters, so a scan would (correctly) find this one gone and resolve it. These tests are
        // about the case where the original is still there, so the monitor is paused; a pause survives a restart.
        var pause = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agents/dlq-monitor/pause");
        pause.Headers.Add("X-ServiceHub-Intent", "pause-agent");
        (await host.Client.SendAsync(pause)).StatusCode.Should().Be(HttpStatusCode.OK);

        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 1);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        return (ns, (await db.DlqMessages.AsNoTracking().FirstAsync()).Id);
    }

    private static async Task<JsonElement> PendingWork(Handle host) => await Json(await host.Client.GetAsync("/api/v1/pending-work"));

    [Fact]
    public async Task After_the_cloud_accepted_a_send_and_the_process_stopped_a_second_replay_is_blocked_everywhere_and_a_person_can_resolve_it()
    {
        long id;
        var firstCloud = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        using (var first = Start(firstCloud, Dies.AfterTheCloudAcceptedBeforeTheAnswerIsRecorded))
        {
            (_, id) = await SeedOne(first);
            await TryReplay(first, id);

            firstCloud.Replays.Should().Be(1, "the cloud was called and accepted the send");
            var left = (await Entries(first)).Should().ContainSingle().Subject;
            left.State.Should().Be(RecoveryEntryState.Executing, "the answer was never written: the claim is all the database holds");
        } // everything the first run held in memory is gone

        var secondCloud = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        using (var second = Start(secondCloud))
        {
            // Visible and not invented: unknown, not failed and not recovered.
            var entry = (await Entries(second)).Should().ContainSingle().Subject;
            entry.State.Should().Be(RecoveryEntryState.ExecutionUnknown, "a stale 'Executing' entry cannot belong to a live call, and its outcome is not known");
            entry.Disposition.Should().BeNull();
            entry.ClosedAt.Should().BeNull();

            var pending = await PendingWork(second);
            var item = pending.GetProperty("items").EnumerateArray().Should().ContainSingle(i => i.GetProperty("kind").GetString() == "unresolved").Subject;
            item.GetProperty("reasonCode").GetString().Should().Be("REPLAY_OUTCOME_UNKNOWN");
            item.GetProperty("reason").GetString().Should().Contain("Check the queue");

            using (var probe = second.Services.CreateScope())
            {
                var m = await probe.ServiceProvider.GetRequiredService<ServiceHubDbContext>().DlqMessages.AsNoTracking().FirstAsync();
                m.Status.Should().Be(DlqMessageStatus.Active, $"the scenario needs the original still in the queue (resolution cause: {m.ResolutionCause})");
            }

            // Manual path.
            var manual = await TryReplay(second, id);
            manual.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await Json(manual)).GetProperty("code").GetString().Should().Be("REPLAY_OUTCOME_UNKNOWN");

            // The replay proposal says so before anyone presses anything.
            var proposal = await Json(await second.Client.GetAsync($"/api/v1/dead-letters/{id}/replay-proposal"));
            proposal.GetProperty("canExecute").GetBoolean().Should().BeFalse();
            proposal.GetProperty("blockedCode").GetString().Should().Be("REPLAY_OUTCOME_UNKNOWN");

            // Automatic and approval-driven paths reach the same service and the same gate.
            using (var scope = second.Services.CreateScope())
            {
                var replay = scope.ServiceProvider.GetRequiredService<IDlqReplayService>();
                var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
                var ns = await db.Namespaces.AsNoTracking().FirstAsync();
                var rule = await replay.ReplayAsync(id, ns, ActorIdentityResolver.ResolveSystemActor("AutoReplayAgent"), "auto-replay", "rule-1", CancellationToken.None, ruleId: 1);
                rule.IsFailure.Should().BeTrue();
                rule.Error.Code.Should().Be("REPLAY_OUTCOME_UNKNOWN");
                var approved = await replay.ReplayAsync(id, ns, new RecoveryActor("approver", RecoveryActorKind.User), "approve-escalation", null, CancellationToken.None);
                approved.IsFailure.Should().BeTrue();
                approved.Error.Code.Should().Be("REPLAY_OUTCOME_UNKNOWN");
                var decision = await replay.CheckEligibilityAsync(id, ns, ActorIdentityResolver.ResolveSystemActor("AutoReplayAgent"), RecoveryOperationKind.Replay, CancellationToken.None);
                decision!.Verdict.Should().Be(EligibilityVerdict.Deny);
            }

            secondCloud.Replays.Should().Be(0, "no path may send an ambiguous attempt again");
            (await Entries(second)).Should().ContainSingle("a refused attempt must not add another open entry");
        }

        // Restarting again changes nothing more: still one entry, still unknown, still one 'unknown' event.
        using (var third = Start(new PeekLog { OnReplay = () => Result<bool>.Success(true) }))
        {
            (await Entries(third)).Should().ContainSingle().Which.State.Should().Be(RecoveryEntryState.ExecutionUnknown);
            using var scope = third.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            (await db.RecoveryEvents.CountAsync(e => e.EventType == RecoveryEventType.ExecutionUnknown)).Should().Be(1);
        }

        // A person looks at the queue and says what they found. Nothing is invented: it closes as written off, in their words.
        var fourthCloud = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        using (var fourth = Start(fourthCloud))
        {
            var entry = (await Entries(fourth)).Single();

            var noIntent = await fourth.Client.PostAsJsonAsync($"/api/v1/pending-work/{entry.Id}/resolve", new { reason = "x" });
            noIntent.StatusCode.Should().Be((HttpStatusCode)428);

            var noReason = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/pending-work/{entry.Id}/resolve") { Content = JsonContent.Create(new { reason = "  " }) };
            noReason.Headers.Add("X-ServiceHub-Intent", "resolve-unknown-outcome");
            (await fourth.Client.SendAsync(noReason)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var resolve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/pending-work/{entry.Id}/resolve") { Content = JsonContent.Create(new { reason = "queue holds one copy; nothing more to send" }) };
            resolve.Headers.Add("X-ServiceHub-Intent", "resolve-unknown-outcome");
            (await fourth.Client.SendAsync(resolve)).StatusCode.Should().Be(HttpStatusCode.NoContent);

            var closed = (await Entries(fourth)).Single();
            closed.State.Should().Be(RecoveryEntryState.WrittenOff);
            closed.Disposition.Should().Be(RecoveryDisposition.WrittenOff, "never Recovered, never Failed: ServiceHub does not know either");
            (await PendingWork(fourth)).GetProperty("items").EnumerateArray().Should().NotContain(i => i.GetProperty("kind").GetString() == "unresolved");

            // A resolved attempt cannot be resolved twice.
            (await fourth.Client.SendAsync(Copy(resolve, entry.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Only now is a fresh, gated, deliberate attempt possible.
            (await TryReplay(fourth, id)).StatusCode.Should().Be(HttpStatusCode.OK);
            fourthCloud.Replays.Should().Be(1);
        }
    }

    private static HttpRequestMessage Copy(HttpRequestMessage original, Guid entryId)
    {
        var again = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/pending-work/{entryId}/resolve") { Content = JsonContent.Create(new { reason = "again" }) };
        again.Headers.Add("X-ServiceHub-Intent", "resolve-unknown-outcome");
        return again;
    }

    [Fact]
    public async Task If_the_process_stopped_before_the_cloud_was_called_the_attempt_is_still_unresolved_because_nobody_can_tell()
    {
        long id;
        var firstCloud = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        using (var first = Start(firstCloud, Dies.AfterTheEntryIsOpened))
        {
            (_, id) = await SeedOne(first);
            await TryReplay(first, id);

            firstCloud.Replays.Should().Be(0, "the cloud was never called");
            (await Entries(first)).Should().ContainSingle().Which.State.Should().Be(RecoveryEntryState.Executing);
        }

        var secondCloud = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        using var second = Start(secondCloud);
        using (var probe = second.Services.CreateScope())
        {
            (await probe.ServiceProvider.GetRequiredService<ServiceHubDbContext>().DlqMessages.AsNoTracking().FirstAsync()).Status
                .Should().Be(DlqMessageStatus.Active, "the block must be the unresolved attempt, not a message that already left the queue");
        }

        (await Entries(second)).Should().ContainSingle().Which.State.Should().Be(RecoveryEntryState.ExecutionUnknown,
            "ServiceHub cannot know the cloud was not called, so it does not guess either way");
        (await TryReplay(second, id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        secondCloud.Replays.Should().Be(0);
    }

    [Fact]
    public async Task Two_replays_of_one_message_at_once_reach_the_cloud_once()
    {
        var cloud = new PeekLog
        {
            OnReplay = () =>
            {
                Thread.Sleep(400); // the first is still in flight when the second arrives
                return Result<bool>.Success(true);
            },
        };
        using var host = Start(cloud);
        var (_, id) = await SeedOne(host);

        var both = await Task.WhenAll(TryReplay(host, id), TryReplay(host, id));

        both.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict],
            "one wins the claim and the other is refused, whichever arrives first");
        cloud.Replays.Should().Be(1, "a second send of the same message is the duplicate this guards against");
        (await Entries(host)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_call_in_flight_on_a_running_host_is_not_mistaken_for_a_stale_one()
    {
        var cloud = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        using var host = Start(cloud);
        var (_, id) = await SeedOne(host);

        (await TryReplay(host, id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = (await Entries(host)).Single();
        entry.State.Should().Be(RecoveryEntryState.Observing, "the sweep at startup must leave a live host's own entries alone");
    }
}
