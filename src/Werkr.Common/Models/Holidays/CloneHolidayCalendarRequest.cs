namespace Werkr.Common.Models.Holidays;

/// <summary>Request body to clone a holiday calendar with a new name.</summary>
public sealed record CloneHolidayCalendarRequest(
    string NewName
);
