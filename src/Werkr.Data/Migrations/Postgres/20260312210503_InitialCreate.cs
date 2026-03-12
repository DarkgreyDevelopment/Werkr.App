using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Werkr.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "werkr");

            migrationBuilder.CreateTable(
                name: "holiday_calendars",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    is_system_calendar = table.Column<bool>(type: "boolean", nullable: false),
                    created_utc = table.Column<string>(type: "text", nullable: false),
                    updated_utc = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_calendars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registered_connections",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    remote_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    local_public_key = table.Column<string>(type: "text", nullable: false),
                    local_private_key = table.Column<string>(type: "text", nullable: false),
                    remote_public_key = table.Column<string>(type: "text", nullable: false),
                    outbound_api_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    inbound_api_key_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    shared_key = table.Column<string>(type: "text", nullable: false),
                    previous_shared_key = table.Column<string>(type: "text", nullable: true),
                    active_key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    previous_key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_server = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    last_seen = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string>(type: "text", nullable: false),
                    allowed_paths = table.Column<string>(type: "text", nullable: false),
                    enforce_allowlist = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registered_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registration_bundles",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    server_public_key = table.Column<string>(type: "text", nullable: false),
                    server_private_key = table.Column<string>(type: "text", nullable: false),
                    bundle_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<string>(type: "text", nullable: false),
                    key_size = table.Column<int>(type: "integer", nullable: false),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    allowed_paths = table.Column<string>(type: "text", nullable: false),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registration_bundles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schedules",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    stop_task_after_minutes = table.Column<long>(type: "bigint", nullable: false),
                    catch_up_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflows",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    target_tags = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holiday_rules",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    holiday_calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    rule_type = table.Column<string>(type: "text", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: true),
                    day = table.Column<int>(type: "integer", nullable: true),
                    day_of_week = table.Column<int>(type: "integer", nullable: true),
                    week_number = table.Column<int>(type: "integer", nullable: true),
                    window_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    window_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    window_time_zone_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    observance_rule = table.Column<string>(type: "text", nullable: false),
                    year_start = table.Column<int>(type: "integer", nullable: true),
                    year_end = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_holiday_rules_holiday_calendars_holiday_calendar_id",
                        column: x => x.holiday_calendar_id,
                        principalSchema: "werkr",
                        principalTable: "holiday_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "daily_recurrence",
                schema: "werkr",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_interval = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_recurrence", x => x.schedule_id);
                    table.ForeignKey(
                        name: "fk_daily_recurrence_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monthly_recurrence",
                schema: "werkr",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_numbers = table.Column<string>(type: "text", nullable: true),
                    months_of_year = table.Column<int>(type: "integer", nullable: false),
                    week_number = table.Column<int>(type: "integer", nullable: true),
                    days_of_week = table.Column<int>(type: "integer", nullable: true),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monthly_recurrence", x => x.schedule_id);
                    table.ForeignKey(
                        name: "fk_monthly_recurrence_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_audit_log",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_utc_time = table.Column<string>(type: "text", nullable: false),
                    calendar_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    holiday_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    created_utc = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_schedule_audit_log_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_expiration",
                schema: "werkr",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_expiration", x => x.schedule_id);
                    table.ForeignKey(
                        name: "fk_schedule_expiration_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_holiday_calendars",
                schema: "werkr",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holiday_calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_holiday_calendars", x => new { x.schedule_id, x.holiday_calendar_id });
                    table.ForeignKey(
                        name: "fk_schedule_holiday_calendars_holiday_calendars_holiday_calend",
                        column: x => x.holiday_calendar_id,
                        principalSchema: "werkr",
                        principalTable: "holiday_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_schedule_holiday_calendars_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_repeat_options",
                schema: "werkr",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repeat_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    repeat_duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_repeat_options", x => x.schedule_id);
                    table.ForeignKey(
                        name: "fk_schedule_repeat_options_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "schedule_start_datetimeinfo",
                schema: "werkr",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_start_datetimeinfo", x => x.schedule_id);
                    table.ForeignKey(
                        name: "fk_schedule_start_datetimeinfo_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "weekly_recurrence",
                schema: "werkr",
                columns: table => new
                {
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_interval = table.Column<int>(type: "integer", nullable: false),
                    days_of_week = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weekly_recurrence", x => x.schedule_id);
                    table.ForeignKey(
                        name: "fk_weekly_recurrence_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    action_type = table.Column<string>(type: "text", nullable: false),
                    workflow_id = table.Column<long>(type: "bigint", nullable: true),
                    content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    arguments = table.Column<string>(type: "text", nullable: true),
                    target_tags = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_ephemeral = table.Column<bool>(type: "boolean", nullable: false),
                    timeout_minutes = table.Column<long>(type: "bigint", nullable: true),
                    sync_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    success_criteria = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    action_sub_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    action_parameters = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tasks_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalSchema: "werkr",
                        principalTable: "workflows",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "workflow_runs",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_id = table.Column<long>(type: "bigint", nullable: false),
                    start_time = table.Column<string>(type: "text", nullable: false),
                    end_time = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_runs_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalSchema: "werkr",
                        principalTable: "workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_schedules",
                schema: "werkr",
                columns: table => new
                {
                    workflow_id = table.Column<long>(type: "bigint", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<string>(type: "text", nullable: false),
                    is_one_time = table.Column<bool>(type: "boolean", nullable: false),
                    workflow_run_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_schedules", x => new { x.workflow_id, x.schedule_id });
                    table.ForeignKey(
                        name: "fk_workflow_schedules_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_schedules_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalSchema: "werkr",
                        principalTable: "workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_variables",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    default_value = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_variables", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_variables_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalSchema: "werkr",
                        principalTable: "workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "holiday_dates",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    holiday_calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holiday_rule_id = table.Column<long>(type: "bigint", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    window_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    window_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    window_time_zone_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_dates", x => x.id);
                    table.ForeignKey(
                        name: "fk_holiday_dates_holiday_calendars_holiday_calendar_id",
                        column: x => x.holiday_calendar_id,
                        principalSchema: "werkr",
                        principalTable: "holiday_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_holiday_dates_holiday_rules_holiday_rule_id",
                        column: x => x.holiday_rule_id,
                        principalSchema: "werkr",
                        principalTable: "holiday_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "task_schedules",
                schema: "werkr",
                columns: table => new
                {
                    task_id = table.Column<long>(type: "bigint", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<string>(type: "text", nullable: false),
                    is_one_time = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_schedules", x => new { x.task_id, x.schedule_id });
                    table.ForeignKey(
                        name: "fk_task_schedules_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_schedules_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "werkr",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_steps",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_id = table.Column<long>(type: "bigint", nullable: false),
                    task_id = table.Column<long>(type: "bigint", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    control_statement = table.Column<string>(type: "text", nullable: false),
                    condition_expression = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    max_iterations = table.Column<int>(type: "integer", nullable: false),
                    agent_connection_id_override = table.Column<Guid>(type: "uuid", nullable: true),
                    dependency_mode = table.Column<string>(type: "text", nullable: false),
                    input_variable_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    output_variable_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_steps_registered_connections_agent_connection_id_o",
                        column: x => x.agent_connection_id_override,
                        principalSchema: "werkr",
                        principalTable: "registered_connections",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_workflow_steps_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "werkr",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_steps_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalSchema: "werkr",
                        principalTable: "workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<long>(type: "bigint", nullable: false),
                    task_snapshot = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    runtime_seconds = table.Column<double>(type: "double precision", nullable: false),
                    start_time = table.Column<string>(type: "text", nullable: false),
                    end_time = table.Column<string>(type: "text", nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    agent_connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    exit_code = table.Column<int>(type: "integer", nullable: true),
                    error_category = table.Column<string>(type: "text", nullable: false),
                    output = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    output_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    workflow_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    step_id = table.Column<long>(type: "bigint", nullable: true),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_jobs_registered_connections_agent_connection_id",
                        column: x => x.agent_connection_id,
                        principalSchema: "werkr",
                        principalTable: "registered_connections",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_jobs_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalSchema: "werkr",
                        principalTable: "schedules",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_jobs_tasks_task_id",
                        column: x => x.task_id,
                        principalSchema: "werkr",
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_jobs_workflow_runs_workflow_run_id",
                        column: x => x.workflow_run_id,
                        principalSchema: "werkr",
                        principalTable: "workflow_runs",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_jobs_workflow_steps_step_id",
                        column: x => x.step_id,
                        principalSchema: "werkr",
                        principalTable: "workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "workflow_step_dependencies",
                schema: "werkr",
                columns: table => new
                {
                    step_id = table.Column<long>(type: "bigint", nullable: false),
                    depends_on_step_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_step_dependencies", x => new { x.step_id, x.depends_on_step_id });
                    table.ForeignKey(
                        name: "fk_workflow_step_dependencies_workflow_steps_depends_on_step_id",
                        column: x => x.depends_on_step_id,
                        principalSchema: "werkr",
                        principalTable: "workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_step_dependencies_workflow_steps_step_id",
                        column: x => x.step_id,
                        principalSchema: "werkr",
                        principalTable: "workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_run_variables",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variable_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    produced_by_step_id = table.Column<long>(type: "bigint", nullable: true),
                    produced_by_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    created = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_run_variables", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_run_variables_jobs_produced_by_job_id",
                        column: x => x.produced_by_job_id,
                        principalSchema: "werkr",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_workflow_run_variables_workflow_runs_workflow_run_id",
                        column: x => x.workflow_run_id,
                        principalSchema: "werkr",
                        principalTable: "workflow_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_run_variables_workflow_steps_produced_by_step_id",
                        column: x => x.produced_by_step_id,
                        principalSchema: "werkr",
                        principalTable: "workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "workflow_step_executions",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<long>(type: "bigint", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    start_time = table.Column<string>(type: "text", nullable: true),
                    end_time = table.Column<string>(type: "text", nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    skip_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created = table.Column<string>(type: "text", nullable: false),
                    last_updated = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_step_executions", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_step_executions_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "werkr",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_workflow_step_executions_workflow_runs_workflow_run_id",
                        column: x => x.workflow_run_id,
                        principalSchema: "werkr",
                        principalTable: "workflow_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_step_executions_workflow_steps_step_id",
                        column: x => x.step_id,
                        principalSchema: "werkr",
                        principalTable: "workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendars_name",
                schema: "werkr",
                table: "holiday_calendars",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holiday_dates_holiday_calendar_id_date",
                schema: "werkr",
                table: "holiday_dates",
                columns: new[] { "holiday_calendar_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holiday_dates_holiday_rule_id",
                schema: "werkr",
                table: "holiday_dates",
                column: "holiday_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_holiday_rules_holiday_calendar_id",
                schema: "werkr",
                table: "holiday_rules",
                column: "holiday_calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_agent_connection_id",
                schema: "werkr",
                table: "jobs",
                column: "agent_connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_schedule_id",
                schema: "werkr",
                table: "jobs",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_step_id",
                schema: "werkr",
                table: "jobs",
                column: "step_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_task_id",
                schema: "werkr",
                table: "jobs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_WorkflowRunId_StepId",
                schema: "werkr",
                table: "jobs",
                columns: new[] { "workflow_run_id", "step_id" });

            migrationBuilder.CreateIndex(
                name: "ix_registered_connections_connection_name",
                schema: "werkr",
                table: "registered_connections",
                column: "connection_name");

            migrationBuilder.CreateIndex(
                name: "ix_registered_connections_remote_url",
                schema: "werkr",
                table: "registered_connections",
                column: "remote_url");

            migrationBuilder.CreateIndex(
                name: "ix_registration_bundles_bundle_id",
                schema: "werkr",
                table: "registration_bundles",
                column: "bundle_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_schedule_audit_log_schedule_id_occurrence_utc_time",
                schema: "werkr",
                table: "schedule_audit_log",
                columns: new[] { "schedule_id", "occurrence_utc_time" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_holiday_calendars_holiday_calendar_id",
                schema: "werkr",
                table: "schedule_holiday_calendars",
                column: "holiday_calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_holiday_calendars_schedule_id",
                schema: "werkr",
                table: "schedule_holiday_calendars",
                column: "schedule_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_schedules_schedule_id",
                schema: "werkr",
                table: "task_schedules",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_workflow_id",
                schema: "werkr",
                table: "tasks",
                column: "workflow_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_run_variables_produced_by_job_id",
                schema: "werkr",
                table: "workflow_run_variables",
                column: "produced_by_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_run_variables_produced_by_step_id",
                schema: "werkr",
                table: "workflow_run_variables",
                column: "produced_by_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_run_variables_workflow_run_id_variable_name_version",
                schema: "werkr",
                table: "workflow_run_variables",
                columns: new[] { "workflow_run_id", "variable_name", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_workflow_id",
                schema: "werkr",
                table: "workflow_runs",
                column: "workflow_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_schedules_schedule_id",
                schema: "werkr",
                table: "workflow_schedules",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_dependencies_depends_on_step_id",
                schema: "werkr",
                table: "workflow_step_dependencies",
                column: "depends_on_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_executions_job_id",
                schema: "werkr",
                table: "workflow_step_executions",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_executions_step_id",
                schema: "werkr",
                table: "workflow_step_executions",
                column: "step_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_executions_workflow_run_id",
                schema: "werkr",
                table: "workflow_step_executions",
                column: "workflow_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_executions_workflow_run_id_step_id_attempt",
                schema: "werkr",
                table: "workflow_step_executions",
                columns: new[] { "workflow_run_id", "step_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_agent_connection_id_override",
                schema: "werkr",
                table: "workflow_steps",
                column: "agent_connection_id_override");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_task_id",
                schema: "werkr",
                table: "workflow_steps",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_workflow_id",
                schema: "werkr",
                table: "workflow_steps",
                column: "workflow_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_variables_workflow_id_name",
                schema: "werkr",
                table: "workflow_variables",
                columns: new[] { "workflow_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_recurrence",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "holiday_dates",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "monthly_recurrence",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "registration_bundles",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "schedule_audit_log",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "schedule_expiration",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "schedule_holiday_calendars",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "schedule_repeat_options",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "schedule_start_datetimeinfo",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "task_schedules",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "weekly_recurrence",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "workflow_run_variables",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "workflow_schedules",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "workflow_step_dependencies",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "workflow_step_executions",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "workflow_variables",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "holiday_rules",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "holiday_calendars",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "schedules",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "workflow_runs",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "workflow_steps",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "registered_connections",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "werkr");

            migrationBuilder.DropTable(
                name: "workflows",
                schema: "werkr");
        }
    }
}
