namespace Werkr.Data.Calendar.Enums;

/// <summary>Determines how a holiday calendar filters schedule occurrences.</summary>
public enum HolidayCalendarMode {

    /// <summary>Occurrences falling on holiday dates are suppressed.</summary>
    Blocklist,

    /// <summary>Only occurrences falling on holiday dates are kept.</summary>
    Allowlist,
}
