using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Werkr.Common.Models;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Encryption;
using Werkr.Data.Entities;
using Werkr.Data.Entities.Audit;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Schedule;
using Werkr.Data.Entities.Settings;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Triggers;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Data;

/// <summary>
/// Primary DbContext for Werkr application data (non-Identity).
/// </summary>
public class WerkrDbContext : DbContext {

    /// <summary>Creates a new instance for use by derived provider-specific contexts.</summary>
    /// <param name="options">The options forwarded from a derived context.</param>
    public WerkrDbContext( DbContextOptions<WerkrDbContext> options ) : base( options ) { }

    /// <summary>Creates a new instance for use by derived provider-specific contexts.</summary>
    /// <param name="options">The options forwarded from a derived context.</param>
    protected WerkrDbContext( DbContextOptions options ) : base( options ) { }

    /// <summary>
    /// Field encryption provider for transparent column encryption.
    /// Null when encryption is not configured (e.g., in test contexts).
    /// </summary>
    public FieldEncryptionProvider? FieldEncryption { get; set; }

    /// <summary>Pending registration bundles (server-side only).</summary>
    public DbSet<RegistrationBundle> RegistrationBundles => Set<RegistrationBundle>( );

    /// <summary>Established connections between Server and Agent.</summary>
    public DbSet<RegisteredConnection> RegisteredConnections => Set<RegisteredConnection>( );

    /// <summary>Schedules.</summary>
    public DbSet<DbSchedule> Schedules => Set<DbSchedule>( );

    /// <summary>Start date/time info for schedules.</summary>
    public DbSet<StartDateTimeInfo> StartDateTimeInfos => Set<StartDateTimeInfo>( );

    /// <summary>Expiration date/time info for schedules.</summary>
    public DbSet<ExpirationDateTimeInfo> ExpirationDateTimeInfos => Set<ExpirationDateTimeInfo>( );

    /// <summary>Schedule repeat options.</summary>
    public DbSet<ScheduleRepeatOptions> ScheduleRepeatOptions => Set<ScheduleRepeatOptions>( );

    /// <summary>Daily recurrences.</summary>
    public DbSet<DailyRecurrence> DailyRecurrences => Set<DailyRecurrence>( );

    /// <summary>Weekly recurrences.</summary>
    public DbSet<WeeklyRecurrence> WeeklyRecurrences => Set<WeeklyRecurrence>( );

    /// <summary>Monthly recurrences.</summary>
    public DbSet<MonthlyRecurrence> MonthlyRecurrences => Set<MonthlyRecurrence>( );

    /// <summary>Tasks.</summary>
    public DbSet<WerkrTask> Tasks => Set<WerkrTask>( );

    /// <summary>Immutable task version snapshots.</summary>
    public DbSet<TaskVersion> TaskVersions => Set<TaskVersion>( );

    /// <summary>Immutable workflow version snapshots.</summary>
    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>( );

    /// <summary>Jobs.</summary>
    public DbSet<WerkrJob> Jobs => Set<WerkrJob>( );

    /// <summary>Workflows.</summary>
    public DbSet<Workflow> Workflows => Set<Workflow>( );

    /// <summary>Workflow steps.</summary>
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>( );

    /// <summary>Workflow step dependencies (many-to-many join table).</summary>
    public DbSet<WorkflowStepDependency> WorkflowStepDependencies => Set<WorkflowStepDependency>( );

    /// <summary>Workflow execution runs.</summary>
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>( );

    /// <summary>Design-time variable definitions on workflows.</summary>
    public DbSet<WorkflowVariable> WorkflowVariables => Set<WorkflowVariable>( );

    /// <summary>Append-only runtime variable values per workflow run.</summary>
    public DbSet<WorkflowRunVariable> WorkflowRunVariables => Set<WorkflowRunVariable>( );

    /// <summary>Holiday calendars.</summary>
    public DbSet<HolidayCalendar> HolidayCalendars => Set<HolidayCalendar>( );

    /// <summary>Holiday rules.</summary>
    public DbSet<HolidayRule> HolidayRules => Set<HolidayRule>( );

    /// <summary>Holiday dates (materialized and manual).</summary>
    public DbSet<HolidayDate> HolidayDates => Set<HolidayDate>( );

    /// <summary>Schedule-to-holiday-calendar junction.</summary>
    public DbSet<ScheduleHolidayCalendar> ScheduleHolidayCalendars => Set<ScheduleHolidayCalendar>( );

    /// <summary>Append-only audit events for all auditable operations.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>( );

    /// <summary>Task-to-schedule many-to-many join table.</summary>
    public DbSet<TaskSchedule> TaskSchedules => Set<TaskSchedule>( );

    /// <summary>Workflow-to-schedule many-to-many join table.</summary>
    public DbSet<WorkflowSchedule> WorkflowSchedules => Set<WorkflowSchedule>( );

    /// <summary>Per-run-per-step execution tracking (supports retry attempts).</summary>
    public DbSet<WorkflowStepExecution> WorkflowStepExecutions => Set<WorkflowStepExecution>( );

    /// <summary>Named saved filter views (personal and shared).</summary>
    public DbSet<SavedFilter> SavedFilters => Set<SavedFilter>( );

    /// <summary>File monitor triggers that watch directories and initiate workflow runs.</summary>
    public DbSet<FileMonitorTrigger> FileMonitorTriggers => Set<FileMonitorTrigger>( );

    /// <summary>Immutable trigger version snapshots.</summary>
    public DbSet<TriggerVersion> TriggerVersions => Set<TriggerVersion>( );

    /// <inheritdoc/>
    protected override void OnModelCreating( ModelBuilder modelBuilder ) {
        base.OnModelCreating( modelBuilder );

        // Set default schema for Postgres (SQLite doesn't support schemas)
        if (Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") {
            _ = modelBuilder.HasDefaultSchema( "werkr" );
        }

        // RegistrationBundle indexes
        _ = modelBuilder.Entity<RegistrationBundle>( entity => {
            _ = entity.HasIndex( e => e.BundleId )
                .IsUnique( );

            // BundleId byte[] needs explicit comparer (hex value converter)
            entity.Property( e => e.BundleId ).Metadata.SetValueComparer(
                new ValueComparer<byte[]>(
                    ( a, b ) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual( b )),
                    v => v == null ? 0 : v.Aggregate( 0, ( hash, b ) => HashCode.Combine( hash, b ) ),
                    v => v == null ? Array.Empty<byte>( ) : v.ToArray( )
                )
            );

            // RegistrationBundle.AllowedPaths stored as JSON
            PropertyBuilder<string[]> allowedPathsProp = entity.Property( e => e.AllowedPaths )
                .HasConversion(
                    v => JsonSerializer.Serialize( v, (JsonSerializerOptions?)null ),
                    v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>()
                );
            allowedPathsProp.Metadata.SetValueComparer(
                new ValueComparer<string[]>(
                    ( a, b ) => ReferenceEquals( a, b ) || (a != null && b != null && a.SequenceEqual( b, StringComparer.OrdinalIgnoreCase )),
                    v => v == null ? 0 : v.Aggregate( 0, ( hash, item ) => HashCode.Combine( hash, item == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode( item ) ) ),
                    v => v == null ? Array.Empty<string>( ) : v.ToArray( )
                )
            );
        } );

        // RegisteredConnection indexes
        _ = modelBuilder.Entity<RegisteredConnection>( entity => {
            _ = entity.HasIndex( e => e.ConnectionName );
            _ = entity.HasIndex( e => e.RemoteUrl );

            // byte[] properties with hex value converter need explicit comparers
            entity.Property( e => e.SharedKey ).Metadata.SetValueComparer(
                new ValueComparer<byte[]>(
                    ( a, b ) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual( b )),
                    v => v == null ? 0 : v.Aggregate( 0, ( hash, b ) => HashCode.Combine( hash, b ) ),
                    v => v == null ? Array.Empty<byte>( ) : v.ToArray( )
                )
            );

            entity.Property( e => e.PreviousSharedKey ).Metadata.SetValueComparer(
                new ValueComparer<byte[]?>(
                    ( a, b ) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual( b )),
                    v => v == null ? 0 : v.Aggregate( 0, ( hash, b ) => HashCode.Combine( hash, b ) ),
                    v => v == null ? null : v.ToArray( )
                )
            );
        } );

        // MonthlyRecurrence.DayNumbers stored as JSON
        _ = modelBuilder.Entity<MonthlyRecurrence>( entity => {
            PropertyBuilder<int[]?> prop = entity.Property( e => e.DayNumbers )
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize( v, (JsonSerializerOptions?)null ),
                    v => v == null ? null : JsonSerializer.Deserialize<int[]>(v, (JsonSerializerOptions?)null)
                );
            prop.Metadata.SetValueComparer(
                new ValueComparer<int[]?>(
                    ( a, b ) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual( b )),
                    v => v == null ? 0 : v.Aggregate( 0, ( hash, item ) => HashCode.Combine( hash, item ) ),
                    v => v == null ? null : v.ToArray( )
                )
            );
        } );

        // RegisteredConnection.Tags stored as JSON
        _ = modelBuilder.Entity<RegisteredConnection>( entity => {
            PropertyBuilder<string[]> prop = entity.Property( e => e.Tags )
                .HasConversion(
                    v => JsonSerializer.Serialize( v, (JsonSerializerOptions?)null ),
                    v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>()
                );
            prop.Metadata.SetValueComparer(
                new ValueComparer<string[]>(
                    ( a, b ) => ReferenceEquals( a, b ) || (a != null && b != null && a.SequenceEqual( b, StringComparer.OrdinalIgnoreCase )),
                    v => v == null ? 0 : v.Aggregate( 0, ( hash, item ) => HashCode.Combine( hash, item == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode( item ) ) ),
                    v => v == null ? Array.Empty<string>( ) : v.ToArray( )
                )
            );

            // RegisteredConnection.AllowedPaths stored as JSON
            PropertyBuilder<string[]> allowedPathsProp = entity.Property( e => e.AllowedPaths )
                .HasConversion(
                    v => JsonSerializer.Serialize( v, (JsonSerializerOptions?)null ),
                    v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>()
                );
            allowedPathsProp.Metadata.SetValueComparer(
                new ValueComparer<string[]>(
                    ( a, b ) => ReferenceEquals( a, b ) || (a != null && b != null && a.SequenceEqual( b, StringComparer.OrdinalIgnoreCase )),
                    v => v == null ? 0 : v.Aggregate( 0, ( hash, item ) => HashCode.Combine( hash, item == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode( item ) ) ),
                    v => v == null ? Array.Empty<string>( ) : v.ToArray( )
                )
            );
        } );

        // WerkrTask.TargetTags stored as JSON
        _ = modelBuilder.Entity<WerkrTask>( entity => {
            PropertyBuilder<string[]> targetTagsProp = entity.Property( e => e.TargetTags )
                .HasConversion(
                    v => JsonSerializer.Serialize( v, (JsonSerializerOptions?)null ),
                    v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>()
                );
            targetTagsProp.Metadata.SetValueComparer(
                new ValueComparer<string[]>(
                    ( a, b ) => ReferenceEquals( a, b ) || (a != null && b != null && a.SequenceEqual( b, StringComparer.OrdinalIgnoreCase )),
                    v => v == null ? 0 : v.Aggregate( 0, ( hash, item ) => HashCode.Combine( hash, item == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode( item ) ) ),
                    v => v == null ? Array.Empty<string>( ) : v.ToArray( )
                )
            );

            // WerkrTask.Arguments stored as JSON
            PropertyBuilder<string[]?> argsProp = entity.Property( e => e.Arguments )
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize( v, (JsonSerializerOptions?)null ),
                    v => v == null ? null : JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null)
                );
            argsProp.Metadata.SetValueComparer(
                new ValueComparer<string[]?>(
                    ( a, b ) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual( b )),
                    v => v == null ? 0 : v.Aggregate( 0, ( hash, item ) => HashCode.Combine( hash, item ) ),
                    v => v == null ? null : v.ToArray( )
                )
            );
        } );

        // TaskVersion — unique index on (TaskId, VersionNumber), standalone TaskId index, cascade delete from task
        _ = modelBuilder.Entity<TaskVersion>( entity => {
            _ = entity.HasIndex( e => new { e.TaskId, e.VersionNumber } )
                .IsUnique( );

            _ = entity.HasIndex( e => e.TaskId );

            _ = entity.HasOne( e => e.Task )
                .WithMany( t => t.Versions )
                .HasForeignKey( e => e.TaskId )
                .OnDelete( DeleteBehavior.Cascade );
        } );

        // WerkrTask.CurrentVersionId FK — SetNull to avoid circular cascade with TaskVersion
        _ = modelBuilder.Entity<WerkrTask>( entity => {
            _ = entity.HasOne( e => e.CurrentVersion )
                .WithMany( )
                .HasForeignKey( e => e.CurrentVersionId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // WorkflowStep.TaskVersionId FK — SetNull on delete so steps survive version cleanup
        _ = modelBuilder.Entity<WorkflowStep>( entity => {
            _ = entity.HasOne( e => e.TaskVersion )
                .WithMany( )
                .HasForeignKey( e => e.TaskVersionId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // Workflow.TargetTags stored as JSON
        _ = modelBuilder.Entity<Workflow>( entity => {
            PropertyBuilder<string[]?> targetTagsProp = entity.Property( e => e.TargetTags )
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize( v, (JsonSerializerOptions?)null ),
                    v => v == null ? null : JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null)
                );
            targetTagsProp.Metadata.SetValueComparer(
                new ValueComparer<string[]?>(
                    ( a, b ) => ReferenceEquals( a, b ) || (a != null && b != null && a.SequenceEqual( b, StringComparer.OrdinalIgnoreCase )),
                    v => v == null ? 0 : v.Aggregate( 0, ( hash, item ) => HashCode.Combine( hash, item == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode( item ) ) ),
                    v => v == null ? null : v.ToArray( )
                )
            );
        } );

        // WorkflowVersion — unique index on (WorkflowId, VersionNumber), standalone WorkflowId index, cascade delete from workflow
        _ = modelBuilder.Entity<WorkflowVersion>( entity => {
            _ = entity.HasIndex( e => new { e.WorkflowId, e.VersionNumber } )
                .IsUnique( );

            _ = entity.HasIndex( e => e.WorkflowId );

            _ = entity.HasOne( e => e.Workflow )
                .WithMany( w => w.Versions )
                .HasForeignKey( e => e.WorkflowId )
                .OnDelete( DeleteBehavior.Cascade );
        } );

        // Workflow.CurrentVersionId FK — SetNull to avoid circular cascade with WorkflowVersion
        _ = modelBuilder.Entity<Workflow>( entity => {
            _ = entity.HasOne( e => e.CurrentVersion )
                .WithMany( )
                .HasForeignKey( e => e.CurrentVersionId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // WorkflowRun.WorkflowVersionId FK — SetNull so runs survive version cleanup
        _ = modelBuilder.Entity<WorkflowRun>( entity => {
            _ = entity.HasOne( e => e.WorkflowVersion )
                .WithMany( )
                .HasForeignKey( e => e.WorkflowVersionId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // WorkflowStep.ChildWorkflow FK — SetNull on delete to avoid cascading removal of the parent step
        _ = modelBuilder.Entity<WorkflowStep>( entity => {
            _ = entity.HasOne( e => e.ChildWorkflow )
                .WithMany( )
                .HasForeignKey( e => e.ChildWorkflowId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // Workflow.ParentStepId FK — allows reverse navigation from child workflow to parent step
        _ = modelBuilder.Entity<Workflow>( entity => {
            _ = entity.HasOne<WorkflowStep>( )
                .WithMany( )
                .HasForeignKey( e => e.ParentStepId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // WorkflowStepDependency composite key and relationships
        _ = modelBuilder.Entity<WorkflowStepDependency>( entity => {
            _ = entity.HasKey( e => new { e.StepId, e.DependsOnStepId } );

            _ = entity.HasOne( e => e.Step )
                .WithMany( s => s.Dependencies )
                .HasForeignKey( e => e.StepId )
                .OnDelete( DeleteBehavior.Cascade );

            _ = entity.HasOne( e => e.DependsOnStep )
                .WithMany( s => s.Dependents )
                .HasForeignKey( e => e.DependsOnStepId )
                .OnDelete( DeleteBehavior.Restrict );
        } );

        // HolidayCalendar
        _ = modelBuilder.Entity<HolidayCalendar>( entity => {
            _ = entity.HasIndex( e => e.Name ).IsUnique( );

            _ = entity.HasMany( e => e.Rules )
                .WithOne( r => r.Calendar )
                .HasForeignKey( r => r.HolidayCalendarId )
                .OnDelete( DeleteBehavior.Cascade );

            _ = entity.HasMany( e => e.Dates )
                .WithOne( d => d.Calendar )
                .HasForeignKey( d => d.HolidayCalendarId )
                .OnDelete( DeleteBehavior.Cascade );
        } );

        // HolidayRule
        _ = modelBuilder.Entity<HolidayRule>( entity => {
            if (Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") {
                _ = entity.Property( e => e.Id ).UseIdentityAlwaysColumn( );
            }

            _ = entity.HasMany( e => e.GeneratedDates )
                .WithOne( d => d.GeneratedByRule )
                .HasForeignKey( d => d.HolidayRuleId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // HolidayDate — unique index on (CalendarId, Date): one entry per calendar per date.
        _ = modelBuilder.Entity<HolidayDate>( entity => {
            if (Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") {
                _ = entity.Property( e => e.Id ).UseIdentityAlwaysColumn( );
            }

            _ = entity.HasIndex( e => new { e.HolidayCalendarId, e.Date } )
                .IsUnique( );
        } );

        // ScheduleHolidayCalendar — composite PK + unique index on ScheduleId (one calendar per schedule, H5)
        _ = modelBuilder.Entity<ScheduleHolidayCalendar>( entity => {
            _ = entity.HasKey( e => new { e.ScheduleId, e.HolidayCalendarId } );
            _ = entity.HasIndex( e => e.ScheduleId ).IsUnique( );

            _ = entity.HasOne( e => e.Schedule )
                .WithOne( s => s.HolidayCalendarLink )
                .HasForeignKey<ScheduleHolidayCalendar>( e => e.ScheduleId )
                .OnDelete( DeleteBehavior.Cascade );

            _ = entity.HasOne( e => e.Calendar )
                .WithMany( c => c.ScheduleLinks )
                .HasForeignKey( e => e.HolidayCalendarId )
                .OnDelete( DeleteBehavior.Cascade );
        } );

        // AuditEvent — append-only audit table with multiple indexes for query performance
        _ = modelBuilder.Entity<AuditEvent>( entity => {
            if (Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") {
                _ = entity.Property( e => e.Id ).UseIdentityAlwaysColumn( );
            }
            _ = entity.HasIndex( e => e.TimestampUtc ).IsDescending( );
            _ = entity.HasIndex( e => e.EventTypeId );
            _ = entity.HasIndex( e => e.EventCategory );
            _ = entity.HasIndex( e => new { e.EntityType, e.EntityId } );
            _ = entity.HasIndex( e => e.ActorId );
        } );

        // TaskSchedule — many-to-many join between WerkrTask and DbSchedule
        _ = modelBuilder.Entity<TaskSchedule>( entity => {
            _ = entity.HasKey( e => new { e.TaskId, e.ScheduleId } );

            _ = entity.HasOne( e => e.Task )
                .WithMany( t => t.TaskSchedules )
                .HasForeignKey( e => e.TaskId )
                .OnDelete( DeleteBehavior.Cascade );

            _ = entity.HasOne( e => e.Schedule )
                .WithMany( s => s.TaskSchedules )
                .HasForeignKey( e => e.ScheduleId )
                .OnDelete( DeleteBehavior.Cascade );
        } );

        // WorkflowSchedule — many-to-many join between Workflow and DbSchedule
        _ = modelBuilder.Entity<WorkflowSchedule>( entity => {
            _ = entity.HasKey( e => new { e.WorkflowId, e.ScheduleId } );

            _ = entity.HasOne( e => e.Workflow )
                .WithMany( w => w.WorkflowSchedules )
                .HasForeignKey( e => e.WorkflowId )
                .OnDelete( DeleteBehavior.Cascade );

            _ = entity.HasOne( e => e.Schedule )
                .WithMany( s => s.WorkflowSchedules )
                .HasForeignKey( e => e.ScheduleId )
                .OnDelete( DeleteBehavior.Cascade );
        } );

        // WorkflowVariable — design-time variable definitions on workflows
        _ = modelBuilder.Entity<WorkflowVariable>( entity => {
            _ = entity.HasKey( e => e.Id );

            _ = entity.Property( e => e.Name ).HasMaxLength( 128 );
            _ = entity.Property( e => e.Description ).HasMaxLength( 500 );
            _ = entity.Property( e => e.DataType ).HasMaxLength( 32 );

            // Unique variable name per workflow (case-insensitive)
            _ = entity.HasIndex( e => new { e.WorkflowId, e.Name } )
                .IsUnique( );

            _ = entity.HasOne( e => e.Workflow )
                .WithMany( w => w.Variables )
                .HasForeignKey( e => e.WorkflowId )
                .OnDelete( DeleteBehavior.Cascade );
        } );

        // WorkflowRunVariable — append-only runtime variable values per workflow run
        _ = modelBuilder.Entity<WorkflowRunVariable>( entity => {
            _ = entity.HasKey( e => e.Id );

            _ = entity.Property( e => e.VariableName ).HasMaxLength( 128 );

            // Unique index enforces append-only invariant at DB level
            _ = entity.HasIndex( e => new { e.WorkflowRunId, e.VariableName, e.Version } )
                .IsUnique( );

            // Index for "which variables did step X produce?" queries
            _ = entity.HasIndex( e => e.ProducedByStepId );

            _ = entity.HasOne( e => e.WorkflowRun )
                .WithMany( r => r.RunVariables )
                .HasForeignKey( e => e.WorkflowRunId )
                .OnDelete( DeleteBehavior.Cascade );

            _ = entity.HasOne( e => e.ProducedByStep )
                .WithMany( )
                .HasForeignKey( e => e.ProducedByStepId )
                .OnDelete( DeleteBehavior.SetNull );

            _ = entity.HasOne( e => e.ProducedByJob )
                .WithMany( )
                .HasForeignKey( e => e.ProducedByJobId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // WerkrJob — StepId FK and index
        _ = modelBuilder.Entity<WerkrJob>( entity => {
            _ = entity.HasIndex( e => new { e.WorkflowRunId, e.StepId } )
                .HasDatabaseName( "IX_jobs_WorkflowRunId_StepId" );

            _ = entity.HasOne( e => e.Step )
                .WithMany( )
                .HasForeignKey( e => e.StepId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // WorkflowStepExecution — per-run-per-step execution tracking
        _ = modelBuilder.Entity<WorkflowStepExecution>( entity => {
            _ = entity.HasIndex( e => new { e.WorkflowRunId, e.StepId, e.Attempt } )
                .IsUnique( );

            _ = entity.HasIndex( e => e.WorkflowRunId );

            _ = entity.HasOne( e => e.WorkflowRun )
                .WithMany( r => r.StepExecutions )
                .HasForeignKey( e => e.WorkflowRunId )
                .OnDelete( DeleteBehavior.Cascade );

            _ = entity.HasOne( e => e.Step )
                .WithMany( )
                .HasForeignKey( e => e.StepId )
                .OnDelete( DeleteBehavior.Cascade );

            _ = entity.HasOne( e => e.Job )
                .WithMany( )
                .HasForeignKey( e => e.JobId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // SavedFilter — named filter views per page per user
        _ = modelBuilder.Entity<SavedFilter>( entity => {
            _ = entity.HasIndex( e => new { e.PageKey, e.OwnerId } );
            _ = entity.HasIndex( e => new { e.PageKey, e.IsShared } );
        } );

        // FileMonitorTrigger — FK to Workflow with cascade delete, JSON conversions
        _ = modelBuilder.Entity<FileMonitorTrigger>( entity => {
            _ = entity.HasOne( e => e.Workflow )
                .WithMany( )
                .HasForeignKey( e => e.WorkflowId )
                .OnDelete( DeleteBehavior.Cascade );

            _ = entity.HasIndex( e => e.WorkflowId );

            // EventTypes stored as JSON string (e.g. ["created","changed"])
            PropertyBuilder<string> eventTypesProp = entity.Property( e => e.EventTypes );
            eventTypesProp.Metadata.SetValueComparer(
                new ValueComparer<string>(
                    ( a, b ) => string.Equals( a, b, StringComparison.Ordinal ),
                    v => v == null ? 0 : v.GetHashCode( StringComparison.Ordinal ),
                    v => v
                )
            );

            // TargetTags stored as nullable JSON string
            PropertyBuilder<string?> targetTagsProp = entity.Property( e => e.TargetTags );
            targetTagsProp.Metadata.SetValueComparer(
                new ValueComparer<string?>(
                    ( a, b ) => string.Equals( a, b, StringComparison.Ordinal ),
                    v => v == null ? 0 : v.GetHashCode( StringComparison.Ordinal ),
                    v => v
                )
            );
        } );

        // TriggerVersion — unique index on (TriggerId, VersionNumber), standalone TriggerId index, cascade delete from trigger
        _ = modelBuilder.Entity<TriggerVersion>( entity => {
            _ = entity.HasIndex( e => new { e.TriggerId, e.VersionNumber } )
                .IsUnique( );

            _ = entity.HasIndex( e => e.TriggerId );

            _ = entity.HasOne( e => e.Trigger )
                .WithMany( t => t.Versions )
                .HasForeignKey( e => e.TriggerId )
                .OnDelete( DeleteBehavior.Cascade );
        } );

        // FileMonitorTrigger.CurrentVersionId FK — SetNull to avoid circular cascade
        _ = modelBuilder.Entity<FileMonitorTrigger>( entity => {
            _ = entity.HasOne( e => e.CurrentVersion )
                .WithMany( )
                .HasForeignKey( e => e.CurrentVersionId )
                .OnDelete( DeleteBehavior.SetNull );

            _ = entity.HasOne( e => e.PinnedWorkflowVersion )
                .WithMany( )
                .HasForeignKey( e => e.PinnedWorkflowVersionId )
                .OnDelete( DeleteBehavior.SetNull );
        } );

        // Field-level encryption for sensitive columns (§9 Data Protection)
        if (FieldEncryption is not null) {
            EncryptedStringConverter encString = new( FieldEncryption );

            // WorkflowRunVariable.Value — runtime variable payloads (JSON)
            _ = modelBuilder.Entity<WorkflowRunVariable>( entity => {
                _ = entity.Property( e => e.Value ).HasConversion( encString );
            } );
        }
    }

    /// <inheritdoc/>
    protected override void ConfigureConventions( ModelConfigurationBuilder configurationBuilder ) {
        base.ConfigureConventions( configurationBuilder );

        // TimeZoneInfo ↔ string (by Id)
        _ = configurationBuilder.Properties<TimeZoneInfo>( )
            .HaveConversion<TimeZoneInfoStringConverter>( );

        // DateTime: EF Core + Npgsql maps to `timestamp with time zone` natively.
        // SQLite continues using TEXT (its only type affinity) with proper EF Core metadata.
        // No global converter needed.

        // RSAParameters ↔ string (JSON)
        _ = configurationBuilder.Properties<RSAParameters>( )
            .HaveConversion<RSAParametersStringConverter>( );

        // RegistrationStatus ↔ string
        _ = configurationBuilder.Properties<RegistrationStatus>( )
            .HaveConversion<RegistrationStatusStringConverter>( );

        // ConnectionStatus ↔ string
        _ = configurationBuilder.Properties<ConnectionStatus>( )
            .HaveConversion<ConnectionStatusStringConverter>( );

        // byte[] ↔ hex string (for BundleId, SharedKey)
        _ = configurationBuilder.Properties<byte[]>( )
            .HaveConversion<ByteArrayHexConverter>( );

        // TaskActionType ↔ string (Decision #45)
        _ = configurationBuilder.Properties<TaskActionType>( )
            .HaveConversion<TaskActionTypeStringConverter>( );

        // ErrorCategory ↔ string (Decision #45)
        _ = configurationBuilder.Properties<ErrorCategory>( )
            .HaveConversion<ErrorCategoryStringConverter>( );

        // ControlStatement ↔ string (Decision #45)
        _ = configurationBuilder.Properties<ControlStatement>( )
            .HaveConversion<ControlStatementStringConverter>( );

        // DependencyMode ↔ string (Decision #45)
        _ = configurationBuilder.Properties<DependencyMode>( )
            .HaveConversion<DependencyModeStringConverter>( );

        // WorkflowRunStatus ↔ string (Decision #45)
        _ = configurationBuilder.Properties<WorkflowRunStatus>( )
            .HaveConversion<WorkflowRunStatusStringConverter>( );

        // HolidayRuleType ↔ string
        _ = configurationBuilder.Properties<HolidayRuleType>( )
            .HaveConversion<HolidayRuleTypeStringConverter>( );

        // ObservanceRule ↔ string
        _ = configurationBuilder.Properties<ObservanceRule>( )
            .HaveConversion<ObservanceRuleStringConverter>( );

        // HolidayCalendarMode ↔ string
        _ = configurationBuilder.Properties<HolidayCalendarMode>( )
            .HaveConversion<HolidayCalendarModeStringConverter>( );

        // VariableSource ↔ string
        _ = configurationBuilder.Properties<VariableSource>( )
            .HaveConversion<VariableSourceStringConverter>( );

        // StepExecutionStatus ↔ string
        _ = configurationBuilder.Properties<Common.Models.StepExecutionStatus>( )
            .HaveConversion<StepExecutionStatusStringConverter>( );

        // CompositeType ↔ string
        _ = configurationBuilder.Properties<CompositeType>( )
            .HaveConversion<CompositeTypeStringConverter>( );

        // VersionBindingMode ↔ string
        _ = configurationBuilder.Properties<VersionBindingMode>( )
            .HaveConversion<VersionBindingModeStringConverter>( );

        // ActorType ↔ string (audit events)
        _ = configurationBuilder.Properties<ActorType>( )
            .HaveConversion<ActorTypeStringConverter>( );
    }

    /// <inheritdoc/>
    public override int SaveChanges( ) {
        UpdateConcurrencyBeforeSaving( );
        return base.SaveChanges( );
    }

    /// <inheritdoc/>
    public override int SaveChanges( bool acceptAllChangesOnSuccess ) {
        UpdateConcurrencyBeforeSaving( );
        return base.SaveChanges( acceptAllChangesOnSuccess );
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync( CancellationToken cancellationToken = default ) {
        UpdateConcurrencyBeforeSaving( );
        return base.SaveChangesAsync( cancellationToken );
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync( bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default ) {
        UpdateConcurrencyBeforeSaving( );
        return base.SaveChangesAsync( acceptAllChangesOnSuccess, cancellationToken );
    }

    private void UpdateConcurrencyBeforeSaving( ) {
        DateTime now = DateTime.UtcNow;

        foreach (EntityEntry<ConcurrencyBase> entry in ChangeTracker.Entries<Entities.ConcurrencyBase>( )) {
            if (entry.State == EntityState.Added) {
                entry.Entity.Created = now;
                entry.Entity.LastUpdated = now;
                entry.Entity.Version = 1;
            } else if (entry.State == EntityState.Modified) {
                entry.Entity.LastUpdated = now;
                entry.Entity.Version++;
            }
        }
    }

    // -- Value Converters --

    private sealed class TimeZoneInfoStringConverter( )
        : ValueConverter<TimeZoneInfo, string>(
            tz => tz.Id,
            id => TimeZoneInfo.FindSystemTimeZoneById( id ) );

    /// <summary>JSON options that include fields - required for <see cref="RSAParameters"/> which uses public fields, not properties.</summary>
    private static readonly JsonSerializerOptions s_rsaJsonOptions = new( ) { IncludeFields = true };

    private sealed class RSAParametersStringConverter( )
        : ValueConverter<RSAParameters, string>(
            rsa => JsonSerializer.Serialize( rsa, s_rsaJsonOptions ),
            json => JsonSerializer.Deserialize<RSAParameters>( json, s_rsaJsonOptions ) );

    private sealed class RegistrationStatusStringConverter( )
        : ValueConverter<RegistrationStatus, string>(
            status => status.ToString( ),
            str => Enum.Parse<RegistrationStatus>( str ) );

    private sealed class ConnectionStatusStringConverter( )
        : ValueConverter<ConnectionStatus, string>(
            status => status.ToString( ),
            str => Enum.Parse<ConnectionStatus>( str ) );

    private sealed class ByteArrayHexConverter( )
        : ValueConverter<byte[], string>(
            bytes => Convert.ToHexString( bytes ),
            hex => Convert.FromHexString( hex ) );

    private sealed class TaskActionTypeStringConverter( )
        : ValueConverter<TaskActionType, string>(
            v => v.ToString( ),
            v => Enum.Parse<TaskActionType>( v ) );

    private sealed class ErrorCategoryStringConverter( )
        : ValueConverter<ErrorCategory, string>(
            v => v.ToString( ),
            v => Enum.Parse<ErrorCategory>( v ) );

    private sealed class ControlStatementStringConverter( )
        : ValueConverter<ControlStatement, string>(
            v => v == ControlStatement.Default ? "Default" : v.ToString( ),
            v => ParseControlStatement( v ) ) {
        private static ControlStatement ParseControlStatement( string v ) =>
            v switch {
                "Sequential" or "Parallel" => ControlStatement.Default,
                "ConditionalIf" => ControlStatement.If,
                "ConditionalElseIf" => ControlStatement.ElseIf,
                "ConditionalElse" => ControlStatement.Else,
                "ConditionalWhile" => ControlStatement.While,
                "ConditionalDo" => ControlStatement.Do,
                _ => Enum.TryParse<ControlStatement>( v, ignoreCase: true, out ControlStatement parsed )
                    ? parsed
                    : ControlStatement.Default,
            };
    }

    private sealed class DependencyModeStringConverter( )
        : ValueConverter<DependencyMode, string>(
            v => v.ToString( ),
            v => Enum.Parse<DependencyMode>( v ) );

    private sealed class WorkflowRunStatusStringConverter( )
        : ValueConverter<WorkflowRunStatus, string>(
            v => v.ToString( ),
            v => Enum.Parse<WorkflowRunStatus>( v ) );

    private sealed class HolidayRuleTypeStringConverter( )
        : ValueConverter<HolidayRuleType, string>(
            v => v.ToString( ),
            v => Enum.Parse<HolidayRuleType>( v ) );

    private sealed class ObservanceRuleStringConverter( )
        : ValueConverter<ObservanceRule, string>(
            v => v.ToString( ),
            v => Enum.Parse<ObservanceRule>( v ) );

    private sealed class HolidayCalendarModeStringConverter( )
        : ValueConverter<HolidayCalendarMode, string>(
            v => v.ToString( ),
            v => Enum.Parse<HolidayCalendarMode>( v ) );

    private sealed class VariableSourceStringConverter( )
        : ValueConverter<VariableSource, string>(
            v => v.ToString( ),
            v => Enum.Parse<VariableSource>( v ) );

    private sealed class StepExecutionStatusStringConverter( )
        : ValueConverter<Common.Models.StepExecutionStatus, string>(
            v => v.ToString( ),
            v => Enum.Parse<Common.Models.StepExecutionStatus>( v ) );

    private sealed class CompositeTypeStringConverter( )
        : ValueConverter<CompositeType, string>(
            v => v.ToString( ),
            v => Enum.Parse<CompositeType>( v ) );

    private sealed class ActorTypeStringConverter( )
        : ValueConverter<ActorType, string>(
            v => v.ToString( ),
            v => Enum.Parse<ActorType>( v ) );

    private sealed class VersionBindingModeStringConverter( )
        : ValueConverter<VersionBindingMode, string>(
            v => v.ToString( ),
            v => Enum.Parse<VersionBindingMode>( v ) );
}
