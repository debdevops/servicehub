using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Mock;

/// <summary>
/// In-memory <see cref="IMessageSender"/> used when the mock provider is active.
/// Stores sent messages in <see cref="MockMessageStore"/> without contacting any cloud service.
/// </summary>
internal sealed class MockMessageSender : IMessageSender
{
    private readonly MockMessageStore _store;

    public MockMessageSender(MockMessageStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<Result> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        _store.AddSentMessage(request);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SendBatchAsync(
        IEnumerable<SendMessageRequest> requests,
        CancellationToken cancellationToken = default)
    {
        foreach (var request in requests)
            _store.AddSentMessage(request);

        return Task.FromResult(Result.Success());
    }
}
