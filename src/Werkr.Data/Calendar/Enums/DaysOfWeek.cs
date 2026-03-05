namespace Werkr.Data.Calendar.Enums;

/// <summary>Days of the week as flags for recurrence patterns.</summary>
[Flags]
public enum DaysOfWeek {

    /// <summary>No days selected.</summary>
    None = 0,

    /// <summary>Monday.</summary>
    Monday = 1,

    /// <summary>Tuesday.</summary>
    Tuesday = 2,

    /// <summary>Wednesday.</summary>
    Wednesday = 4,

    /// <summary>Thursday.</summary>
    Thursday = 8,

    /// <summary>Friday.</summary>
    Friday = 16,

    /// <summary>Saturday.</summary>
    Saturday = 32,

    /// <summary>Sunday.</summary>
    Sunday = 64,
}
