namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Generic paginated response wrapper.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">Page items.</param>
/// <param name="TotalCount">Total items count before paging.</param>
/// <param name="Page">Current page number (1-based).</param>
/// <param name="PageSize">Page size.</param>
/// <param name="HasNextPage">Whether there is a next page.</param>
/// <param name="HasPreviousPage">Whether there is a previous page.</param>
public sealed record PaginatedResponse<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasNextPage,
    bool HasPreviousPage);
