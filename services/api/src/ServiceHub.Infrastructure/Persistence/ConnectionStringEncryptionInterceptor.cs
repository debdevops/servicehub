using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Makes "a connection string is never readable in the database" true by construction rather than
/// by every caller remembering to encrypt (unit 1.2).
/// </summary>
/// <remarks>
/// <para>
/// Any <see cref="Namespace"/> being added, or whose connection string changed, is passed through
/// <see cref="IConnectionStringProtector.Protect"/> just before the write. <c>Protect</c> is
/// idempotent — a value that is already an <c>ENC[…]</c> envelope is returned unchanged — so a
/// caller that encrypted early costs nothing, and one that forgot cannot leak.
/// </para>
/// <para>
/// <b>Fail closed.</b> If protecting fails, the save throws. The alternative is persisting the
/// plaintext, which is exactly the leak this exists to prevent. Reads are the opposite: nothing is
/// decrypted here, so a loaded entity carries the envelope and plaintext exists only where a
/// provider client is constructed, via <see cref="IConnectionStringProtector.Unprotect"/>.
/// </para>
/// </remarks>
public sealed class ConnectionStringEncryptionInterceptor : SaveChangesInterceptor
{
    private readonly IConnectionStringProtector _protector;

    /// <summary>Creates the interceptor over the protector that owns the key registry.</summary>
    public ConnectionStringEncryptionInterceptor(IConnectionStringProtector protector)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ProtectPending(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ProtectPending(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ProtectPending(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<Namespace>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var property = entry.Property(e => e.ConnectionString);
            if (entry.State == EntityState.Modified && !property.IsModified)
            {
                continue;
            }

            if (string.IsNullOrEmpty(property.CurrentValue))
            {
                continue;
            }

            var protectedValue = _protector.Protect(property.CurrentValue);
            if (protectedValue.IsFailure)
            {
                // The message names the code, never the value.
                throw new InvalidOperationException(
                    $"Refusing to save namespace '{entry.Entity.Name}': its connection string could not be protected ({protectedValue.Error.Code}).");
            }

            property.CurrentValue = protectedValue.Value;
        }
    }
}
