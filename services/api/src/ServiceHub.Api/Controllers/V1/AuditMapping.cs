using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>Maps identity and audit facts to what the UI receives.</summary>
internal static class AuditMapping
{
    public static ActorResponse ToResponse(RecoveryActor actor) => new(
        actor.Identity,
        Kind(actor.Kind),
        RecoveryActorLabel.For(actor.Identity),
        RecoveryActorLabel.IsSession(actor.Identity));

    public static AuditEntryResponse ToResponse(AuditLog entry) => new(
        entry.Id,
        entry.Timestamp,
        new ActorResponse(
            entry.UserIdentity,
            entry.UserIdentity.StartsWith("ApiKey:", StringComparison.Ordinal) ? "apiKey" : "user",
            RecoveryActorLabel.For(entry.UserIdentity),
            RecoveryActorLabel.IsSession(entry.UserIdentity)),
        entry.Action,
        entry.Outcome,
        entry.NamespaceId,
        entry.NamespaceName,
        entry.CloudProvider,
        entry.Environment,
        entry.ResourceName,
        entry.ErrorDetails,
        entry.CorrelationId);

    private static string Kind(RecoveryActorKind kind) => kind switch
    {
        RecoveryActorKind.User => "user",
        RecoveryActorKind.ApiKey => "apiKey",
        RecoveryActorKind.Automation => "automation",
        _ => "system",
    };
}
