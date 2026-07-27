using Microsoft.Extensions.Logging;
using ServiceHub.Infrastructure.Persistence.InMemory;

namespace ServiceHub.Simulator;

/// <summary>
/// Namespace repository used only in Simulator mode. State lives purely in the
/// in-process dictionary inherited from <see cref="InMemoryNamespaceRepositoryBase"/> —
/// it never reads or writes the real repository's on-disk file, so simulator
/// namespaces are discarded when the process exits and never leak into a real
/// ServiceHub run.
/// </summary>
public sealed class EphemeralNamespaceRepository(ILogger<EphemeralNamespaceRepository> logger)
    : InMemoryNamespaceRepositoryBase(logger);
