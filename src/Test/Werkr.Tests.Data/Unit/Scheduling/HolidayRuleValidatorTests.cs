using System.ComponentModel.DataAnnotations;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Validation;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Tests.Data.Unit.Scheduling;

/// <summary>
/// Unit tests for the <see cref="HolidayRuleValidator"/> class, validating fixed-date, nth-weekday, and last-weekday
/// rule constraints including field requirements, boundary checks, observance rules, time-window validation, and
/// year-range validation.
/// </summary>
[TestClass]
public class HolidayRuleValidatorTests {

    #region Helpers

    /// <summary>
    /// Creates a valid fixed-date <see cref="HolidayRule"/> for July 4 with no observance.
    /// </summary>
    private static HolidayRule MakeValidFixedDate( ) => new( ) {
        Name = "Test Fixed",
        RuleType = HolidayRuleType.FixedDate,
        Month = 7,
        Day = 4,
        ObservanceRule = ObservanceRule.None,
    };

    /// <summary>
    /// Creates a valid nth-weekday <see cref="HolidayRule"/> for the third Monday of January.
    /// </summary>
    private static HolidayRule MakeValidNthWeekday( ) => new( ) {
        Name = "Test Nth",
        RuleType = HolidayRuleType.NthWeekdayOfMonth,
        Month = 1,
        DayOfWeek = DayOfWeek.Monday,
        WeekNumber = 3,
        ObservanceRule = ObservanceRule.None,
    };

    /// <summary>
    /// Creates a valid last-weekday <see cref="HolidayRule"/> for the last Monday of May.
    /// </summary>
    private static HolidayRule MakeValidLastWeekday( ) => new( ) {
        Name = "Test Last",
        RuleType = HolidayRuleType.LastWeekdayOfMonth,
        Month = 5,
        DayOfWeek = DayOfWeek.Monday,
        ObservanceRule = ObservanceRule.None,
    };

    #endregion

    // ── Valid Rules ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a valid fixed-date rule passes validation.
    /// </summary>
    [TestMethod]
    public void Valid_FixedDate_ReturnsSuccess( ) {
        ValidationResult? result = HolidayRuleValidator.Validate( MakeValidFixedDate( ) );
        Assert.AreEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that a valid nth-weekday rule passes validation.
    /// </summary>
    [TestMethod]
    public void Valid_NthWeekday_ReturnsSuccess( ) {
        ValidationResult? result = HolidayRuleValidator.Validate( MakeValidNthWeekday( ) );
        Assert.AreEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that a valid last-weekday rule passes validation.
    /// </summary>
    [TestMethod]
    public void Valid_LastWeekday_ReturnsSuccess( ) {
        ValidationResult? result = HolidayRuleValidator.Validate( MakeValidLastWeekday( ) );
        Assert.AreEqual(
            ValidationResult.Success,
            result
        );
    }

    // ── Name Required ──────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty name fails validation with an error mentioning "Name".
    /// </summary>
    [TestMethod]
    public void MissingName_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Name = string.Empty;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
        Assert.Contains(
            "Name",
            result!.ErrorMessage!
        );
    }

    // ── Month Required ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a null month fails validation.
    /// </summary>
    [TestMethod]
    public void MissingMonth_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Month = null;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
        Assert.Contains(
            "Month",
            result!.ErrorMessage!
        );
    }

    /// <summary>
    /// Verifies that month zero fails validation.
    /// </summary>
    [TestMethod]
    public void MonthZero_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Month = 0;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that month 13 fails validation.
    /// </summary>
    [TestMethod]
    public void MonthThirteen_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Month = 13;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    // ── FixedDate Specifics ────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a null day on a fixed-date rule fails validation.
    /// </summary>
    [TestMethod]
    public void FixedDate_MissingDay_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Day = null;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that day zero on a fixed-date rule fails validation.
    /// </summary>
    [TestMethod]
    public void FixedDate_DayZero_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Day = 0;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that day 32 on a fixed-date rule fails validation.
    /// </summary>
    [TestMethod]
    public void FixedDate_Day32_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Day = 32;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that February 30 on a fixed-date rule fails validation.
    /// </summary>
    [TestMethod]
    public void FixedDate_Feb30_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Month = 2;
        rule.Day = 30;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that a fixed-date rule with a week number set fails validation.
    /// </summary>
    [TestMethod]
    public void FixedDate_WithWeekNumber_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.WeekNumber = 1;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that a fixed-date rule with a day-of-week set fails validation.
    /// </summary>
    [TestMethod]
    public void FixedDate_WithDayOfWeek_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.DayOfWeek = DayOfWeek.Monday;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    // ── NthWeekday Specifics ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies that an nth-weekday rule with a null day-of-week fails validation.
    /// </summary>
    [TestMethod]
    public void NthWeekday_MissingDayOfWeek_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.DayOfWeek = null;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that an nth-weekday rule with a null week number fails validation.
    /// </summary>
    [TestMethod]
    public void NthWeekday_MissingWeekNumber_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.WeekNumber = null;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that week number zero on an nth-weekday rule fails validation.
    /// </summary>
    [TestMethod]
    public void NthWeekday_WeekNumberZero_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.WeekNumber = 0;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that week number six on an nth-weekday rule fails validation.
    /// </summary>
    [TestMethod]
    public void NthWeekday_WeekNumberSix_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.WeekNumber = 6;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that an nth-weekday rule with a day value set fails validation.
    /// </summary>
    [TestMethod]
    public void NthWeekday_WithDay_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.Day = 15;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    // ── LastWeekday Specifics ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a last-weekday rule with a null day-of-week fails validation.
    /// </summary>
    [TestMethod]
    public void LastWeekday_MissingDayOfWeek_Fails( ) {
        HolidayRule rule = MakeValidLastWeekday( );
        rule.DayOfWeek = null;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that a last-weekday rule with a day value set fails validation.
    /// </summary>
    [TestMethod]
    public void LastWeekday_WithDay_Fails( ) {
        HolidayRule rule = MakeValidLastWeekday( );
        rule.Day = 1;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that a last-weekday rule with a week number set fails validation.
    /// </summary>
    [TestMethod]
    public void LastWeekday_WithWeekNumber_Fails( ) {
        HolidayRule rule = MakeValidLastWeekday( );
        rule.WeekNumber = 3;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    // ── ObservanceRule Validation ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a non-None observance rule on an nth-weekday rule fails validation.
    /// </summary>
    [TestMethod]
    public void ObservanceRule_NonNone_OnNthWeekday_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
        Assert.Contains(
            "ObservanceRule",
            result!.ErrorMessage!
        );
    }

    /// <summary>
    /// Verifies that a non-None observance rule on a last-weekday rule fails validation.
    /// </summary>
    [TestMethod]
    public void ObservanceRule_NonNone_OnLastWeekday_Fails( ) {
        HolidayRule rule = MakeValidLastWeekday( );
        rule.ObservanceRule = ObservanceRule.NearestWeekday;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that an observance rule on a fixed-date rule is allowed.
    /// </summary>
    [TestMethod]
    public void ObservanceRule_OnFixedDate_Allowed( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreEqual(
            ValidationResult.Success,
            result
        );
    }

    // ── Time Window Validation ─────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a complete time-window configuration passes validation.
    /// </summary>
    [TestMethod]
    public void TimeWindow_AllSet_Valid( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.WindowStart = new TimeOnly(
            9,
            30
        );
        rule.WindowEnd = new TimeOnly(
            16,
            0
        );
        rule.WindowTimeZoneId = "America/New_York";
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that a partially set time window (start only) fails validation.
    /// </summary>
    [TestMethod]
    public void TimeWindow_PartialSet_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.WindowStart = new TimeOnly(
            9,
            30
        );
        // WindowEnd and WindowTimeZoneId are null
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that a time window with start after end fails validation.
    /// </summary>
    [TestMethod]
    public void TimeWindow_StartAfterEnd_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.WindowStart = new TimeOnly(
            16,
            0
        );
        rule.WindowEnd = new TimeOnly(
            9,
            30
        );
        rule.WindowTimeZoneId = "America/New_York";
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that an invalid time-zone identifier in the time window fails validation.
    /// </summary>
    [TestMethod]
    public void TimeWindow_InvalidTimezone_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.WindowStart = new TimeOnly(
            9,
            30
        );
        rule.WindowEnd = new TimeOnly(
            16,
            0
        );
        rule.WindowTimeZoneId = "Invalid/Timezone";
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    // ── Year Range Validation ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a year range with start after end fails validation.
    /// </summary>
    [TestMethod]
    public void YearRange_StartAfterEnd_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.YearStart = 2030;
        rule.YearEnd = 2025;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual(
            ValidationResult.Success,
            result
        );
    }

    /// <summary>
    /// Verifies that an equal start and end year passes validation.
    /// </summary>
    [TestMethod]
    public void YearRange_Equal_Valid( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.YearStart = 2026;
        rule.YearEnd = 2026;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreEqual(
            ValidationResult.Success,
            result
        );
    }
}
