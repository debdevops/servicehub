using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Signatures;

namespace ServiceHub.UnitTests.Infrastructure.Signatures;

/// <summary>Unit 3.1: two messages failing the same way share a signature; two failing differently do not.</summary>
public sealed class SignatureRecorderTests : IAsyncLifetime
{
    private static readonly Guid NamespaceId = Guid.NewGuid();
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync(); // proves migration 0005 applies, not just that the model builds
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private ServiceHubDbContext NewDb() => new(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);

    private static DlqMessage Message(long id, string reason = "MaxDeliveryCountExceeded", string entity = "orders", int deliveries = 10,
        string? description = null, string owner = "o1") => new()
    {
        MessageId = $"m{id}", SequenceNumber = id, BodyHash = "h", NamespaceId = NamespaceId, OwnerId = owner, CloudProvider = CloudProviderType.Azure,
        EntityName = entity, EntityType = ServiceBusEntityType.Queue, EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        DetectedAtUtc = DateTimeOffset.UtcNow, DeadLetterReason = reason, DeadLetterErrorDescription = description, DeliveryCount = deliveries,
    };

    [Fact]
    public async Task Messages_failing_the_same_way_share_one_signature_and_it_counts_them()
    {
        await using var db = NewDb();
        var a = await SignatureRecorder.AssignAsync(db, Message(1), default);
        var b = await SignatureRecorder.AssignAsync(db, Message(2), default);
        await db.SaveChangesAsync();

        a.Should().Be(b);
        var rows = await db.NamespaceSignatures.ToListAsync();
        rows.Should().ContainSingle().Which.OccurrenceCount.Should().Be(2);
    }

    [Fact]
    public async Task Messages_failing_differently_get_different_signatures()
    {
        await using var db = NewDb();
        var a = await SignatureRecorder.AssignAsync(db, Message(1), default);
        var b = await SignatureRecorder.AssignAsync(db, Message(2, reason: "ValidationFailed", description: "validation error"), default);
        var c = await SignatureRecorder.AssignAsync(db, Message(3, entity: "payments"), default);
        await db.SaveChangesAsync();

        new[] { a, b, c }.Distinct().Should().HaveCount(3);
        (await db.NamespaceSignatures.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task A_signature_seen_in_an_earlier_scan_is_counted_not_duplicated()
    {
        await using (var first = NewDb())
        {
            await SignatureRecorder.AssignAsync(first, Message(1), default);
            await first.SaveChangesAsync();
        }

        await using var second = NewDb();
        await SignatureRecorder.AssignAsync(second, Message(2), default);
        await second.SaveChangesAsync();

        (await second.NamespaceSignatures.SingleAsync()).OccurrenceCount.Should().Be(2);
    }

    [Fact]
    public async Task Owners_never_share_a_row()
    {
        await using var db = NewDb();
        await SignatureRecorder.AssignAsync(db, Message(1, owner: "o1"), default);
        await SignatureRecorder.AssignAsync(db, Message(2, owner: "o2"), default);
        await db.SaveChangesAsync();

        (await db.NamespaceSignatures.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_delivery_band_not_the_raw_count_is_what_differs()
    {
        await using var db = NewDb();
        var ten = await SignatureRecorder.AssignAsync(db, Message(1, deliveries: 10), default);
        var nine = await SignatureRecorder.AssignAsync(db, Message(2, deliveries: 9), default);
        await db.SaveChangesAsync();

        ten.Should().Be(nine, "both are the same 'medium' band — trust scores are keyed by this hash");
    }
}
