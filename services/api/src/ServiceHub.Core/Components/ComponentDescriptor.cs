using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Components;

/// <summary>
/// One row of the coverage table: one component, one place work gets stuck, and what ServiceHub can do about it.
/// A component with several places (Service Bus: dead-letter, transfer dead-letter, stuck) has several rows that share a
/// <see cref="ComponentKey"/>.
/// </summary>
/// <param name="Id">Stable id, <c>component.place</c>, kebab-case. Never renamed once shipped.</param>
/// <param name="ComponentKey">The component the row belongs to, shared by its rows. Used by the sidebar and the routes.</param>
/// <param name="Cloud">Which cloud's section it is listed under.</param>
/// <param name="Name">The component as people call it.</param>
/// <param name="StuckPlace">Where the failed or stuck work ends up, in plain words.</param>
/// <param name="Detection">How ServiceHub finds it.</param>
/// <param name="MovesOnBy">How it moves on, in one plain phrase.</param>
/// <param name="Facts">Copy, path, proof.</param>
/// <param name="Status">Supported only after a recorded live run.</param>
/// <param name="Note">One line: a limit, or the reason it is not supported and where to look instead.</param>
/// <param name="Provider">The adapter that reads it, when one exists. Null means no adapter is registered for it.</param>
/// <param name="EntityKinds">Which of the adapter's entity kinds (<c>queue</c>, <c>topic</c>, <c>subscription</c>) belong to this component.</param>
/// <param name="InSidebar">Whether the component is listed in the menu. Only Supported or BeingValidated rows may be.</param>
/// <param name="PlanUnit">The build-plan unit that delivers or validates the row, when it is not already supported.</param>
/// <param name="EvidenceRef">Repo-relative path of the recorded evidence a Supported row rests on.</param>
public sealed record ComponentDescriptor(
    string Id,
    string ComponentKey,
    CloudKind Cloud,
    string Name,
    string StuckPlace,
    DetectionKind Detection,
    string MovesOnBy,
    RecoverabilityFacts Facts,
    SupportStatus Status,
    string? Note = null,
    CloudProviderType? Provider = null,
    IReadOnlyList<string>? EntityKinds = null,
    bool InSidebar = false,
    string? PlanUnit = null,
    string? EvidenceRef = null)
{
    /// <summary>Derived, never set: a row that is not Supported is always <see cref="RecoverabilityClass.R0Unknown"/> or hand-off only.</summary>
    public RecoverabilityClass Class => Status switch
    {
        SupportStatus.Supported => Facts.Classify(validated: true),
        SupportStatus.BeingValidated => RecoverabilityClass.R0Unknown,
        _ => Facts.HasSignal ? RecoverabilityClass.R5DetectOnly : RecoverabilityClass.R6NotRecoverable,
    };
}
