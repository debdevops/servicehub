using System.Text.RegularExpressions;
using FluentAssertions;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Main plan rule 11, build unit G0: the safety kernel is fixed. Every action that changes a cloud's messages is reached
/// through <c>DlqReplayService</c>, which asks <c>RecoveryEligibilityGate</c> before it calls the cloud and records the
/// attempt in the ledger. A new component can add an adapter; it cannot add a path around the gate.
///
/// What the emergency stop covers is NOT widened here: it halts Automation and System actors only and never a person's own
/// recovery (a documented design in <c>RecoveryEligibilityGate</c>, predicate 0). This test keeps the set of doors small;
/// it does not change which door the stop guards.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class KernelGuardTests
{
    private const string Src = "services/api/src";

    // Calls that change messages in a cloud. Provider projects implement them and are excluded from the scan.
    private static readonly Regex Mutating = new(
        @"\.(ReplayMessageAsync|PurgeMessageAsync|DeadLetterMessagesAsync|SendBatchAsync|SendReturningProviderIdAsync|RedriveAsync)\(|GetMessageSender\(\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The only files allowed to call a mutating provider operation, each with the reason.</summary>
    private static readonly Dictionary<string, string> Doors = new(StringComparer.Ordinal)
    {
        ["DlqReplayService.cs"] = "the gated door: asks the gate, opens a ledger operation, then replays or purges",
        ["MessageOperationsService.cs"] = "the wrapper DlqReplayService calls; nothing else may reach it (checked below)",
        ["ICloudMessagingProvider.cs"] = "only declares the GetMessageSender() accessor; it calls nothing",
        ["ComponentRedriveService.cs"] = "the gated redrive door (PP5): asks the gate, opens a ledger operation, then asks the cloud to redrive; a person only",
        ["MessagesController.cs"] = "manual 'Send a test message': operator-only, refused on Production, intent header, audited; not a recovery",
    };

    private static IEnumerable<FileInfo> NonProviderSources() =>
        new[] { "ServiceHub.Infrastructure", "ServiceHub.Api", "ServiceHub.Core" }
            .SelectMany(p => RepositoryRoot.CSharpFilesUnder($"{Src}/{p}"));

    [Fact]
    public void Only_the_known_doors_call_a_mutating_provider_operation()
    {
        var offenders = NonProviderSources()
            .Where(f => File.ReadLines(f.FullName).Any(l =>
                !l.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
                !l.TrimStart().StartsWith("///", StringComparison.Ordinal) && Mutating.IsMatch(l)))
            .Select(f => f.Name)
            .Where(n => !Doors.ContainsKey(n))
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty(
            "a cloud-changing call outside these doors skips the gate and the ledger. Route it through DlqReplayService, " +
            "or add a door here with its reason and a test of its own protections (rule 11)");
    }

    [Fact]
    public void Nothing_but_the_gated_service_calls_the_message_operations_that_replay_or_purge()
    {
        var users = NonProviderSources()
            .Where(f => f.Name != "MessageOperationsService.cs" && f.Name != "DlqReplayService.cs")
            .Where(f => Regex.IsMatch(File.ReadAllText(f.FullName), @"\bIMessageOperationsService\b"))
            .Select(f => f.Name)
            // The interface itself and the registration or an entity comment are not callers.
            .Where(n => n != "IMessageOperationsService.cs" && n != "RoutingServiceCollectionExtensions.cs" && n != "DlqObserverAttestation.cs")
            .ToList();

        users.Should().BeEmpty("IMessageOperationsService can replay and purge; only DlqReplayService may hold it");
    }

    [Theory]
    [InlineData("public async Task<Result<ReplayOutcome>> ReplayAsync(", "_operations.ReplayMessageAsync(")]
    [InlineData("public async Task<Result<ReplayOutcome>> PurgeAsync(", "_operations.PurgeMessageAsync(")]
    public void The_gate_is_asked_before_the_cloud_is_called(string methodStart, string cloudCall)
    {
        var file = RepositoryRoot.CSharpFilesUnder($"{Src}/ServiceHub.Infrastructure").Single(f => f.Name == "DlqReplayService.cs");
        var text = File.ReadAllText(file.FullName);

        var start = text.IndexOf(methodStart, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"{methodStart} must still exist");
        var next = text.IndexOf("\n    public ", start + methodStart.Length, StringComparison.Ordinal);
        var body = next < 0 ? text[start..] : text[start..next];

        var gate = body.IndexOf("EvaluateAsync(", StringComparison.Ordinal);
        var ledger = body.IndexOf("_ledger.OpenOperationAsync(", StringComparison.Ordinal);
        var cloud = body.IndexOf(cloudCall, StringComparison.Ordinal);

        gate.Should().BeGreaterThan(-1, "the method must ask the gate");
        cloud.Should().BeGreaterThan(-1, "the method must make the cloud call this test watches");
        gate.Should().BeLessThan(cloud, "the gate is asked before the cloud call");
        ledger.Should().BeGreaterThan(gate, "the attempt is opened in the ledger after the gate allows it");
        ledger.Should().BeLessThan(cloud, "the attempt is on the record before the cloud call");
    }

    [Fact]
    public void Nothing_but_the_gated_redrive_service_holds_the_component_mover()
    {
        var users = NonProviderSources()
            .Where(f => f.Name != "ComponentRedriveService.cs")
            .Where(f => Regex.IsMatch(File.ReadAllText(f.FullName), @"\bIComponentMoveOn\b"))
            .Select(f => f.Name)
            // The interface itself is not a caller.
            .Where(n => n != "ComponentMoveOn.cs")
            .ToList();

        users.Should().BeEmpty("IComponentMoveOn asks a cloud to move a run on; only ComponentRedriveService may hold it");
    }

    [Fact]
    public void The_redrive_asks_the_gate_and_is_on_the_ledger_before_the_cloud_is_called()
    {
        var file = RepositoryRoot.CSharpFilesUnder($"{Src}/ServiceHub.Infrastructure").Single(f => f.Name == "ComponentRedriveService.cs");
        var text = File.ReadAllText(file.FullName);

        const string methodStart = "public async Task<Result<RedriveOutcome>> RequestRedriveAsync(";
        var start = text.IndexOf(methodStart, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "RequestRedriveAsync must still exist");
        var next = text.IndexOf("\n    private ", start + methodStart.Length, StringComparison.Ordinal);
        var body = next < 0 ? text[start..] : text[start..next];

        var gate = body.IndexOf("EvaluateAsync(", StringComparison.Ordinal);
        var ledger = body.IndexOf("_ledger.OpenOperationAsync(", StringComparison.Ordinal);
        var entry = body.IndexOf("_ledger.BeginEntryAsync(", StringComparison.Ordinal);
        var cloud = body.IndexOf("mover.RedriveAsync(", StringComparison.Ordinal);

        gate.Should().BeGreaterThan(-1, "the method must ask the gate");
        cloud.Should().BeGreaterThan(-1, "the method must make the cloud call this test watches");
        gate.Should().BeLessThan(ledger, "the attempt is opened in the ledger only after the gate allows it");
        ledger.Should().BeLessThan(entry);
        entry.Should().BeLessThan(cloud, "the attempt is on the record before the cloud call");
        File.ReadAllText(file.FullName).Should().Contain("_gate.EvaluateAsync(", "the private evaluate step really asks the gate");
    }

    [Fact]
    public void The_redrive_endpoint_keeps_its_protections()
    {
        var file = RepositoryRoot.CSharpFilesUnder($"{Src}/ServiceHub.Api").Single(f => f.Name == "NamespacesController.cs");
        var text = File.ReadAllText(file.FullName);
        var action = text[text.IndexOf("public async Task<IActionResult> ComponentRedrive(", StringComparison.Ordinal)..];
        action = action[..action.IndexOf("private static FailedItemResponse ToResponse(", StringComparison.Ordinal)];

        action.Should().Contain("IntentHeaders.RedriveExecution", "an explicit intent header is required");
        action.Should().Contain("GovernanceRole.Approver", "only an Approver can redrive a run");
        action.Should().Contain("_redrive.RequestRedriveAsync(", "the only route to the cloud is the gated service");
    }

    [Fact]
    public void The_private_evaluate_step_really_asks_the_gate()
    {
        var file = RepositoryRoot.CSharpFilesUnder($"{Src}/ServiceHub.Infrastructure").Single(f => f.Name == "DlqReplayService.cs");
        File.ReadAllText(file.FullName).Should().Contain("_gate.EvaluateAsync(");
    }

    [Fact]
    public void The_manual_send_door_keeps_its_own_three_protections()
    {
        // 'Send a test message' is outside the gate by design (it is not a recovery). Its protections must not erode.
        var file = RepositoryRoot.CSharpFilesUnder($"{Src}/ServiceHub.Api").Single(f => f.Name == "MessagesController.cs");
        var text = File.ReadAllText(file.FullName);
        var send = text[text.IndexOf("public async Task<IActionResult> Send(", StringComparison.Ordinal)..];
        send = send[..send.IndexOf("private async Task<IActionResult> PeekAsync(", StringComparison.Ordinal)];

        send.Should().Contain("IntentHeaders.SendMessage", "an explicit intent header is required");
        send.Should().Contain("EnvironmentType.Prod", "sending into Production is refused");
        send.Should().Contain("GovernanceRole.Operator", "a Viewer cannot send");
        send.Should().Contain("AuditActions.MessageSend", "every send is audited");
    }
}
