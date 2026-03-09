using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Werkr.Data.Migrations.Sqlite;

/// <inheritdoc />
public partial class InitialCreate : Migration {
    /// <inheritdoc />
    protected override void Up( MigrationBuilder migrationBuilder ) {
        _ = migrationBuilder.CreateTable(
            name: "holiday_calendars",
            columns: table => new {
                id = table.Column<Guid>( type: "TEXT", nullable: false ),
                name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                description = table.Column<string>( type: "TEXT", maxLength: 1024, nullable: false ),
                is_system_calendar = table.Column<bool>( type: "INTEGER", nullable: false ),
                created_utc = table.Column<string>( type: "TEXT", nullable: false ),
                updated_utc = table.Column<string>( type: "TEXT", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_holiday_calendars", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "registered_connections",
            columns: table => new {
                id = table.Column<Guid>( type: "TEXT", nullable: false ),
                connection_name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                remote_url = table.Column<string>( type: "TEXT", maxLength: 2048, nullable: false ),
                local_public_key = table.Column<string>( type: "TEXT", nullable: false ),
                local_private_key = table.Column<string>( type: "TEXT", nullable: false ),
                remote_public_key = table.Column<string>( type: "TEXT", nullable: false ),
                outbound_api_key = table.Column<string>( type: "TEXT", maxLength: 512, nullable: false ),
                inbound_api_key_hash = table.Column<string>( type: "TEXT", maxLength: 512, nullable: false ),
                shared_key = table.Column<string>( type: "TEXT", nullable: false ),
                previous_shared_key = table.Column<string>( type: "TEXT", nullable: true ),
                active_key_id = table.Column<string>( type: "TEXT", maxLength: 128, nullable: true ),
                previous_key_id = table.Column<string>( type: "TEXT", maxLength: 128, nullable: true ),
                is_server = table.Column<bool>( type: "INTEGER", nullable: false ),
                status = table.Column<string>( type: "TEXT", nullable: false ),
                last_seen = table.Column<string>( type: "TEXT", nullable: true ),
                tags = table.Column<string>( type: "TEXT", nullable: false ),
                allowed_paths = table.Column<string>( type: "TEXT", nullable: false ),
                enforce_allowlist = table.Column<bool>( type: "INTEGER", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_registered_connections", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "registration_bundles",
            columns: table => new {
                id = table.Column<Guid>( type: "TEXT", nullable: false ),
                connection_name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                server_public_key = table.Column<string>( type: "TEXT", nullable: false ),
                server_private_key = table.Column<string>( type: "TEXT", nullable: false ),
                bundle_id = table.Column<string>( type: "TEXT", nullable: false ),
                status = table.Column<string>( type: "TEXT", nullable: false ),
                expires_at = table.Column<string>( type: "TEXT", nullable: false ),
                key_size = table.Column<int>( type: "INTEGER", nullable: false ),
                tags = table.Column<string>( type: "TEXT", nullable: false ),
                allowed_paths = table.Column<string>( type: "TEXT", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_registration_bundles", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedules",
            columns: table => new {
                id = table.Column<Guid>( type: "TEXT", nullable: false ),
                name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                stop_task_after_minutes = table.Column<long>( type: "INTEGER", nullable: false ),
                catch_up_enabled = table.Column<bool>( type: "INTEGER", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedules", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflows",
            columns: table => new {
                id = table.Column<long>( type: "INTEGER", nullable: false )
                    .Annotation( "Sqlite:Autoincrement", true ),
                name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                description = table.Column<string>( type: "TEXT", maxLength: 2000, nullable: false ),
                enabled = table.Column<bool>( type: "INTEGER", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflows", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "holiday_rules",
            columns: table => new {
                id = table.Column<long>( type: "INTEGER", nullable: false )
                    .Annotation( "Sqlite:Autoincrement", true ),
                holiday_calendar_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                rule_type = table.Column<string>( type: "TEXT", nullable: false ),
                month = table.Column<int>( type: "INTEGER", nullable: true ),
                day = table.Column<int>( type: "INTEGER", nullable: true ),
                day_of_week = table.Column<int>( type: "INTEGER", nullable: true ),
                week_number = table.Column<int>( type: "INTEGER", nullable: true ),
                window_start = table.Column<TimeOnly>( type: "TEXT", nullable: true ),
                window_end = table.Column<TimeOnly>( type: "TEXT", nullable: true ),
                window_time_zone_id = table.Column<string>( type: "TEXT", maxLength: 128, nullable: true ),
                observance_rule = table.Column<string>( type: "TEXT", nullable: false ),
                year_start = table.Column<int>( type: "INTEGER", nullable: true ),
                year_end = table.Column<int>( type: "INTEGER", nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_holiday_rules", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_holiday_rules_holiday_calendars_holiday_calendar_id",
                    column: x => x.holiday_calendar_id,
                    principalTable: "holiday_calendars",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "daily_recurrence",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                day_interval = table.Column<int>( type: "INTEGER", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_daily_recurrence", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_daily_recurrence_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "monthly_recurrence",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                day_numbers = table.Column<string>( type: "TEXT", nullable: true ),
                months_of_year = table.Column<int>( type: "INTEGER", nullable: false ),
                week_number = table.Column<int>( type: "INTEGER", nullable: true ),
                days_of_week = table.Column<int>( type: "INTEGER", nullable: true ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_monthly_recurrence", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_monthly_recurrence_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedule_audit_log",
            columns: table => new {
                id = table.Column<long>( type: "INTEGER", nullable: false )
                    .Annotation( "Sqlite:Autoincrement", true ),
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                occurrence_utc_time = table.Column<string>( type: "TEXT", nullable: false ),
                calendar_name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                holiday_name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                mode = table.Column<string>( type: "TEXT", nullable: false ),
                created_utc = table.Column<string>( type: "TEXT", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedule_audit_log", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_schedule_audit_log_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedule_expiration",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false ),
                date = table.Column<DateOnly>( type: "TEXT", nullable: false ),
                time = table.Column<TimeOnly>( type: "TEXT", nullable: false ),
                time_zone = table.Column<string>( type: "TEXT", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedule_expiration", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_schedule_expiration_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedule_holiday_calendars",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                holiday_calendar_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                mode = table.Column<string>( type: "TEXT", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedule_holiday_calendars", x => new { x.schedule_id, x.holiday_calendar_id } );
                _ = table.ForeignKey(
                    name: "fk_schedule_holiday_calendars_holiday_calendars_holiday_calendar_id",
                    column: x => x.holiday_calendar_id,
                    principalTable: "holiday_calendars",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
                _ = table.ForeignKey(
                    name: "fk_schedule_holiday_calendars_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedule_repeat_options",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                repeat_interval_minutes = table.Column<int>( type: "INTEGER", nullable: false ),
                repeat_duration_minutes = table.Column<int>( type: "INTEGER", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedule_repeat_options", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_schedule_repeat_options_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedule_start_datetimeinfo",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false ),
                date = table.Column<DateOnly>( type: "TEXT", nullable: false ),
                time = table.Column<TimeOnly>( type: "TEXT", nullable: false ),
                time_zone = table.Column<string>( type: "TEXT", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedule_start_datetimeinfo", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_schedule_start_datetimeinfo_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "weekly_recurrence",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                week_interval = table.Column<int>( type: "INTEGER", nullable: false ),
                days_of_week = table.Column<int>( type: "INTEGER", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_weekly_recurrence", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_weekly_recurrence_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "tasks",
            columns: table => new {
                id = table.Column<long>( type: "INTEGER", nullable: false )
                    .Annotation( "Sqlite:Autoincrement", true ),
                name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                description = table.Column<string>( type: "TEXT", maxLength: 2000, nullable: false ),
                action_type = table.Column<string>( type: "TEXT", nullable: false ),
                workflow_id = table.Column<long>( type: "INTEGER", nullable: true ),
                content = table.Column<string>( type: "TEXT", maxLength: 8000, nullable: false ),
                arguments = table.Column<string>( type: "TEXT", nullable: true ),
                target_tags = table.Column<string>( type: "TEXT", nullable: false ),
                enabled = table.Column<bool>( type: "INTEGER", nullable: false ),
                is_ephemeral = table.Column<bool>( type: "INTEGER", nullable: false ),
                timeout_minutes = table.Column<long>( type: "INTEGER", nullable: true ),
                sync_interval_minutes = table.Column<int>( type: "INTEGER", nullable: false ),
                success_criteria = table.Column<string>( type: "TEXT", maxLength: 500, nullable: true ),
                action_sub_type = table.Column<string>( type: "TEXT", maxLength: 30, nullable: true ),
                action_parameters = table.Column<string>( type: "TEXT", nullable: true ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_tasks", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_tasks_workflows_workflow_id",
                    column: x => x.workflow_id,
                    principalTable: "workflows",
                    principalColumn: "id" );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_runs",
            columns: table => new {
                id = table.Column<Guid>( type: "TEXT", nullable: false ),
                workflow_id = table.Column<long>( type: "INTEGER", nullable: false ),
                start_time = table.Column<string>( type: "TEXT", nullable: false ),
                end_time = table.Column<string>( type: "TEXT", nullable: true ),
                status = table.Column<string>( type: "TEXT", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_runs", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_workflow_runs_workflows_workflow_id",
                    column: x => x.workflow_id,
                    principalTable: "workflows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_schedules",
            columns: table => new {
                workflow_id = table.Column<long>( type: "INTEGER", nullable: false ),
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                created_at_utc = table.Column<string>( type: "TEXT", nullable: false ),
                is_one_time = table.Column<bool>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_schedules", x => new { x.workflow_id, x.schedule_id } );
                _ = table.ForeignKey(
                    name: "fk_workflow_schedules_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
                _ = table.ForeignKey(
                    name: "fk_workflow_schedules_workflows_workflow_id",
                    column: x => x.workflow_id,
                    principalTable: "workflows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "holiday_dates",
            columns: table => new {
                id = table.Column<long>( type: "INTEGER", nullable: false )
                    .Annotation( "Sqlite:Autoincrement", true ),
                holiday_calendar_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                holiday_rule_id = table.Column<long>( type: "INTEGER", nullable: true ),
                date = table.Column<DateOnly>( type: "TEXT", nullable: false ),
                name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                year = table.Column<int>( type: "INTEGER", nullable: false ),
                window_start = table.Column<TimeOnly>( type: "TEXT", nullable: true ),
                window_end = table.Column<TimeOnly>( type: "TEXT", nullable: true ),
                window_time_zone_id = table.Column<string>( type: "TEXT", maxLength: 128, nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_holiday_dates", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_holiday_dates_holiday_calendars_holiday_calendar_id",
                    column: x => x.holiday_calendar_id,
                    principalTable: "holiday_calendars",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
                _ = table.ForeignKey(
                    name: "fk_holiday_dates_holiday_rules_holiday_rule_id",
                    column: x => x.holiday_rule_id,
                    principalTable: "holiday_rules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull );
            } );

        _ = migrationBuilder.CreateTable(
            name: "task_schedules",
            columns: table => new {
                task_id = table.Column<long>( type: "INTEGER", nullable: false ),
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: false ),
                created_at_utc = table.Column<string>( type: "TEXT", nullable: false ),
                is_one_time = table.Column<bool>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_task_schedules", x => new { x.task_id, x.schedule_id } );
                _ = table.ForeignKey(
                    name: "fk_task_schedules_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
                _ = table.ForeignKey(
                    name: "fk_task_schedules_tasks_task_id",
                    column: x => x.task_id,
                    principalTable: "tasks",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_steps",
            columns: table => new {
                id = table.Column<long>( type: "INTEGER", nullable: false )
                    .Annotation( "Sqlite:Autoincrement", true ),
                workflow_id = table.Column<long>( type: "INTEGER", nullable: false ),
                task_id = table.Column<long>( type: "INTEGER", nullable: false ),
                order = table.Column<int>( type: "INTEGER", nullable: false ),
                control_statement = table.Column<string>( type: "TEXT", nullable: false ),
                condition_expression = table.Column<string>( type: "TEXT", maxLength: 2000, nullable: true ),
                max_iterations = table.Column<int>( type: "INTEGER", nullable: false ),
                agent_connection_id_override = table.Column<Guid>( type: "TEXT", nullable: true ),
                dependency_mode = table.Column<string>( type: "TEXT", nullable: false ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_steps", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_workflow_steps_registered_connections_agent_connection_id_override",
                    column: x => x.agent_connection_id_override,
                    principalTable: "registered_connections",
                    principalColumn: "id" );
                _ = table.ForeignKey(
                    name: "fk_workflow_steps_tasks_task_id",
                    column: x => x.task_id,
                    principalTable: "tasks",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
                _ = table.ForeignKey(
                    name: "fk_workflow_steps_workflows_workflow_id",
                    column: x => x.workflow_id,
                    principalTable: "workflows",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "jobs",
            columns: table => new {
                id = table.Column<Guid>( type: "TEXT", nullable: false ),
                task_id = table.Column<long>( type: "INTEGER", nullable: false ),
                task_snapshot = table.Column<string>( type: "TEXT", maxLength: 8000, nullable: false ),
                runtime_seconds = table.Column<double>( type: "REAL", nullable: false ),
                start_time = table.Column<string>( type: "TEXT", nullable: false ),
                end_time = table.Column<string>( type: "TEXT", nullable: true ),
                success = table.Column<bool>( type: "INTEGER", nullable: false ),
                agent_connection_id = table.Column<Guid>( type: "TEXT", nullable: true ),
                exit_code = table.Column<int>( type: "INTEGER", nullable: true ),
                error_category = table.Column<string>( type: "TEXT", nullable: false ),
                output = table.Column<string>( type: "TEXT", maxLength: 2000, nullable: true ),
                output_path = table.Column<string>( type: "TEXT", maxLength: 512, nullable: true ),
                workflow_run_id = table.Column<Guid>( type: "TEXT", nullable: true ),
                schedule_id = table.Column<Guid>( type: "TEXT", nullable: true ),
                created = table.Column<string>( type: "TEXT", nullable: false ),
                last_updated = table.Column<string>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_jobs", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_jobs_registered_connections_agent_connection_id",
                    column: x => x.agent_connection_id,
                    principalTable: "registered_connections",
                    principalColumn: "id" );
                _ = table.ForeignKey(
                    name: "fk_jobs_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalTable: "schedules",
                    principalColumn: "id" );
                _ = table.ForeignKey(
                    name: "fk_jobs_tasks_task_id",
                    column: x => x.task_id,
                    principalTable: "tasks",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
                _ = table.ForeignKey(
                    name: "fk_jobs_workflow_runs_workflow_run_id",
                    column: x => x.workflow_run_id,
                    principalTable: "workflow_runs",
                    principalColumn: "id" );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_step_dependencies",
            columns: table => new {
                step_id = table.Column<long>( type: "INTEGER", nullable: false ),
                depends_on_step_id = table.Column<long>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_step_dependencies", x => new { x.step_id, x.depends_on_step_id } );
                _ = table.ForeignKey(
                    name: "fk_workflow_step_dependencies_workflow_steps_depends_on_step_id",
                    column: x => x.depends_on_step_id,
                    principalTable: "workflow_steps",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict );
                _ = table.ForeignKey(
                    name: "fk_workflow_step_dependencies_workflow_steps_step_id",
                    column: x => x.step_id,
                    principalTable: "workflow_steps",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_holiday_calendars_name",
            table: "holiday_calendars",
            column: "name",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_holiday_dates_holiday_calendar_id_date",
            table: "holiday_dates",
            columns: new[] { "holiday_calendar_id", "date" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_holiday_dates_holiday_rule_id",
            table: "holiday_dates",
            column: "holiday_rule_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_holiday_rules_holiday_calendar_id",
            table: "holiday_rules",
            column: "holiday_calendar_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_jobs_agent_connection_id",
            table: "jobs",
            column: "agent_connection_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_jobs_schedule_id",
            table: "jobs",
            column: "schedule_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_jobs_task_id",
            table: "jobs",
            column: "task_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_jobs_workflow_run_id",
            table: "jobs",
            column: "workflow_run_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_registered_connections_connection_name",
            table: "registered_connections",
            column: "connection_name" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_registered_connections_remote_url",
            table: "registered_connections",
            column: "remote_url" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_registration_bundles_bundle_id",
            table: "registration_bundles",
            column: "bundle_id",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_schedule_audit_log_schedule_id_occurrence_utc_time",
            table: "schedule_audit_log",
            columns: new[] { "schedule_id", "occurrence_utc_time" } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_schedule_holiday_calendars_holiday_calendar_id",
            table: "schedule_holiday_calendars",
            column: "holiday_calendar_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_schedule_holiday_calendars_schedule_id",
            table: "schedule_holiday_calendars",
            column: "schedule_id",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_task_schedules_schedule_id",
            table: "task_schedules",
            column: "schedule_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_tasks_workflow_id",
            table: "tasks",
            column: "workflow_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_runs_workflow_id",
            table: "workflow_runs",
            column: "workflow_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_schedules_schedule_id",
            table: "workflow_schedules",
            column: "schedule_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_step_dependencies_depends_on_step_id",
            table: "workflow_step_dependencies",
            column: "depends_on_step_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_steps_agent_connection_id_override",
            table: "workflow_steps",
            column: "agent_connection_id_override" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_steps_task_id",
            table: "workflow_steps",
            column: "task_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_steps_workflow_id",
            table: "workflow_steps",
            column: "workflow_id" );
    }

    /// <inheritdoc />
    protected override void Down( MigrationBuilder migrationBuilder ) {
        _ = migrationBuilder.DropTable(
            name: "daily_recurrence" );

        _ = migrationBuilder.DropTable(
            name: "holiday_dates" );

        _ = migrationBuilder.DropTable(
            name: "jobs" );

        _ = migrationBuilder.DropTable(
            name: "monthly_recurrence" );

        _ = migrationBuilder.DropTable(
            name: "registration_bundles" );

        _ = migrationBuilder.DropTable(
            name: "schedule_audit_log" );

        _ = migrationBuilder.DropTable(
            name: "schedule_expiration" );

        _ = migrationBuilder.DropTable(
            name: "schedule_holiday_calendars" );

        _ = migrationBuilder.DropTable(
            name: "schedule_repeat_options" );

        _ = migrationBuilder.DropTable(
            name: "schedule_start_datetimeinfo" );

        _ = migrationBuilder.DropTable(
            name: "task_schedules" );

        _ = migrationBuilder.DropTable(
            name: "weekly_recurrence" );

        _ = migrationBuilder.DropTable(
            name: "workflow_schedules" );

        _ = migrationBuilder.DropTable(
            name: "workflow_step_dependencies" );

        _ = migrationBuilder.DropTable(
            name: "holiday_rules" );

        _ = migrationBuilder.DropTable(
            name: "workflow_runs" );

        _ = migrationBuilder.DropTable(
            name: "schedules" );

        _ = migrationBuilder.DropTable(
            name: "workflow_steps" );

        _ = migrationBuilder.DropTable(
            name: "holiday_calendars" );

        _ = migrationBuilder.DropTable(
            name: "registered_connections" );

        _ = migrationBuilder.DropTable(
            name: "tasks" );

        _ = migrationBuilder.DropTable(
            name: "workflows" );
    }
}
