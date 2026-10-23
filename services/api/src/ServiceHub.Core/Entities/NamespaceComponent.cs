namespace ServiceHub.Core.Entities;

/// <summary>
/// One ticked (or unticked) component on one connection (Add a component, plan §7a; the one migration the owner signed off under
/// ADR-0017 on 2026-10-23). The component is a catalogue key, never a provider name: which adapter reads it is the catalogue's business.
/// <para>
/// Deliberately small and mutable, like <see cref="AutoReplayRule.Enabled"/>: it is a current choice, not a claim about what happened.
/// Unticking sets <see cref="Enabled"/> to false and deletes nothing, so the history the component produced stays (the ledger is whole).
/// Every change also writes an <see cref="AuditLog"/> row. There is no replay column: the components that can be ticked here are read-only.
/// </para>
/// </summary>
public sealed class NamespaceComponent
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner, as on every namespace-scoped row.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The connection this choice is for. Soft reference, no foreign key (same convention as the ledger-adjacent tables).</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>The catalogue <c>ComponentKey</c>, such as <c>step-functions</c>.</summary>
    public required string ComponentKey { get; init; }

    /// <summary>Whether ServiceHub reads this component on this connection.</summary>
    public bool Enabled { get; set; }

    /// <summary>When the choice last changed.</summary>
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>The resolved identity that last changed it, never caller-supplied.</summary>
    public required string ChangedBy { get; set; }
}
