using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.LiveTail;

/// <inheritdoc cref="ILiveTailSessionFactory" />
public sealed class LiveTailSessionFactory : ILiveTailSessionFactory
{
    private readonly IMessageOperationsService _messageOperationsService;

    public LiveTailSessionFactory(IMessageOperationsService messageOperationsService)
    {
        _messageOperationsService = messageOperationsService ?? throw new ArgumentNullException(nameof(messageOperationsService));
    }

    /// <inheritdoc />
    public ILiveTailSession Create(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        bool fromDeadLetter) =>
        new LiveTailSession(_messageOperationsService, namespaceId, entityName, subscriptionName, fromDeadLetter);
}
