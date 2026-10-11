using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Helpers;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Signatures;

namespace ServiceHub.UnitTests.Infrastructure.Signatures;

/// <summary>Design 10 §5: the capped shape is recomputed from the dead letters already recorded, oldest first. Not wired yet.</summary>
public sealed class ErrorTemplateCatalogTests : IDisposable
{
    private const string Owner = "owner1";
    private const string Reason = "MaxDeliveryCountExceeded";
    private readonly ServiceHubDbContext _db;
    private readonly Guid _ns = Guid.NewGuid();
    private readonly DateTimeOffset _t0 = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
    private long _seq;

    public ErrorTemplateCatalogTests()
    {
        _db = new ServiceHubDbContext(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite("DataSource=:memory:").Options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private DlqMessage Make(string? error, int minute, string entity = "orders", string? reason = Reason, Guid? ns = null) => new()
    {
        MessageId = Guid.NewGuid().ToString(), SequenceNumber = _seq++, BodyHash = Guid.NewGuid().ToString("N"),
        NamespaceId = ns ?? _ns, OwnerId = Owner, EntityName = entity, EntityType = ServiceBusEntityType.Queue,
        CloudProvider = CloudProviderType.Azure, EnqueuedTimeUtc = _t0.AddMinutes(minute), DetectedAtUtc = _t0.AddMinutes(minute),
        DeadLetterReason = reason, DeadLetterErrorDescription = error,
    };

    private async Task Save(params DlqMessage[] m)
    {
        _db.DlqMessages.AddRange(m);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_shape_is_its_own_while_the_queue_has_room()
    {
        await Save(Make("Customer 1 not found", 0));
        var next = Make("Customer 99 not found", 1);

        (await ErrorTemplateCatalog.TemplateForAsync(_db, next, 20, default)).Should().Be("customer <n> not found");
    }

    [Fact]
    public async Task Once_the_queue_is_full_a_new_shape_folds_but_a_known_one_does_not()
    {
        await Save(Make("alpha failed", 0), Make("beta failed", 1), Make("gamma failed", 2));

        (await ErrorTemplateCatalog.TemplateForAsync(_db, Make("delta failed", 3), 3, default)).Should().Be(ErrorTemplateCap.Other);
        (await ErrorTemplateCatalog.TemplateForAsync(_db, Make("beta failed", 3), 3, default)).Should().Be("beta failed");
    }

    [Fact]
    public async Task The_first_shapes_seen_win_the_slots_regardless_of_insert_order()
    {
        await Save(Make("late failed", 30), Make("early failed", 0), Make("middle failed", 10));

        var known = await ErrorTemplateCatalog.KnownAsync(_db, Make("x", 40), 2, default);

        known.Should().Equal("early failed", "middle failed");
    }

    [Fact]
    public async Task Other_queues_reasons_and_namespaces_do_not_use_this_ones_slots()
    {
        await Save(Make("a failed", 0, entity: "other-queue"), Make("b failed", 1, reason: "Different"), Make("c failed", 2, ns: Guid.NewGuid()));

        (await ErrorTemplateCatalog.KnownAsync(_db, Make("x", 5), 20, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_message_with_no_error_text_has_the_empty_shape_and_uses_no_slot()
    {
        await Save(Make("a failed", 0));

        (await ErrorTemplateCatalog.TemplateForAsync(_db, Make(null, 1), 1, default)).Should().BeEmpty();
        (await ErrorTemplateCatalog.KnownAsync(_db, Make("x", 2), 5, default)).Should().Equal("a failed");
    }

    [Fact]
    public async Task Messages_added_earlier_in_the_same_scan_but_not_saved_still_count()
    {
        var first = Make("first failed", 0);
        var second = Make("second failed", 1);
        _db.DlqMessages.AddRange(first, second);   // tracked, not saved

        (await ErrorTemplateCatalog.TemplateForAsync(_db, Make("third failed", 2), 2, default)).Should().Be(ErrorTemplateCap.Other);
    }

    [Fact]
    public async Task A_message_never_counts_against_itself()
    {
        var m = Make("only failed", 0);
        await Save(m);

        (await ErrorTemplateCatalog.TemplateForAsync(_db, m, 1, default)).Should().Be("only failed");
    }
}
