using System.Globalization;
using Werkr.Common.Models;

namespace Werkr.Server.Helpers;

/// <summary>Shared filter-criteria parsing utilities for list pages.</summary>
public static class FilterHelper {
    /// <summary>Parses a "yyyy-MM-dd" date string from filter criteria.</summary>
    public static DateTime? TryParseFilterDate( string? dateText ) =>
        DateTime.TryParseExact( dateText, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed )
            ? parsed : null;

    /// <summary>
    /// Client-side predicate for WorkflowRunDto filtering by status + date range.
    /// Used by Runs.razor and AllRuns.razor.
    /// </summary>
    public static bool MatchesRunFilter( WorkflowRunDto run, FilterCriteria? criteria ) {
        if (criteria is null) return true;

        string? status = criteria.Get( "status" );
        if (!string.IsNullOrWhiteSpace( status )
             && !run.Status.Equals( status, StringComparison.OrdinalIgnoreCase ))
            return false;

        DateTime? since = TryParseFilterDate( criteria.Get( "since" ) );
        if (since.HasValue && run.StartTime < since.Value) return false;

        DateTime? until = TryParseFilterDate( criteria.Get( "until" ) );
        if (until.HasValue && run.StartTime >= until.Value.AddDays( 1 )) return false;

        return true;
    }
}
