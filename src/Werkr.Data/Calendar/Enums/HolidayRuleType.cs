namespace Werkr.Data.Calendar.Enums;

/// <summary>The type of algorithmic rule used to compute a holiday date.</summary>
public enum HolidayRuleType {

    /// <summary>A fixed calendar date (e.g., July 4).</summary>
    FixedDate,

    /// <summary>The Nth occurrence of a weekday in a month (e.g., 3rd Monday in January).</summary>
    NthWeekdayOfMonth,

    /// <summary>The last occurrence of a weekday in a month (e.g., last Monday in May).</summary>
    LastWeekdayOfMonth,
}
