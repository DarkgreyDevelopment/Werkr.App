namespace Werkr.Data.Calendar.Enums;

/// <summary>Determines how schedule occurrences are handled when they fall on a non-business day.</summary>
public enum ShiftMode {
    /// <summary>Suppress (drop) the occurrence entirely.</summary>
    None = 0,
    /// <summary>Shift to the next non-holiday weekday.</summary>
    NextBusinessDay = 1,
    /// <summary>Shift to the previous non-holiday weekday.</summary>
    PreviousBusinessDay = 2,
    /// <summary>Shift to the nearest non-holiday weekday; tiebreaker from holiday rule.</summary>
    NearestBusinessDay = 3,
}
