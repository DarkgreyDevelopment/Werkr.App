using System.ComponentModel.DataAnnotations;

using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Validation;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Tests.Data.Unit.Scheduling;

[TestClass]
public class HolidayRuleValidatorTests {

    #region Helpers

    private static HolidayRule MakeValidFixedDate( ) => new( ) {
        Name = "Test Fixed",
        RuleType = HolidayRuleType.FixedDate,
        Month = 7,
        Day = 4,
        ObservanceRule = ObservanceRule.None,
    };

    private static HolidayRule MakeValidNthWeekday( ) => new( ) {
        Name = "Test Nth",
        RuleType = HolidayRuleType.NthWeekdayOfMonth,
        Month = 1,
        DayOfWeek = DayOfWeek.Monday,
        WeekNumber = 3,
        ObservanceRule = ObservanceRule.None,
    };

    private static HolidayRule MakeValidLastWeekday( ) => new( ) {
        Name = "Test Last",
        RuleType = HolidayRuleType.LastWeekdayOfMonth,
        Month = 5,
        DayOfWeek = DayOfWeek.Monday,
        ObservanceRule = ObservanceRule.None,
    };

    #endregion

    // ── Valid Rules ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Valid_FixedDate_ReturnsSuccess( ) {
        ValidationResult? result = HolidayRuleValidator.Validate( MakeValidFixedDate( ) );
        Assert.AreEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void Valid_NthWeekday_ReturnsSuccess( ) {
        ValidationResult? result = HolidayRuleValidator.Validate( MakeValidNthWeekday( ) );
        Assert.AreEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void Valid_LastWeekday_ReturnsSuccess( ) {
        ValidationResult? result = HolidayRuleValidator.Validate( MakeValidLastWeekday( ) );
        Assert.AreEqual( ValidationResult.Success, result );
    }

    // ── Name Required ──────────────────────────────────────────────────────────

    [TestMethod]
    public void MissingName_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Name = "";
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
        Assert.Contains( "Name", result!.ErrorMessage! );
    }

    // ── Month Required ─────────────────────────────────────────────────────────

    [TestMethod]
    public void MissingMonth_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Month = null;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
        Assert.Contains( "Month", result!.ErrorMessage! );
    }

    [TestMethod]
    public void MonthZero_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Month = 0;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void MonthThirteen_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Month = 13;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    // ── FixedDate Specifics ────────────────────────────────────────────────────

    [TestMethod]
    public void FixedDate_MissingDay_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Day = null;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void FixedDate_DayZero_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Day = 0;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void FixedDate_Day32_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Day = 32;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void FixedDate_Feb30_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.Month = 2;
        rule.Day = 30;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void FixedDate_WithWeekNumber_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.WeekNumber = 1;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void FixedDate_WithDayOfWeek_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.DayOfWeek = DayOfWeek.Monday;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    // ── NthWeekday Specifics ───────────────────────────────────────────────────

    [TestMethod]
    public void NthWeekday_MissingDayOfWeek_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.DayOfWeek = null;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void NthWeekday_MissingWeekNumber_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.WeekNumber = null;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void NthWeekday_WeekNumberZero_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.WeekNumber = 0;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void NthWeekday_WeekNumberSix_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.WeekNumber = 6;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void NthWeekday_WithDay_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.Day = 15;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    // ── LastWeekday Specifics ──────────────────────────────────────────────────

    [TestMethod]
    public void LastWeekday_MissingDayOfWeek_Fails( ) {
        HolidayRule rule = MakeValidLastWeekday( );
        rule.DayOfWeek = null;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void LastWeekday_WithDay_Fails( ) {
        HolidayRule rule = MakeValidLastWeekday( );
        rule.Day = 1;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void LastWeekday_WithWeekNumber_Fails( ) {
        HolidayRule rule = MakeValidLastWeekday( );
        rule.WeekNumber = 3;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    // ── ObservanceRule Validation ──────────────────────────────────────────────

    [TestMethod]
    public void ObservanceRule_NonNone_OnNthWeekday_Fails( ) {
        HolidayRule rule = MakeValidNthWeekday( );
        rule.ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
        Assert.Contains( "ObservanceRule", result!.ErrorMessage! );
    }

    [TestMethod]
    public void ObservanceRule_NonNone_OnLastWeekday_Fails( ) {
        HolidayRule rule = MakeValidLastWeekday( );
        rule.ObservanceRule = ObservanceRule.NearestWeekday;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void ObservanceRule_OnFixedDate_Allowed( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreEqual( ValidationResult.Success, result );
    }

    // ── Time Window Validation ─────────────────────────────────────────────────

    [TestMethod]
    public void TimeWindow_AllSet_Valid( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.WindowStart = new TimeOnly( 9, 30 );
        rule.WindowEnd = new TimeOnly( 16, 0 );
        rule.WindowTimeZoneId = "America/New_York";
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void TimeWindow_PartialSet_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.WindowStart = new TimeOnly( 9, 30 );
        // WindowEnd and WindowTimeZoneId are null
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void TimeWindow_StartAfterEnd_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.WindowStart = new TimeOnly( 16, 0 );
        rule.WindowEnd = new TimeOnly( 9, 30 );
        rule.WindowTimeZoneId = "America/New_York";
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void TimeWindow_InvalidTimezone_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.WindowStart = new TimeOnly( 9, 30 );
        rule.WindowEnd = new TimeOnly( 16, 0 );
        rule.WindowTimeZoneId = "Invalid/Timezone";
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    // ── Year Range Validation ──────────────────────────────────────────────────

    [TestMethod]
    public void YearRange_StartAfterEnd_Fails( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.YearStart = 2030;
        rule.YearEnd = 2025;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreNotEqual( ValidationResult.Success, result );
    }

    [TestMethod]
    public void YearRange_Equal_Valid( ) {
        HolidayRule rule = MakeValidFixedDate( );
        rule.YearStart = 2026;
        rule.YearEnd = 2026;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        Assert.AreEqual( ValidationResult.Success, result );
    }
}
