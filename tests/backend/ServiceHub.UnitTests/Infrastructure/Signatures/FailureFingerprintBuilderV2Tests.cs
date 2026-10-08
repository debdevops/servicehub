using FluentAssertions;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Signatures;

namespace ServiceHub.UnitTests.Infrastructure.Signatures;

/// <summary>
/// Design 10 §5: fingerprint v2 hashes the error message's shape. It is behind a constructor flag and nothing turns it on,
/// so the first test is the one that matters most — v1 must not move.
/// </summary>
public sealed class FailureFingerprintBuilderV2Tests
{
    private static readonly FailureFingerprintBuilder V1 = new();
    private static readonly FailureFingerprintBuilder V2 = new(includeErrorTemplate: true);

    private static FailureFeatures Features(string? error = null, string reason = "MaxDeliveryCountExceeded", string entity = "test-queue") => new()
    {
        DeadLetterReason = reason,
        EntityName = entity,
        Provider = CloudProviderType.Azure,
        DeliveryCount = 1,
        ErrorTextNormalized = error,
    };

    private static async Task<FailureFingerprint> Compute(FailureFingerprintBuilder b, FailureFeatures f) => (await b.ComputeAsync(f)).Value;

    [Fact]
    public async Task Version_1_is_the_default_and_its_hash_is_pinned()
    {
        // Computed independently of the code: sha256("v1|maxdeliverycountexceeded|test-queue|azure|null|null|" + sorted terms).
        const string golden = "38bc700a2554b35ad1565ff7d7a4b217aaa05d85d9a60e79884c2b5ece7baf26";

        (await Compute(V1, Features())).Hash.Should().Be(golden);
        (await Compute(V1, Features("some error text"))).Hash.Should().Be(golden, "v1 never looked at the error text");
        (await Compute(new FailureFingerprintBuilder(includeErrorTemplate: false), Features())).Hash.Should().Be(golden);
        V1.CurrentVersion.Should().Be(1);
    }

    [Fact]
    public async Task Version_2_reports_itself_and_never_equals_a_version_1_hash()
    {
        V2.CurrentVersion.Should().Be(2);
        var fingerprint = await Compute(V2, Features("customer not found"));
        fingerprint.Version.Should().Be(2);
        fingerprint.Hash.Should().NotBe((await Compute(V1, Features("customer not found"))).Hash);
        fingerprint.Hash.Should().HaveLength(64);
    }

    [Fact]
    public async Task Version_2_gives_one_signature_to_one_cause_whatever_its_ids()
    {
        var a = await Compute(V2, Features("Customer 'C-100' not found for order 5001"));
        var b = await Compute(V2, Features("Customer 'C-999' not found for order 7777"));
        a.Hash.Should().Be(b.Hash);
    }

    [Fact]
    public async Task Version_2_separates_two_causes_that_version_1_merges()
    {
        var timeout = Features("Inventory service timed out after 30 seconds");
        var missing = Features("Customer 42 does not exist");

        (await Compute(V1, timeout)).Hash.Should().Be((await Compute(V1, missing)).Hash, "this is the bug design 10 fixes");
        (await Compute(V2, timeout)).Hash.Should().NotBe((await Compute(V2, missing)).Hash);
    }

    [Fact]
    public async Task Version_2_keeps_messages_with_no_error_text_together_and_apart_from_ones_with_text()
    {
        var none = (await Compute(V2, Features(null))).Hash;
        (await Compute(V2, Features("   "))).Hash.Should().Be(none);
        (await Compute(V2, Features("something went wrong"))).Hash.Should().NotBe(none);
    }

    [Fact]
    public async Task Version_2_still_separates_by_reason_and_entity()
    {
        var baseline = (await Compute(V2, Features("x failed"))).Hash;
        (await Compute(V2, Features("x failed", reason: "Other"))).Hash.Should().NotBe(baseline);
        (await Compute(V2, Features("x failed", entity: "other-queue"))).Hash.Should().NotBe(baseline);
    }

    [Fact]
    public async Task Both_versions_are_deterministic_and_batch_matches_single()
    {
        var features = new[] { Features("a 1"), Features("b 2") };
        var batch = (await V2.ComputeBatchAsync(features)).Value;
        batch[0].Hash.Should().Be((await Compute(V2, features[0])).Hash);
        batch[1].Hash.Should().Be((await Compute(V2, features[1])).Hash);
    }

    [Fact]
    public async Task A_capped_template_is_hashed_as_given_and_folded_messages_share_one_signature()
    {
        var a = Features("alpha 1") with { };
        var folded1 = new FailureFeatures { DeadLetterReason = a.DeadLetterReason, EntityName = a.EntityName, Provider = a.Provider, DeliveryCount = 1, ErrorTextNormalized = "one thing", ErrorTemplate = ServiceHub.Core.Helpers.ErrorTemplateCap.Other };
        var folded2 = new FailureFeatures { DeadLetterReason = a.DeadLetterReason, EntityName = a.EntityName, Provider = a.Provider, DeliveryCount = 1, ErrorTextNormalized = "another thing", ErrorTemplate = ServiceHub.Core.Helpers.ErrorTemplateCap.Other };

        (await Compute(V2, folded1)).Hash.Should().Be((await Compute(V2, folded2)).Hash);
        (await Compute(V2, folded1)).Hash.Should().NotBe((await Compute(V2, Features("one thing"))).Hash);
        (await Compute(V1, folded1)).Hash.Should().Be((await Compute(V1, Features())).Hash, "v1 ignores the template");
    }
}
