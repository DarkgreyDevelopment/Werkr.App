namespace Werkr.Common.Models;

/// <summary>
/// Generic paginated result container, reusable across all paginated endpoints.
/// </summary>
/// <typeparam name="T">The type of items in the result set.</typeparam>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Limit,
    int Offset
);
