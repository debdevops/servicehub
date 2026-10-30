using System.Text.RegularExpressions;
using Grpc.Core;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

namespace ServiceHub.Providers.Gcp.Components;

/// <summary>
/// Google Cloud Scheduler jobs whose <b>last attempt</b> failed (the first detect-only reader). <b>Read-only</b>: it lists locations and
/// jobs and gets one job, and never maps a job's target, so a URL, header or body is never read.
/// </summary>
/// <remarks>
/// The fact is a state Google returns, not a metric and not a stored history: only the last attempt is known. A job that failed last night
/// and succeeded this morning shows nothing. A paused job is never counted as a failure, because its last attempt is stale. A place is a location that holds jobs.
/// Needs <c>cloudscheduler.jobs.list</c>, <c>cloudscheduler.jobs.get</c> and <c>cloudscheduler.locations.list</c> (Cloud Scheduler Viewer).
/// </remarks>
public sealed partial class CloudSchedulerSource : IComponentSource
{
    /// <summary>The catalogue component this serves.</summary>
    public const string Key = "cloud-scheduler";

    private const int MaxTextLength = 1000;
    private const string Paused = "PAUSED";

    private readonly IGcpSchedulerReader _reader;

    // The reader is this module's own seam to Google, so a test can stand in for it; only the public constructor is for the host.
    internal CloudSchedulerSource(IGcpSchedulerReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>Creates the source over Google.</summary>
    public CloudSchedulerSource(IGcpClientFactory clients)
        : this(new GcpSchedulerReader(clients))
    {
    }

    /// <inheritdoc />
    public string ComponentKey => Key;

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Gcp;

    /// <inheritdoc />
    public ComponentSourceCapabilities Capabilities { get; } = new(ReasonInList: true, ReadsPayload: false);

    /// <inheritdoc />
    public async Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        if (ProjectOf(ns) is null)
        {
            return Result.Failure(NoProject());
        }

        try
        {
            await _reader.ListLocationsAsync(ns, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A cloud failure becomes a result the screen can show, never an exception.
        catch (Exception ex)
        {
            return Result.Failure(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<SourcePlacesPage>> ListPlacesAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        if (ProjectOf(ns) is null)
        {
            return Result.Failure<SourcePlacesPage>(NoProject());
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            var found = await FindJobsAsync(ns, 500, budget.Token, ct).ConfigureAwait(false);
            if (found.Error is not null)
            {
                return Result.Failure<SourcePlacesPage>(found.Error);
            }

            // A place is a location that holds jobs; a failed job is an item in it. (A job holds one last attempt, so it is not a place.)
            var locations = found.Items.Select(j => LocationOf(j.Name)).Where(l => l is not null).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            return Result.Success(new SourcePlacesPage(
                [.. locations.Take(ComponentSourceLimits.MaxPlaces).Select(l => new SourcePlace(l!, ShortName(l!), "location", PlaceLink(l!)))],
                found.More || locations.Count > ComponentSourceLimits.MaxPlaces, found.Incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<SourcePlacesPage>(Error.ExternalService("GCP.Scheduler.ReadTimedOut", "Reading Cloud Scheduler took too long."));
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure<SourcePlacesPage>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemsPage>> ListFailedAsync(Namespace ns, string? placeId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        if (ProjectOf(ns) is null)
        {
            return Result.Failure<FailedItemsPage>(NoProject());
        }

        var place = placeId is null ? null : InOwnLocation(ns, placeId);
        if (placeId is not null && place is null)
        {
            return Result.Failure<FailedItemsPage>(Error.Validation("GCP.Scheduler.InvalidPlace", "That is not a Cloud Scheduler location in this connection's project."));
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            // The list already carries each job's last-attempt status, so one place is just the same read filtered to that location.
            var found = await FindJobsAsync(ns, 500, budget.Token, ct).ConfigureAwait(false);
            if (found.Error is not null)
            {
                return Result.Failure<FailedItemsPage>(found.Error);
            }

            var failed = found.Items
                .Where(IsFailed)
                .Where(j => place is null || string.Equals(LocationOf(j.Name), place, StringComparison.Ordinal))
                .OrderByDescending(j => j.LastAttemptUtc ?? DateTimeOffset.MinValue)
                .ToList();
            return Result.Success(new FailedItemsPage(
                [.. failed.Take(ComponentSourceLimits.MaxFailedItems).Select(j => ToItem(ns, j))],
                found.More || failed.Count > ComponentSourceLimits.MaxFailedItems, found.Incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<FailedItemsPage>(Error.ExternalService("GCP.Scheduler.ReadTimedOut", "Reading Cloud Scheduler took too long."));
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure<FailedItemsPage>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemDetail?>> GetFailedAsync(Namespace ns, string itemId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        if (ProjectOf(ns) is null)
        {
            return Result.Failure<FailedItemDetail?>(NoProject());
        }

        var own = InOwnProject(ns, itemId);
        if (own is null)
        {
            return Result.Failure<FailedItemDetail?>(Error.Validation("GCP.Scheduler.InvalidItem", "That is not a Cloud Scheduler job in this connection's project."));
        }

        try
        {
            var job = await _reader.GetJobAsync(ns, own, ct).ConfigureAwait(false);
            if (job is null || !IsFailed(job))
            {
                // Gone, or its last attempt has succeeded since: nothing is failed any more.
                return Result.Success<FailedItemDetail?>(null);
            }

            return Result.Success<FailedItemDetail?>(new FailedItemDetail(ToItem(ns, job), CodeWords(job.StatusCode!.Value), Clean(job.StatusMessage), job.LastAttemptUtc, job.LastAttemptUtc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            return Result.Failure<FailedItemDetail?>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    private sealed record Found(IReadOnlyList<SchedulerJobRef> Items, bool More, bool Incomplete, Error? Error);

    /// <summary>Every job in every location, sorted by name. A location that cannot be read leaves the result incomplete.</summary>
    private async Task<Found> FindJobsAsync(Namespace ns, int max, CancellationToken budget, CancellationToken caller)
    {
        // "-" in place of a location lists every location in one call (as Workflows and Eventarc do). The per-location walk is the fallback.
#pragma warning disable CA1031
        try
        {
            var wide = await _reader.ListJobsAsync(ns, "-", max, budget).ConfigureAwait(false);
            return new Found([.. wide.Items.OrderBy(j => j.Name, StringComparer.Ordinal)], wide.More, false, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // fall through to the per-location walk
        }
#pragma warning restore CA1031

        var locations = await _reader.ListLocationsAsync(ns, budget).ConfigureAwait(false);
        var results = await SourceReads.RunAsync(
            locations.Items, (location, token) => _reader.ListJobsAsync(ns, location, max, token), Fail, new Listing<SchedulerJobRef>([], false), budget, caller).ConfigureAwait(false);

        if (results.Count > 0 && results.All(r => r.Failed))
        {
            return new Found([], false, true, results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("GCP.Scheduler.ReadFailed", "Cloud Scheduler could not be read."));
        }

        var all = results.SelectMany(r => r.Value.Items).OrderBy(j => j.Name, StringComparer.Ordinal).ToList();
        return new Found(all, locations.More || results.Any(r => r.Value.More), results.Any(r => r.Failed), null);
    }

    // Failed means: it has run, the last attempt came back with a status other than OK, and the job is not paused.
    private static bool IsFailed(SchedulerJobRef j) => j.State != Paused && j.StatusCode is > 0;

    private static FailedItem ToItem(Namespace ns, SchedulerJobRef j) =>
        new(j.Name, LocationOf(j.Name) ?? j.Name, j.LastAttemptUtc, CodeName(j.StatusCode!.Value), ReasonCode: j.StatusCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Last attempt failed: {CodeWords(j.StatusCode.Value)} ({Clean(ShortName(j.Name))})", CopyKind.None, JobLink(j.Name));

    // gRPC status codes, in Google's own names. A code with no name here is shown as its number, never guessed.
    private static string CodeName(int code) => code switch
    {
        1 => "CANCELLED", 2 => "UNKNOWN", 3 => "INVALID_ARGUMENT", 4 => "DEADLINE_EXCEEDED", 5 => "NOT_FOUND", 6 => "ALREADY_EXISTS",
        7 => "PERMISSION_DENIED", 8 => "RESOURCE_EXHAUSTED", 9 => "FAILED_PRECONDITION", 10 => "ABORTED", 11 => "OUT_OF_RANGE",
        12 => "UNIMPLEMENTED", 13 => "INTERNAL", 14 => "UNAVAILABLE", 15 => "DATA_LOSS", 16 => "UNAUTHENTICATED",
        _ => $"CODE_{code}",
    };

    private static string CodeWords(int code) => code switch
    {
        1 => "Cancelled", 2 => "Unknown error", 3 => "Invalid argument", 4 => "Deadline exceeded", 5 => "Not found", 6 => "Already exists",
        7 => "Permission denied", 8 => "Resource exhausted", 9 => "Failed precondition", 10 => "Aborted", 11 => "Out of range",
        12 => "Unimplemented", 13 => "Internal error", 14 => "Unavailable", 15 => "Data loss", 16 => "Unauthenticated",
        _ => $"Status code {code}",
    };

    private static string? ProjectOf(Namespace ns) => string.IsNullOrWhiteSpace(ns.GcpProjectId) ? null : ns.GcpProjectId.Trim();

    private static Error NoProject() =>
        Error.Validation("GCP.Scheduler.NoProject", "This connection has no Google project id, so Cloud Scheduler cannot be read.");

    // projects/{project}/locations/{location}/jobs/{job}, in this connection's own project only.
    [GeneratedRegex(@"^projects/(?<p>[^/]+)/locations/(?<l>[^/]+)/jobs/(?<j>[^/]+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NameRegex();

    // A name is accepted only when its project segment is this connection's own id, so a read can only happen in this connection's project.
    private static string? InOwnProject(Namespace ns, string name)
    {
        var m = name.Length <= 1024 ? NameRegex().Match(name) : Match.Empty;
        var project = ProjectOf(ns);
        return m.Success && project is not null && string.Equals(m.Groups["p"].Value, project, StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    // projects/{project}/locations/{location}, in this connection's own project only.
    [GeneratedRegex(@"^projects/(?<p>[^/]+)/locations/(?<l>[^/]+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LocationRegex();

    private static string? InOwnLocation(Namespace ns, string name)
    {
        var m = name.Length <= 1024 ? LocationRegex().Match(name) : Match.Empty;
        var project = ProjectOf(ns);
        return m.Success && project is not null && string.Equals(m.Groups["p"].Value, project, StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    private static string? LocationOf(string jobName)
    {
        var m = NameRegex().Match(jobName);
        return m.Success ? $"projects/{m.Groups["p"].Value}/locations/{m.Groups["l"].Value}" : null;
    }

    private static string PlaceLink(string location) =>
        $"https://console.cloud.google.com/cloudscheduler?project={LocationRegex().Match(location).Groups["p"].Value}";

    private static string ShortName(string name) => name[(name.LastIndexOf('/') + 1)..];

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var safe = LogRedactor.SanitiseForLog(LogRedactor.Redact(text));
        return safe.Length <= MaxTextLength ? safe : safe[..MaxTextLength] + "…";
    }

    private static string JobLink(string jobName)
    {
        var m = NameRegex().Match(jobName);
        return m.Success
            ? $"https://console.cloud.google.com/cloudscheduler/jobs/edit/{m.Groups["l"].Value}/{m.Groups["j"].Value}?project={m.Groups["p"].Value}"
            : "https://console.cloud.google.com/cloudscheduler";
    }

    private static Error Fail(Exception ex) => ex is RpcException { StatusCode: StatusCode.PermissionDenied or StatusCode.Unauthenticated }
        ? Error.Forbidden("GCP.Scheduler.NotEnabled",
            "Not enabled for this connection: its service account may not read Cloud Scheduler (needs cloudscheduler.jobs.list, cloudscheduler.jobs.get and " +
            "cloudscheduler.locations.list, the Cloud Scheduler Viewer role, and the Cloud Scheduler API turned on).")
        : Error.ExternalService("GCP.Scheduler.ReadFailed", LogRedactor.SanitiseForLog(LogRedactor.Redact(ex.Message)));
}
