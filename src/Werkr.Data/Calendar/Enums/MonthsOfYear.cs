namespace Werkr.Data.Calendar.Enums;

/// <summary>
/// Months of year as flags for recurrence patterns.
/// Values are intentionally non-sequential to avoid confusion with month numbers.
/// </summary>
[Flags]
public enum MonthsOfYear {

    /// <summary>No months selected.</summary>
    None = 0,

    /// <summary>January.</summary>
    January = 16,

    /// <summary>February.</summary>
    February = 32,

    /// <summary>March.</summary>
    March = 64,

    /// <summary>April.</summary>
    April = 128,

    /// <summary>May.</summary>
    May = 256,

    /// <summary>June.</summary>
    June = 512,

    /// <summary>July.</summary>
    July = 1024,

    /// <summary>August.</summary>
    August = 2048,

    /// <summary>September.</summary>
    September = 4096,

    /// <summary>October.</summary>
    October = 8192,

    /// <summary>November.</summary>
    November = 16384,

    /// <summary>December.</summary>
    December = 32768,
}
