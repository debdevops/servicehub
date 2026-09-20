using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Agent;

/// <summary>
/// HTTP client for the ServiceHub reasoning-companion service (<c>services/agent</c>).
/// <para>
/// <see cref="IsAvailableAsync"/> calls <c>GET /health</c> and <see cref="ProposeAsync"/> calls
/// <c>POST /propose</c>; both structurally mirror <see cref="AI.AIServiceClient"/>. ServiceHub
/// makes no external LLM API calls of any kind: the reasoning-companion service is a local,
/// self-hosted container the operator controls, reached only when
/// <see cref="ReasoningAgentOptions.Enabled"/> is explicitly set — and even then, it only
/// produces proposals if its own <c>OLLAMA_HOST</c> is configured (see
/// <c>services/agent/README.md</c>).
/// </para>
/// <para>
/// Every failure path — unreachable host, timeout, malformed response, non-2xx status —
/// degrades to the same "no proposals" result. This type never throws into a caller.
/// </para>
/// </summary>
public sealed class ReasoningAgentClient : IReasoningAgentClient
{
    /// <summary>Name of the named <see cref="HttpClient"/> registered for this client.</summary>
    public const string HttpClientName = "ReasoningAgent";

    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions ResponseSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ReasoningAgentOptions _options;
    private readonly ILogger<ReasoningAgentClient> _logger;

    private int _unavailabilityLogged;

    /// <summary>Initializes a new instance of the <see cref="ReasoningAgentClient"/> class.</summary>
    public ReasoningAgentClient(
        IHttpClientFactory httpClientFactory,
        IOptions<ReasoningAgentOptions> options,
        ILogger<ReasoningAgentClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<ReasoningProposal>>> ProposeAsync(
        IReadOnlyList<ReasoningEvidenceRecord> evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ServiceUrl))
        {
            return Result.Success<IReadOnlyList<ReasoningProposal>>([]);
        }

        if (evidence.Count == 0)
        {
            return Result.Success<IReadOnlyList<ReasoningProposal>>([]);
        }

        var refToRecord = new Dictionary<string, ReasoningEvidenceRecord>(evidence.Count, StringComparer.Ordinal);
        var records = new List<EvidenceRecordDto>(evidence.Count);
        foreach (var record in evidence)
        {
            refToRecord[record.Ref] = record;
            records.Add(ToEvidenceRecordDto(record));
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(
                "propose",
                new ProposeRequestDto(records),
                ResponseSerializerOptions,
                timeoutCts.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                LogUnavailabilityOnce();
                return Result.Success<IReadOnlyList<ReasoningProposal>>([]);
            }

            if (!response.IsSuccessStatusCode)
            {
                LogUnavailabilityOnce();
                return Result.Success<IReadOnlyList<ReasoningProposal>>([]);
            }

            var proposeResponse = await response.Content
                .ReadFromJsonAsync<ProposeResponseDto>(ResponseSerializerOptions, timeoutCts.Token)
                .ConfigureAwait(false);

            if (proposeResponse?.Proposals is null)
            {
                LogUnavailabilityOnce();
                return Result.Success<IReadOnlyList<ReasoningProposal>>([]);
            }

            // Every proposal must resolve back to a record this call actually sent — a ref the
            // companion invented (or echoed from a different request) is dropped, never trusted.
            var proposals = proposeResponse.Proposals
                .Where(p => p.Ref is not null && refToRecord.ContainsKey(p.Ref))
                .Select(p => new ReasoningProposal(p.Ref!, p.Summary ?? string.Empty, p.Considerations ?? []))
                .Where(p => !string.IsNullOrWhiteSpace(p.Summary))
                .ToList();

            return Result.Success<IReadOnlyList<ReasoningProposal>>(proposals);
        }
        catch (OperationCanceledException)
        {
            // Covers both caller cancellation and the configured timeout.
            LogUnavailabilityOnce();
            return Result.Success<IReadOnlyList<ReasoningProposal>>([]);
        }
        catch (HttpRequestException)
        {
            LogUnavailabilityOnce();
            return Result.Success<IReadOnlyList<ReasoningProposal>>([]);
        }
        catch (JsonException)
        {
            LogUnavailabilityOnce();
            return Result.Success<IReadOnlyList<ReasoningProposal>>([]);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ServiceUrl))
        {
            return Result.Success(false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(HealthCheckTimeout);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync("health", timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogUnavailabilityOnce();
                return Result.Success(false);
            }

            var health = await response.Content
                .ReadFromJsonAsync<HealthResponseDto>(ResponseSerializerOptions, timeoutCts.Token)
                .ConfigureAwait(false);

            var available = health is { Ready: true, ReasoningConfigured: true };
            if (!available)
            {
                LogUnavailabilityOnce();
            }

            return Result.Success(available);
        }
        catch (OperationCanceledException)
        {
            LogUnavailabilityOnce();
            return Result.Success(false);
        }
        catch (HttpRequestException)
        {
            LogUnavailabilityOnce();
            return Result.Success(false);
        }
        catch (JsonException)
        {
            LogUnavailabilityOnce();
            return Result.Success(false);
        }
    }

    /// <summary>
    /// Logs the "reasoning companion is unavailable" warning at most once per process.
    /// </summary>
    private void LogUnavailabilityOnce()
    {
        if (Interlocked.Exchange(ref _unavailabilityLogged, 1) == 0)
        {
            _logger.LogWarning(
                "Reasoning companion service is unavailable or not configured with a reasoning " +
                "backend — ServiceHub continues operating without it until it recovers.");
        }
    }

    private static EvidenceRecordDto ToEvidenceRecordDto(ReasoningEvidenceRecord r) => new(
        Ref: r.Ref,
        SignatureHash: r.SignatureHash,
        LifecycleStatus: r.LifecycleStatus,
        Severity: r.Severity,
        Provider: r.Provider,
        DominantDeadletterReason: r.DominantDeadletterReason,
        TopTerms: r.TopTerms,
        OccurrenceCount: r.OccurrenceCount,
        BlastRadius: r.BlastRadius,
        IsRecurring: r.IsRecurring,
        PendingDecisionCount: r.PendingDecisionCount,
        RecoveryEntryCount: r.RecoveryEntryCount,
        OpenRecoveryEntryCount: r.OpenRecoveryEntryCount,
        AnomalyFlagCount: r.AnomalyFlagCount,
        DriftFindingCount: r.DriftFindingCount,
        CorrelationHypothesisCount: r.CorrelationHypothesisCount,
        PreventionTriggerCount: r.PreventionTriggerCount,
        ReplayPlanCount: r.ReplayPlanCount);

    private sealed record HealthResponseDto(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("ready")] bool Ready,
        [property: JsonPropertyName("reasoning_configured")] bool ReasoningConfigured);

    private sealed record ProposeRequestDto(
        [property: JsonPropertyName("records")] IReadOnlyList<EvidenceRecordDto> Records);

    private sealed record EvidenceRecordDto(
        [property: JsonPropertyName("ref")] string Ref,
        [property: JsonPropertyName("signature_hash")] string SignatureHash,
        [property: JsonPropertyName("lifecycle_status")] string LifecycleStatus,
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("provider")] string? Provider,
        [property: JsonPropertyName("dominant_deadletter_reason")] string? DominantDeadletterReason,
        [property: JsonPropertyName("top_terms")] IReadOnlyList<string> TopTerms,
        [property: JsonPropertyName("occurrence_count")] int OccurrenceCount,
        [property: JsonPropertyName("blast_radius")] int BlastRadius,
        [property: JsonPropertyName("is_recurring")] bool IsRecurring,
        [property: JsonPropertyName("pending_decision_count")] int PendingDecisionCount,
        [property: JsonPropertyName("recovery_entry_count")] int RecoveryEntryCount,
        [property: JsonPropertyName("open_recovery_entry_count")] int OpenRecoveryEntryCount,
        [property: JsonPropertyName("anomaly_flag_count")] int AnomalyFlagCount,
        [property: JsonPropertyName("drift_finding_count")] int DriftFindingCount,
        [property: JsonPropertyName("correlation_hypothesis_count")] int CorrelationHypothesisCount,
        [property: JsonPropertyName("prevention_trigger_count")] int PreventionTriggerCount,
        [property: JsonPropertyName("replay_plan_count")] int ReplayPlanCount);

    private sealed record ProposeResponseDto(
        [property: JsonPropertyName("proposals")] List<ProposalDto>? Proposals,
        [property: JsonPropertyName("method")] string? Method,
        [property: JsonPropertyName("model")] string? Model);

    private sealed record ProposalDto(
        [property: JsonPropertyName("ref")] string? Ref,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("considerations")] List<string>? Considerations);
}
