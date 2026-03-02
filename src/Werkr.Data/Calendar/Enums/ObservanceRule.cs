namespace Werkr.Data.Calendar.Enums;

/// <summary>Rules for how a holiday is observed when it falls on a weekend.</summary>
public enum ObservanceRule {
    /// <summary>No shift — the holiday is observed on its actual date regardless of day of week.</summary>
    None,
    /// <summary>Saturday holidays shift to Friday; Sunday holidays shift to Monday.</summary>
    SaturdayToFriday_SundayToMonday,
    /// <summary>Saturday holidays shift to the following Monday.</summary>
    SaturdayToMonday,
    /// <summary>Observed on the nearest weekday (Saturday → Friday, Sunday → Monday).</summary>
    NearestWeekday,
}
