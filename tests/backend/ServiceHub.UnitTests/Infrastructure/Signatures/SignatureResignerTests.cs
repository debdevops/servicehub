using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using Microsoft.Extensions.Configuration;
using Moq;
using ServiceHub.Core.Helpers;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Rules;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Signatures;

namespace ServiceHub.UnitTests.Infrastructure.Signatures;

/// <summary>Design 10 step 4: the one-off re-sign. Safe to repeat, all-or-nothing, rebuilds the tallies. Not wired.</summary>
public sealed class SignatureResignerTests : IDisposable
{
    private const string Owner = "owner1";
    private const string Reason = "MaxDeliveryCountExceeded";
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly ServiceHubDbContext _db;
    private readonly Guid _ns = Guid.NewGuid();
    private long _seq;

    public SignatureResignerTests()
    {
        _db = new ServiceHubDbContext(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite("DataSource=:memory:").Options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    /// <summary>A dead letter signed the version-1 way, with its v1 tally row, exactly as 4.1.0 leaves the database.</summary>
    private async Task Seed(string? error, int minute, string entity = "orders", Guid? ns = null, string owner = Owner)
    {
        var m = new DlqMessage
        {
            MessageId = Guid.NewGuid().ToString(), SequenceNumber = _seq++, BodyHash = Guid.NewGuid().ToString("N"),
            NamespaceId = ns ?? _ns, OwnerId = owner, EntityName = entity, EntityType = ServiceBusEntityType.Queue,
            CloudProvider = CloudProviderType.Azure, EnqueuedTimeUtc = T0.AddMinutes(minute), DetectedAtUtc = T0.AddMinutes(minute),
            DeadLetterReason = Reason, DeadLetterErrorDescription = error,
        };
        m.SignatureHash = await SignatureRecorder.AssignAsync(_db, m, default);
        _db.DlqMessages.Add(m);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private Task<ResignResult> Run(bool dryRun = false, Guid? ns = null, int max = 20, Action<int>? progress = null) =>
        SignatureResigner.ResignNamespaceAsync(_db, Owner, ns ?? _ns, dryRun, max, progress);

    private async Task<(string?[] Hashes, string[] Tally)> Snapshot(Guid? ns = null)
    {
        var id = ns ?? _ns;
        var hashes = await _db.DlqMessages.AsNoTracking().Where(m => m.NamespaceId == id).OrderBy(m => m.SequenceNumber).Select(m => m.SignatureHash).ToArrayAsync();
        var tally = await _db.NamespaceSignatures.AsNoTracking().Where(s => s.NamespaceId == id).OrderBy(s => s.SignatureHash)
            .Select(s => s.SignatureHash + ":" + s.OccurrenceCount).ToArrayAsync();
        return (hashes, tally);
    }

    [Fact]
    public async Task Two_causes_that_version_1_merged_become_two_signatures_with_their_own_counts()
    {
        await Seed("Inventory service timed out after 30 seconds", 0);
        await Seed("Customer 42 does not exist", 1);
        await Seed("Customer 77 does not exist", 2);
        (await Snapshot()).Tally.Should().HaveCount(1, "version 1 merged them");

        var result = await Run();

        result.Should().Be(new ResignResult(3, 3, 1, 2, 0, true));
        var after = await Snapshot();
        after.Hashes.Distinct().Should().HaveCount(2);
        after.Hashes[1].Should().Be(after.Hashes[2]);
        after.Tally.Select(t => t.Split(':')[1]).Order().Should().Equal("1", "2");
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        await Seed("a failed", 0);
        await Seed("b failed", 1);
        await Run();
        var first = await Snapshot();

        var second = await Run();

        second.MessagesChanged.Should().Be(0);
        (await Snapshot()).Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task A_run_that_stops_half_way_leaves_everything_as_it_was_and_a_rerun_reaches_the_same_end()
    {
        await Seed("a failed", 0, entity: "q1");
        await Seed("b failed", 1, entity: "q2");
        await Seed("c failed", 2, entity: "q3");
        var original = await Snapshot();

        var crash = () => Run(progress: done => { if (done >= 2) throw new InvalidOperationException("killed"); });
        await crash.Should().ThrowAsync<InvalidOperationException>();
        (await Snapshot()).Should().BeEquivalentTo(original, "the namespace is one transaction");

        await Run();
        var finished = await Snapshot();
        finished.Hashes.Should().NotEqual(original.Hashes);

        // A clean run on an identical database ends in the same place.
        var other = Guid.NewGuid();
        await Seed("a failed", 0, entity: "q1", ns: other);
        await Seed("b failed", 1, entity: "q2", ns: other);
        await Seed("c failed", 2, entity: "q3", ns: other);
        await Run(ns: other);
        (await Snapshot(other)).Tally.Select(t => t.Split(':')[1]).Should().Equal(finished.Tally.Select(t => t.Split(':')[1]));
    }

    [Fact]
    public async Task A_dry_run_reports_the_change_and_writes_nothing()
    {
        await Seed("x failed", 0);
        await Seed("y failed", 1);
        var before = await Snapshot();

        var result = await Run(dryRun: true);

        result.Saved.Should().BeFalse();
        result.MessagesChanged.Should().Be(2);
        result.SignaturesAfter.Should().Be(2);
        (await Snapshot()).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Too_many_shapes_in_one_queue_fold_into_the_other_group_oldest_first()
    {
        for (var i = 0; i < 5; i++)
        {
            await Seed($"shape number {(char)('a' + i)} failed", i);
        }

        var result = await Run(max: 3);

        result.FoldedIntoOther.Should().Be(2);
        result.SignaturesAfter.Should().Be(4);
        var hashes = (await Snapshot()).Hashes;
        hashes[3].Should().Be(hashes[4]);
        hashes.Take(3).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task The_tally_is_rebuilt_from_the_messages_with_first_and_last_seen()
    {
        await Seed("same 1 failed", 5);
        await Seed("same 2 failed", 1);
        await Seed("same 3 failed", 9);
        await Run();

        var row = await _db.NamespaceSignatures.AsNoTracking().SingleAsync();
        row.OccurrenceCount.Should().Be(3);
        row.FirstSeenAt.Should().Be(T0.AddMinutes(1));
        row.LastSeenAt.Should().Be(T0.AddMinutes(9));
        row.EntityName.Should().Be("orders");
        row.DominantDeadletterReason.Should().Be(Reason);
    }

    [Fact]
    public async Task Other_namespaces_and_other_owners_are_left_alone()
    {
        var elsewhere = Guid.NewGuid();
        await Seed("a failed", 0, ns: elsewhere);
        await Seed("a failed", 0, owner: "someone-else");
        await Seed("a failed", 1);
        var untouched = await Snapshot(elsewhere);

        await Run();

        (await Snapshot(elsewhere)).Should().BeEquivalentTo(untouched);
        (await _db.DlqMessages.AsNoTracking().Where(m => m.OwnerId == "someone-else").Select(m => m.SignatureHash).SingleAsync())
            .Should().Be(untouched.Hashes[0], "same text, same v1 hash, never re-signed");
    }

    [Fact]
    public async Task Rows_that_were_never_signed_get_signed()
    {
        await Seed("a failed", 0);
        await _db.DlqMessages.ExecuteUpdateAsync(s => s.SetProperty(m => m.SignatureHash, (string?)null));

        var result = await Run();

        result.MessagesChanged.Should().Be(1);
        (await Snapshot()).Hashes.Single().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_namespace_with_nothing_in_it_is_a_clean_no_op()
        => (await Run()).Should().Be(new ResignResult(0, 0, 0, 0, 0, true));

    // ── Step 7: rules made from an old signature ─────────────────────────────

    private async Task<AutoReplayRule> RuleOn(string? hash, string owner = Owner, bool enabled = true)
    {
        var rule = new AutoReplayRule
        {
            OwnerId = owner, Name = "orders rule", Provider = CloudProviderType.Azure, Reason = Reason, EntityName = "orders",
            SignatureHash = hash, CreatedAt = T0, Enabled = enabled,
        };
        _db.AutoReplayRules.Add(rule);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return rule;
    }

    private Task<AutoReplayRule> Reload(long id) => _db.AutoReplayRules.AsNoTracking().SingleAsync(r => r.Id == id);

    [Fact]
    public async Task A_rule_made_from_a_signature_that_no_longer_exists_is_switched_off_and_told_why()
    {
        await Seed("Inventory service timed out after 30 seconds", 0);
        await Seed("Customer 42 does not exist", 1);
        var oldHash = (await Snapshot()).Hashes[0]!;
        var rule = await RuleOn(oldHash);

        var result = await Run();

        result.RulesDisabled.Should().Be(1);
        var after = await Reload(rule.Id);
        after.Enabled.Should().BeFalse();
        after.DisabledReason.Should().Be(SignatureResigner.StaleRuleReason);
        after.DisabledDetail.Should().Contain("Make a new rule").And.Contain("Customer 42 does not exist");
        after.DisabledDetail!.Length.Should().BeLessThanOrEqualTo(512);
        after.SignatureHash.Should().Be(oldHash, "never deleted and never re-pointed: which group it covers is a person's choice");
    }

    [Fact]
    public async Task A_rule_stays_alive_while_another_namespace_still_carries_its_signature()
    {
        var elsewhere = Guid.NewGuid();
        await Seed("Customer 42 does not exist", 0);
        await Seed("Customer 42 does not exist", 0, ns: elsewhere);
        var oldHash = (await Snapshot()).Hashes[0]!;
        var rule = await RuleOn(oldHash);

        (await Run()).RulesDisabled.Should().Be(0);
        (await Reload(rule.Id)).Enabled.Should().BeTrue();

        (await Run(ns: elsewhere)).RulesDisabled.Should().Be(1, "the last namespace to move takes the signature away");
        (await Reload(rule.Id)).DisabledReason.Should().Be(SignatureResigner.StaleRuleReason);
    }

    [Fact]
    public async Task Rules_with_no_signature_or_a_different_one_or_another_owner_are_left_alone()
    {
        await Seed("Customer 42 does not exist", 0);
        var oldHash = (await Snapshot()).Hashes[0]!;
        var general = await RuleOn(null);
        var unrelated = await RuleOn("0000");
        var someoneElses = await RuleOn(oldHash, owner: "someone-else");

        (await Run()).RulesDisabled.Should().Be(0);

        foreach (var r in new[] { general, unrelated, someoneElses })
        {
            (await Reload(r.Id)).Enabled.Should().BeTrue();
        }
    }

    [Fact]
    public async Task A_dry_run_counts_the_rules_it_would_switch_off_without_touching_them()
    {
        await Seed("Customer 42 does not exist", 0);
        var rule = await RuleOn((await Snapshot()).Hashes[0]);

        (await Run(dryRun: true)).RulesDisabled.Should().Be(1);

        (await Reload(rule.Id)).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_second_run_does_not_switch_anything_off_again()
    {
        await Seed("Customer 42 does not exist", 0);
        await RuleOn((await Snapshot()).Hashes[0]);
        await Run();

        (await Run()).RulesDisabled.Should().Be(0);
    }

    [Fact]
    public async Task A_person_cannot_turn_a_split_rule_back_on()
    {
        await Seed("Customer 42 does not exist", 0);
        var rule = await RuleOn((await Snapshot()).Hashes[0]);
        await Run();
        var service = new RulesService(_db, Mock.Of<INamespaceRepository>(), Mock.Of<IDlqReplayService>(), new ConfigurationBuilder().Build());

        var turnedOn = await service.SetEnabledAsync(Owner, rule.Id, true, default);

        turnedOn.IsSuccess.Should().BeFalse();
        turnedOn.Error.Code.Should().Be("RULE_SIGNATURE_SPLIT");
        (await Reload(rule.Id)).Enabled.Should().BeFalse();
    }
}
