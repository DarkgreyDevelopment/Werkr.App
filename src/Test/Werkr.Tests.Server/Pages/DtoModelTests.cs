using Werkr.Common.Models;

namespace Werkr.Tests.Server.Pages;

/// <summary>
/// Unit tests for DTO construction and helper logic used by Blazor pages.
/// </summary>
[TestClass]
public class DtoModelTests {
    [TestMethod]
    public void ScheduleDto_RoundTrip( ) {
        ScheduleDto dto = new(
            Guid.NewGuid( ),
            "Test Schedule",
            60,
            new StartDateTimeDto( new DateOnly( 2026, 1, 1 ), new TimeOnly( 8, 0 ), "UTC" ),
            null,
            new DailyRecurrenceDto( 1 ),
            null,
            null,
            null );

        Assert.AreEqual( "Test Schedule", dto.Name );
        Assert.AreEqual( 60, dto.StopTaskAfterMinutes );
        Assert.IsNotNull( dto.DailyRecurrence );
        Assert.AreEqual( 1, dto.DailyRecurrence.DayInterval );
        Assert.IsNull( dto.Expiration );
    }

    [TestMethod]
    public void TaskDto_TagsPreserved( ) {
        TaskDto dto = new(
            42,
            "Backup DB",
            "Run nightly backup",
            "PowerShell",
            "Backup-Database",
            [],
            ["prod", "db"],
            true,
            null,
            5,
            null,
            "ExitCode",
            null,
            null );

        Assert.AreEqual( 42, dto.Id );
        Assert.HasCount( 2, dto.TargetTags );
        Assert.Contains( "prod", dto.TargetTags );
        Assert.Contains( "db", dto.TargetTags );
    }

    [TestMethod]
    public void WorkflowStepDto_Dependencies( ) {
        StepDependencyDto dep = new( 2, 1 );
        WorkflowStepDto step = new(
            2, 1, 100, 2, "Always", null, 0, null, "AllSucceeded", [dep] );

        Assert.HasCount( 1, step.Dependencies );
        Assert.AreEqual( 1, step.Dependencies[0].DependsOnStepId );
        Assert.AreEqual( 2, step.Dependencies[0].StepId );
    }

    [TestMethod]
    public void WeeklyRecurrenceDto_FlagIntValues( ) {
        // Sun=1, Mon=2, Wed=8 => 11
        WeeklyRecurrenceDto dto = new( 1, 11 );
        Assert.AreEqual( 1, dto.WeekInterval );
        Assert.AreEqual( 11, dto.DaysOfWeek );
    }

    [TestMethod]
    public void MonthlyRecurrenceDto_FlagIntValues( ) {
        // Jan=1, Mar=4 => 5
        MonthlyRecurrenceDto dto = new( [15], 5, null, null );
        Assert.AreEqual( 5, dto.MonthsOfYear );
        Assert.IsNull( dto.WeekNumber );
        Assert.IsNull( dto.DaysOfWeek );
        Assert.AreEqual( 15, dto.DayNumbers![0] );
    }

    [TestMethod]
    public void OccurrencePreviewResponse_EmptyList( ) {
        OccurrencePreviewResponse resp = new(
            Guid.NewGuid( ),
            DateTime.UtcNow.AddDays( 30 ),
            [] );

        Assert.IsEmpty( resp.Occurrences );
    }

    [TestMethod]
    public void DagValidationResult_Valid( ) {
        DagValidationResult result = new( true, [] );
        Assert.IsTrue( result.IsValid );
        Assert.IsEmpty( result.Errors );
    }

    [TestMethod]
    public void DagValidationResult_Invalid( ) {
        DagValidationResult result = new( false, ["Cycle detected at step 3"] );
        Assert.IsFalse( result.IsValid );
        Assert.HasCount( 1, result.Errors );
    }
}
