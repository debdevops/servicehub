using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>
/// What every <see cref="IComponentSource"/> must pass (PLAN-PLUG-AND-PLAY-COMPONENTS §3.5). A module inherits this class, arranges its
/// fake cloud in the abstract methods, and gets the same checks as every other module: the offline counterpart of
/// <c>scripts/conformance-suite.py</c>. A new module that cannot pass a check here is not ready to be called Supported.
/// </summary>
/// <remarks>The fake cloud must record the name of every operation called on it, so the read-only promise is a measured fact.</remarks>
[Trait("Category", "Conformance")]
public abstract class ComponentSourceConformance
{
    /// <summary>A secret the fake cloud places in an error or a failure text. It must never come out of the source.</summary>
    protected const string Secret = "Password=hunter2-not-for-display";

    private static readonly string[] ReadVerbs = ["List", "Describe", "Get", "Query", "Read"];

    /// <summary>The source under test, wired to the fake cloud.</summary>
    protected abstract IComponentSource Source { get; }

    /// <summary>A connection the source can read with.</summary>
    protected abstract Namespace Connection { get; }

    /// <summary>One place the arranged cloud holds failures in.</summary>
    protected abstract string PlaceId { get; }

    /// <summary>One failed item the arranged cloud holds, readable in full.</summary>
    protected abstract string ItemId { get; }

    /// <summary>The name of every cloud operation called so far, in order (no property reads, no disposals).</summary>
    protected abstract IReadOnlyList<string> CloudCalls { get; }

    /// <summary>The cloud holds <paramref name="count"/> failed items in <see cref="PlaceId"/>, and pages at <see cref="ComponentSourceLimits.MaxFailedItems"/> as the real one does.</summary>
    protected abstract void ArrangeFailures(int count);

    /// <summary>The credential is refused by the cloud.</summary>
    protected abstract void ArrangeAccessDenied();

    /// <summary>Every cloud call fails with this message.</summary>
    protected abstract void ArrangeCloudFailure(string message);

    /// <summary>The failed item's free text (error, cause) carries <paramref name="secret"/>.</summary>
    protected abstract void ArrangeSecretInFailureText(string secret);

    /// <summary>The cloud no longer has <see cref="ItemId"/>.</summary>
    protected abstract void ArrangeMissingItem();

    /// <summary>The cloud honours a cancelled token the way a real client does.</summary>
    protected abstract void ArrangeCancellationAware();

    [Fact]
    public void It_serves_a_catalogue_component_that_is_read_on_demand_not_a_message_store()
    {
        ComponentCatalog.All.Should().Contain(
            c => c.ComponentKey == Source.ComponentKey && c.Cloud == ComponentCatalog.CloudOf(Source.Provider) && ComponentAccessRules.ReadsOnDemand(c),
            "a source is registered for a run-history or failed-records row, and a message-store row keeps its messaging adapter");
        ComponentCatalog.All.Where(c => c.ComponentKey == Source.ComponentKey).Should().OnlyContain(c => c.Provider == Source.Provider,
            "the catalogue row names the adapter the source reads with");
    }

    [Fact]
    public async Task It_lists_places_and_failures_and_reads_one_item()
    {
        ArrangeFailures(3);

        var places = await Source.ListPlacesAsync(Connection, default);
        var failed = await Source.ListFailedAsync(Connection, PlaceId, default);
        var detail = await Source.GetFailedAsync(Connection, ItemId, default);

        places.IsSuccess.Should().BeTrue();
        places.Value.Items.Should().NotBeEmpty();
        failed.IsSuccess.Should().BeTrue();
        failed.Value.Items.Should().HaveCount(3);
        failed.Value.ReachedLimit.Should().BeFalse();
        failed.Value.Incomplete.Should().BeFalse();
        failed.Value.Items.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Summary));
        detail.IsSuccess.Should().BeTrue();
        detail.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Its_declared_capabilities_are_not_exceeded()
    {
        ArrangeFailures(3);

        var failed = (await Source.ListFailedAsync(Connection, PlaceId, default)).Value;

        if (!Source.Capabilities.ReasonInList)
        {
            failed.Items.Should().OnlyContain(i => i.ReasonCode == null, "ReasonInList is false, so the list must not claim a reason it did not read");
        }

        if (!Source.Capabilities.ReadsPayload)
        {
            failed.Items.Should().OnlyContain(i => i.Copy != CopyKind.Held, "ReadsPayload is false, so no item may claim a payload ServiceHub can show (a pointer to the original is not the payload)");
        }
    }

    [Fact]
    public async Task It_only_ever_calls_read_operations()
    {
        ArrangeFailures(3);

        await Source.ValidateAccessAsync(Connection, default);
        await Source.ListPlacesAsync(Connection, default);
        await Source.ListFailedAsync(Connection, PlaceId, default);
        await Source.ListFailedAsync(Connection, null, default);
        await Source.GetFailedAsync(Connection, ItemId, default);

        CloudCalls.Should().NotBeEmpty("the source must have read something");
        CloudCalls.Where(c => !ReadVerbs.Any(v => c.StartsWith(v, StringComparison.Ordinal)))
            .Should().BeEmpty("a source reads only: a call that is not a list, describe or get would change a cloud outside the gate");
    }

    [Fact]
    public async Task A_listing_is_capped_and_says_so()
    {
        ArrangeFailures(ComponentSourceLimits.MaxFailedItems + 50);

        var failed = (await Source.ListFailedAsync(Connection, PlaceId, default)).Value;

        failed.Items.Count.Should().BeLessThanOrEqualTo(ComponentSourceLimits.MaxFailedItems);
        failed.ReachedLimit.Should().BeTrue("a listing that stopped at its cap must say there is more");
    }

    [Fact]
    public async Task Failures_are_newest_first()
    {
        ArrangeFailures(5);

        var items = (await Source.ListFailedAsync(Connection, PlaceId, default)).Value.Items;

        items.Select(i => i.AtUtc ?? DateTimeOffset.MinValue).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task A_refused_credential_becomes_a_not_enabled_reason_not_an_exception()
    {
        ArrangeAccessDenied();

        var access = await Source.ValidateAccessAsync(Connection, default);
        var places = await Source.ListPlacesAsync(Connection, default);

        access.IsFailure.Should().BeTrue();
        access.Error.Message.Should().StartWith("Not enabled", "the screen shows the reason; silence reads as 'nothing is wrong'");
        places.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_cloud_failure_is_a_failed_result_with_the_secret_removed()
    {
        ArrangeCloudFailure($"the call failed ({Secret})");

        var results = new[]
        {
            (await Source.ValidateAccessAsync(Connection, default)).Error.Message,
            (await Source.ListPlacesAsync(Connection, default)).Error.Message,
            (await Source.ListFailedAsync(Connection, PlaceId, default)).Error.Message,
            (await Source.GetFailedAsync(Connection, ItemId, default)).Error.Message,
        };

        results.Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m), "each read must fail as a result, not an exception");
        results.Should().OnlyContain(m => !m.Contains("hunter2", StringComparison.Ordinal), "a secret never appears in an error");
    }

    [Fact]
    public async Task Failure_text_a_workload_wrote_is_redacted()
    {
        ArrangeFailures(1);
        ArrangeSecretInFailureText(Secret);

        var detail = (await Source.GetFailedAsync(Connection, ItemId, default)).Value;
        var list = (await Source.ListFailedAsync(Connection, PlaceId, default)).Value;

        var shown = new[] { detail?.Reason, detail?.Cause, detail?.Item.Summary }.Concat(list.Items.Select(i => i.Summary));
        shown.Where(t => t is not null).Should().OnlyContain(t => !t!.Contains("hunter2", StringComparison.Ordinal), "free text is redacted before it leaves the source");
    }

    [Fact]
    public async Task An_item_the_cloud_no_longer_has_is_a_success_with_nothing()
    {
        ArrangeMissingItem();

        var detail = await Source.GetFailedAsync(Connection, ItemId, default);

        detail.IsSuccess.Should().BeTrue();
        detail.Value.Should().BeNull();
    }

    [Fact]
    public async Task A_cancelled_caller_gets_a_cancellation_not_a_failed_result()
    {
        ArrangeFailures(3);
        ArrangeCancellationAware();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await ((Func<Task>)(() => Source.ListFailedAsync(Connection, PlaceId, cancelled.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => Source.ListPlacesAsync(Connection, cancelled.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_place_or_item_that_is_not_this_components_is_refused_before_any_cloud_call()
    {
        var before = CloudCalls.Count;

        var place = await Source.ListFailedAsync(Connection, "not-a-place", default);
        var item = await Source.GetFailedAsync(Connection, "not-an-item", default);

        place.IsFailure.Should().BeTrue();
        item.IsFailure.Should().BeTrue();
        CloudCalls.Count.Should().Be(before, "an id the caller made up is never passed to the cloud");
    }
}
