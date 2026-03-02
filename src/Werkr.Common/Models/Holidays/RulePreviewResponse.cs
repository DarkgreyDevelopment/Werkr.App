namespace Werkr.Common.Models.Holidays;

/// <summary>Rule preview response.</summary>
public sealed record RulePreviewResponse(
    int StartYear,
    int EndYear,
    IReadOnlyList<HolidayDateDto> Dates );
