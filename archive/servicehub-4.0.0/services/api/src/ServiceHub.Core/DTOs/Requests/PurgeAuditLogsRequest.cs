using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request body for <c>POST /api/v1/audit/purge</c>.</summary>
/// <param name="OlderThanDays">
/// Permanently deletes every audit log entry (across all owners — retention is an
/// instance-wide policy) timestamped more than this many days ago.
/// </param>
public sealed record PurgeAuditLogsRequest(
    [Range(1, int.MaxValue, ErrorMessage = "OlderThanDays must be at least 1")]
    int OlderThanDays);
