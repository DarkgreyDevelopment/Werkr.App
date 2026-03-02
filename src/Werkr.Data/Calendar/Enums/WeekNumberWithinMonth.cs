namespace Werkr.Data.Calendar.Enums;

/// <summary>Week number within a month as flags for monthly recurrence.</summary>
[Flags]
public enum WeekNumberWithinMonth {
    /// <summary>No week selected.</summary>
    None = 0,
    /// <summary>First week.</summary>
    First = 1,
    /// <summary>Second week.</summary>
    Second = 2,
    /// <summary>Third week.</summary>
    Third = 4,
    /// <summary>Fourth week.</summary>
    Fourth = 8,
    /// <summary>Fifth week.</summary>
    Fifth = 16,
    /// <summary>Sixth week (partial).</summary>
    Sixth = 32,
}
