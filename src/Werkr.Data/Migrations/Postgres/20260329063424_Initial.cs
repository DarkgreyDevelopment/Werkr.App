using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Werkr.Data.Migrations.Postgres; 
/// <inheritdoc />
public partial class Initial : Migration {
    /// <inheritdoc />
    protected override void Up( MigrationBuilder migrationBuilder ) {
        _ = migrationBuilder.EnsureSchema(
            name: "werkr" );

        _ = migrationBuilder.CreateTable(
            name: "audit_events",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn ),
                event_type_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                event_category = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: false ),
                source_module = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: false ),
                actor_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                actor_type = table.Column<string>( type: "text", nullable: false ),
                entity_type = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: true ),
                entity_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                action_performed = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: false ),
                details = table.Column<string>( type: "character varying(8192)", maxLength: 8192, nullable: false ),
                timestamp_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                correlation_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_audit_events", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "configuration_entries",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                key = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                value = table.Column<string>( type: "text", nullable: false ),
                value_type = table.Column<string>( type: "character varying(32)", maxLength: 32, nullable: false ),
                category = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: false ),
                description = table.Column<string>( type: "character varying(500)", maxLength: 500, nullable: true ),
                scope_level = table.Column<int>( type: "integer", nullable: false ),
                scope_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                sync_version = table.Column<long>( type: "bigint", nullable: false ),
                validation_rules = table.Column<string>( type: "text", nullable: true ),
                default_value = table.Column<string>( type: "text", nullable: false ),
                created_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                modified_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                modified_by_user_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_configuration_entries", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "credentials",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                name = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                type = table.Column<string>( type: "text", nullable: false ),
                encrypted_value = table.Column<string>( type: "text", nullable: false ),
                description = table.Column<string>( type: "character varying(500)", maxLength: 500, nullable: true ),
                created_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                modified_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                created_by_user_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                modified_by_user_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_credentials", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "holiday_calendars",
            schema: "werkr",
            columns: table => new {
                id = table.Column<Guid>( type: "uuid", nullable: false ),
                name = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                description = table.Column<string>( type: "character varying(1024)", maxLength: 1024, nullable: false ),
                is_system_calendar = table.Column<bool>( type: "boolean", nullable: false ),
                created_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                updated_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                working_days = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_holiday_calendars", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "notification_channels",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                name = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                channel_type = table.Column<string>( type: "character varying(32)", maxLength: 32, nullable: false ),
                configuration = table.Column<string>( type: "text", nullable: false ),
                is_enabled = table.Column<bool>( type: "boolean", nullable: false ),
                created_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                modified_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_notification_channels", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "notification_templates",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                event_type_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                channel_type = table.Column<string>( type: "character varying(32)", maxLength: 32, nullable: false ),
                subject = table.Column<string>( type: "character varying(500)", maxLength: 500, nullable: true ),
                body = table.Column<string>( type: "character varying(8000)", maxLength: 8000, nullable: false ),
                is_default = table.Column<bool>( type: "boolean", nullable: false ),
                created_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                modified_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_notification_templates", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "registered_connections",
            schema: "werkr",
            columns: table => new {
                id = table.Column<Guid>( type: "uuid", nullable: false ),
                connection_name = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                remote_url = table.Column<string>( type: "character varying(2048)", maxLength: 2048, nullable: false ),
                local_public_key = table.Column<string>( type: "text", nullable: false ),
                local_private_key = table.Column<string>( type: "text", nullable: false ),
                remote_public_key = table.Column<string>( type: "text", nullable: false ),
                outbound_api_key = table.Column<string>( type: "character varying(512)", maxLength: 512, nullable: false ),
                inbound_api_key_hash = table.Column<string>( type: "character varying(512)", maxLength: 512, nullable: false ),
                shared_key = table.Column<string>( type: "text", nullable: false ),
                previous_shared_key = table.Column<string>( type: "text", nullable: true ),
                active_key_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                previous_key_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                key_rotated_at_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: true ),
                is_server = table.Column<bool>( type: "boolean", nullable: false ),
                status = table.Column<string>( type: "text", nullable: false ),
                last_seen = table.Column<DateTime>( type: "timestamp with time zone", nullable: true ),
                tags = table.Column<string>( type: "text", nullable: false ),
                allowed_paths = table.Column<string>( type: "text", nullable: false ),
                enforce_allowlist = table.Column<bool>( type: "boolean", nullable: false ),
                agent_version = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                pending_shared_key = table.Column<string>( type: "text", nullable: true ),
                pending_key_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_registered_connections", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "registration_bundles",
            schema: "werkr",
            columns: table => new {
                id = table.Column<Guid>( type: "uuid", nullable: false ),
                connection_name = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                server_public_key = table.Column<string>( type: "text", nullable: false ),
                server_private_key = table.Column<string>( type: "text", nullable: false ),
                bundle_id = table.Column<string>( type: "text", nullable: false ),
                status = table.Column<string>( type: "text", nullable: false ),
                expires_at = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                key_size = table.Column<int>( type: "integer", nullable: false ),
                tags = table.Column<string[]>( type: "text[]", nullable: false ),
                allowed_paths = table.Column<string>( type: "text", nullable: false ),
                registration_key = table.Column<string>( type: "text", nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_registration_bundles", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "retention_policies",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                entity_type = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: false ),
                retention_days = table.Column<int>( type: "integer", nullable: false ),
                is_enabled = table.Column<bool>( type: "boolean", nullable: false ),
                modified_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                modified_by_user_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_retention_policies", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "saved_filters",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                owner_id = table.Column<string>( type: "character varying(450)", maxLength: 450, nullable: false ),
                page_key = table.Column<string>( type: "character varying(50)", maxLength: 50, nullable: false ),
                name = table.Column<string>( type: "character varying(200)", maxLength: 200, nullable: false ),
                criteria_json = table.Column<string>( type: "character varying(4096)", maxLength: 4096, nullable: false ),
                is_shared = table.Column<bool>( type: "boolean", nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_saved_filters", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedules",
            schema: "werkr",
            columns: table => new {
                id = table.Column<Guid>( type: "uuid", nullable: false ),
                name = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                stop_task_after_minutes = table.Column<long>( type: "bigint", nullable: false ),
                catch_up_enabled = table.Column<bool>( type: "boolean", nullable: false ),
                shift_mode = table.Column<int>( type: "integer", nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedules", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "user_notification_preferences",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                user_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                event_category_id = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: false ),
                channel_type = table.Column<string>( type: "character varying(32)", maxLength: 32, nullable: false ),
                is_enabled = table.Column<bool>( type: "boolean", nullable: false ),
                quiet_hours_start = table.Column<TimeOnly>( type: "time without time zone", nullable: true ),
                quiet_hours_end = table.Column<TimeOnly>( type: "time without time zone", nullable: true ),
                quiet_hours_timezone = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_user_notification_preferences", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "user_notifications",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                user_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                event_type_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                event_category_id = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: false ),
                title = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                body = table.Column<string>( type: "character varying(2000)", maxLength: 2000, nullable: false ),
                is_read = table.Column<bool>( type: "boolean", nullable: false ),
                created_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                read_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: true ),
                link = table.Column<string>( type: "character varying(512)", maxLength: 512, nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_user_notifications", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "user_preferences",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                user_id = table.Column<string>( type: "character varying(450)", maxLength: 450, nullable: false ),
                key = table.Column<string>( type: "character varying(100)", maxLength: 100, nullable: false ),
                value = table.Column<string>( type: "character varying(500)", maxLength: 500, nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_user_preferences", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "configuration_change_logs",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                configuration_entry_id = table.Column<long>( type: "bigint", nullable: false ),
                key = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                previous_value = table.Column<string>( type: "text", nullable: true ),
                new_value = table.Column<string>( type: "text", nullable: false ),
                changed_by_user_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                changed_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_configuration_change_logs", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_configuration_change_logs_configuration_entries_configurati",
                    column: x => x.configuration_entry_id,
                    principalSchema: "werkr",
                    principalTable: "configuration_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "holiday_rules",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn ),
                holiday_calendar_id = table.Column<Guid>( type: "uuid", nullable: false ),
                name = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                rule_type = table.Column<string>( type: "text", nullable: false ),
                month = table.Column<int>( type: "integer", nullable: true ),
                day = table.Column<int>( type: "integer", nullable: true ),
                day_of_week = table.Column<int>( type: "integer", nullable: true ),
                week_number = table.Column<int>( type: "integer", nullable: true ),
                window_start = table.Column<TimeOnly>( type: "time without time zone", nullable: true ),
                window_end = table.Column<TimeOnly>( type: "time without time zone", nullable: true ),
                window_time_zone_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                observance_rule = table.Column<string>( type: "text", nullable: false ),
                year_start = table.Column<int>( type: "integer", nullable: true ),
                year_end = table.Column<int>( type: "integer", nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_holiday_rules", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_holiday_rules_holiday_calendars_holiday_calendar_id",
                    column: x => x.holiday_calendar_id,
                    principalSchema: "werkr",
                    principalTable: "holiday_calendars",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "notification_deliveries",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                channel_id = table.Column<long>( type: "bigint", nullable: false ),
                event_type_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                recipient_id = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                status = table.Column<int>( type: "integer", nullable: false ),
                attempt_count = table.Column<int>( type: "integer", nullable: false ),
                max_attempts = table.Column<int>( type: "integer", nullable: false ),
                last_attempt_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: true ),
                next_retry_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: true ),
                error_message = table.Column<string>( type: "character varying(2000)", maxLength: 2000, nullable: true ),
                payload_json = table.Column<string>( type: "text", nullable: false ),
                created_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                completed_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_notification_deliveries", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_notification_deliveries_notification_channels_channel_id",
                    column: x => x.channel_id,
                    principalSchema: "werkr",
                    principalTable: "notification_channels",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "credential_agent_scopes",
            schema: "werkr",
            columns: table => new {
                credential_id = table.Column<long>( type: "bigint", nullable: false ),
                agent_connection_id = table.Column<Guid>( type: "uuid", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_credential_agent_scopes", x => new { x.credential_id, x.agent_connection_id } );
                _ = table.ForeignKey(
                    name: "fk_credential_agent_scopes_credentials_credential_id",
                    column: x => x.credential_id,
                    principalSchema: "werkr",
                    principalTable: "credentials",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
                _ = table.ForeignKey(
                    name: "fk_credential_agent_scopes_registered_connections_agent_connec",
                    column: x => x.agent_connection_id,
                    principalSchema: "werkr",
                    principalTable: "registered_connections",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "pending_agent_notifications",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                connection_id = table.Column<Guid>( type: "uuid", nullable: false ),
                channel = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: false ),
                payload = table.Column<string>( type: "character varying(2000)", maxLength: 2000, nullable: true ),
                created_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                expires_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_pending_agent_notifications", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_pending_agent_notifications_registered_connections_connecti",
                    column: x => x.connection_id,
                    principalSchema: "werkr",
                    principalTable: "registered_connections",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "daily_recurrence",
            schema: "werkr",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "uuid", nullable: false ),
                day_interval = table.Column<int>( type: "integer", nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_daily_recurrence", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_daily_recurrence_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "werkr",
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "monthly_recurrence",
            schema: "werkr",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "uuid", nullable: false ),
                day_numbers = table.Column<string>( type: "text", nullable: true ),
                months_of_year = table.Column<int>( type: "integer", nullable: false ),
                week_number = table.Column<int>( type: "integer", nullable: true ),
                days_of_week = table.Column<int>( type: "integer", nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_monthly_recurrence", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_monthly_recurrence_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "werkr",
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedule_expiration",
            schema: "werkr",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "uuid", nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false ),
                date = table.Column<DateOnly>( type: "date", nullable: false ),
                time = table.Column<TimeOnly>( type: "time without time zone", nullable: false ),
                time_zone = table.Column<string>( type: "text", nullable: false ),
                is_fixed_offset = table.Column<bool>( type: "boolean", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedule_expiration", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_schedule_expiration_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "werkr",
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedule_holiday_calendars",
            schema: "werkr",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "uuid", nullable: false ),
                holiday_calendar_id = table.Column<Guid>( type: "uuid", nullable: false ),
                mode = table.Column<string>( type: "text", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedule_holiday_calendars", x => new { x.schedule_id, x.holiday_calendar_id } );
                _ = table.ForeignKey(
                    name: "fk_schedule_holiday_calendars_holiday_calendars_holiday_calend",
                    column: x => x.holiday_calendar_id,
                    principalSchema: "werkr",
                    principalTable: "holiday_calendars",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
                _ = table.ForeignKey(
                    name: "fk_schedule_holiday_calendars_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "werkr",
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedule_repeat_options",
            schema: "werkr",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "uuid", nullable: false ),
                repeat_interval_minutes = table.Column<int>( type: "integer", nullable: false ),
                repeat_duration_minutes = table.Column<int>( type: "integer", nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedule_repeat_options", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_schedule_repeat_options_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "werkr",
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "schedule_start_datetimeinfo",
            schema: "werkr",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "uuid", nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false ),
                date = table.Column<DateOnly>( type: "date", nullable: false ),
                time = table.Column<TimeOnly>( type: "time without time zone", nullable: false ),
                time_zone = table.Column<string>( type: "text", nullable: false ),
                is_fixed_offset = table.Column<bool>( type: "boolean", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_schedule_start_datetimeinfo", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_schedule_start_datetimeinfo_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "werkr",
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "weekly_recurrence",
            schema: "werkr",
            columns: table => new {
                schedule_id = table.Column<Guid>( type: "uuid", nullable: false ),
                week_interval = table.Column<int>( type: "integer", nullable: false ),
                days_of_week = table.Column<int>( type: "integer", nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_weekly_recurrence", x => x.schedule_id );
                _ = table.ForeignKey(
                    name: "fk_weekly_recurrence_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "werkr",
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "holiday_dates",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn ),
                holiday_calendar_id = table.Column<Guid>( type: "uuid", nullable: false ),
                holiday_rule_id = table.Column<long>( type: "bigint", nullable: true ),
                date = table.Column<DateOnly>( type: "date", nullable: false ),
                name = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                year = table.Column<int>( type: "integer", nullable: false ),
                window_start = table.Column<TimeOnly>( type: "time without time zone", nullable: true ),
                window_end = table.Column<TimeOnly>( type: "time without time zone", nullable: true ),
                window_time_zone_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_holiday_dates", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_holiday_dates_holiday_calendars_holiday_calendar_id",
                    column: x => x.holiday_calendar_id,
                    principalSchema: "werkr",
                    principalTable: "holiday_calendars",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
                _ = table.ForeignKey(
                    name: "fk_holiday_dates_holiday_rules_holiday_rule_id",
                    column: x => x.holiday_rule_id,
                    principalSchema: "werkr",
                    principalTable: "holiday_rules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull );
            } );

        _ = migrationBuilder.CreateTable(
            name: "file_monitor_triggers",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                workflow_id = table.Column<long>( type: "bigint", nullable: false ),
                watch_directory = table.Column<string>( type: "character varying(500)", maxLength: 500, nullable: false ),
                file_pattern = table.Column<string>( type: "character varying(200)", maxLength: 200, nullable: false ),
                event_types = table.Column<string>( type: "text", nullable: false ),
                debounce_ms = table.Column<int>( type: "integer", nullable: false ),
                enabled = table.Column<bool>( type: "boolean", nullable: false ),
                target_tags = table.Column<string>( type: "text", nullable: true ),
                current_version_id = table.Column<long>( type: "bigint", nullable: true ),
                version_binding_mode = table.Column<string>( type: "text", nullable: false ),
                pinned_workflow_version_id = table.Column<long>( type: "bigint", nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_file_monitor_triggers", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "trigger_versions",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                trigger_id = table.Column<long>( type: "bigint", nullable: false ),
                version_number = table.Column<int>( type: "integer", nullable: false ),
                definition = table.Column<string>( type: "text", nullable: false ),
                created_by_user_id = table.Column<string>( type: "character varying(450)", maxLength: 450, nullable: true ),
                change_description = table.Column<string>( type: "character varying(500)", maxLength: 500, nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_trigger_versions", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_trigger_versions_file_monitor_triggers_trigger_id",
                    column: x => x.trigger_id,
                    principalSchema: "werkr",
                    principalTable: "file_monitor_triggers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "jobs",
            schema: "werkr",
            columns: table => new {
                id = table.Column<Guid>( type: "uuid", nullable: false ),
                task_id = table.Column<long>( type: "bigint", nullable: false ),
                task_snapshot = table.Column<string>( type: "character varying(8000)", maxLength: 8000, nullable: false ),
                runtime_seconds = table.Column<double>( type: "double precision", nullable: false ),
                start_time = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                end_time = table.Column<DateTime>( type: "timestamp with time zone", nullable: true ),
                success = table.Column<bool>( type: "boolean", nullable: false ),
                agent_connection_id = table.Column<Guid>( type: "uuid", nullable: true ),
                exit_code = table.Column<int>( type: "integer", nullable: true ),
                error_category = table.Column<string>( type: "text", nullable: false ),
                output = table.Column<string>( type: "character varying(2000)", maxLength: 2000, nullable: true ),
                output_path = table.Column<string>( type: "character varying(512)", maxLength: 512, nullable: true ),
                workflow_run_id = table.Column<Guid>( type: "uuid", nullable: true ),
                schedule_id = table.Column<Guid>( type: "uuid", nullable: true ),
                step_id = table.Column<long>( type: "bigint", nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_jobs", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_jobs_registered_connections_agent_connection_id",
                    column: x => x.agent_connection_id,
                    principalSchema: "werkr",
                    principalTable: "registered_connections",
                    principalColumn: "id" );
                _ = table.ForeignKey(
                    name: "fk_jobs_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "werkr",
                    principalTable: "schedules",
                    principalColumn: "id" );
            } );

        _ = migrationBuilder.CreateTable(
            name: "notification_subscriptions",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                subscription_type = table.Column<int>( type: "integer", nullable: false ),
                workflow_id = table.Column<long>( type: "bigint", nullable: true ),
                tag = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                event_category_id = table.Column<string>( type: "character varying(64)", maxLength: 64, nullable: false ),
                channel_id = table.Column<long>( type: "bigint", nullable: false ),
                is_enabled = table.Column<bool>( type: "boolean", nullable: false ),
                created_by_user_id = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                created_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_notification_subscriptions", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_notification_subscriptions_notification_channels_channel_id",
                    column: x => x.channel_id,
                    principalSchema: "werkr",
                    principalTable: "notification_channels",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "task_schedules",
            schema: "werkr",
            columns: table => new {
                task_id = table.Column<long>( type: "bigint", nullable: false ),
                schedule_id = table.Column<Guid>( type: "uuid", nullable: false ),
                created_at_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                is_one_time = table.Column<bool>( type: "boolean", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_task_schedules", x => new { x.task_id, x.schedule_id } );
                _ = table.ForeignKey(
                    name: "fk_task_schedules_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "werkr",
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "task_versions",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                task_id = table.Column<long>( type: "bigint", nullable: false ),
                version_number = table.Column<int>( type: "integer", nullable: false ),
                definition = table.Column<string>( type: "text", nullable: false ),
                created_by_user_id = table.Column<string>( type: "character varying(450)", maxLength: 450, nullable: true ),
                change_description = table.Column<string>( type: "character varying(500)", maxLength: 500, nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_task_versions", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "tasks",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                name = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                description = table.Column<string>( type: "character varying(2000)", maxLength: 2000, nullable: false ),
                action_type = table.Column<string>( type: "text", nullable: false ),
                workflow_id = table.Column<long>( type: "bigint", nullable: true ),
                content = table.Column<string>( type: "character varying(8000)", maxLength: 8000, nullable: false ),
                arguments = table.Column<string>( type: "text", nullable: true ),
                target_tags = table.Column<string>( type: "text", nullable: false ),
                enabled = table.Column<bool>( type: "boolean", nullable: false ),
                is_ephemeral = table.Column<bool>( type: "boolean", nullable: false ),
                timeout_minutes = table.Column<long>( type: "bigint", nullable: true ),
                sync_interval_minutes = table.Column<int>( type: "integer", nullable: false ),
                success_criteria = table.Column<string>( type: "character varying(500)", maxLength: 500, nullable: true ),
                action_sub_type = table.Column<string>( type: "character varying(30)", maxLength: 30, nullable: true ),
                action_parameters = table.Column<string>( type: "text", nullable: true ),
                current_version_id = table.Column<long>( type: "bigint", nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_tasks", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_tasks_task_versions_current_version_id",
                    column: x => x.current_version_id,
                    principalSchema: "werkr",
                    principalTable: "task_versions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_run_variables",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                workflow_run_id = table.Column<Guid>( type: "uuid", nullable: false ),
                variable_name = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                value = table.Column<string>( type: "text", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false ),
                produced_by_step_id = table.Column<long>( type: "bigint", nullable: true ),
                produced_by_job_id = table.Column<Guid>( type: "uuid", nullable: true ),
                source = table.Column<string>( type: "text", nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_run_variables", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_workflow_run_variables_jobs_produced_by_job_id",
                    column: x => x.produced_by_job_id,
                    principalSchema: "werkr",
                    principalTable: "jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_runs",
            schema: "werkr",
            columns: table => new {
                id = table.Column<Guid>( type: "uuid", nullable: false ),
                workflow_id = table.Column<long>( type: "bigint", nullable: false ),
                start_time = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                end_time = table.Column<DateTime>( type: "timestamp with time zone", nullable: true ),
                status = table.Column<string>( type: "text", nullable: false ),
                workflow_version_id = table.Column<long>( type: "bigint", nullable: true ),
                workflow_name_snapshot = table.Column<string>( type: "character varying(200)", maxLength: 200, nullable: true ),
                workflow_version_snapshot = table.Column<int>( type: "integer", nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_runs", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_schedules",
            schema: "werkr",
            columns: table => new {
                workflow_id = table.Column<long>( type: "bigint", nullable: false ),
                schedule_id = table.Column<Guid>( type: "uuid", nullable: false ),
                created_at_utc = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                is_one_time = table.Column<bool>( type: "boolean", nullable: false ),
                workflow_run_id = table.Column<Guid>( type: "uuid", nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_schedules", x => new { x.workflow_id, x.schedule_id } );
                _ = table.ForeignKey(
                    name: "fk_workflow_schedules_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "werkr",
                    principalTable: "schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_step_dependencies",
            schema: "werkr",
            columns: table => new {
                step_id = table.Column<long>( type: "bigint", nullable: false ),
                depends_on_step_id = table.Column<long>( type: "bigint", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_step_dependencies", x => new { x.step_id, x.depends_on_step_id } );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_step_executions",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                workflow_run_id = table.Column<Guid>( type: "uuid", nullable: false ),
                step_id = table.Column<long>( type: "bigint", nullable: false ),
                attempt = table.Column<int>( type: "integer", nullable: false ),
                status = table.Column<string>( type: "text", nullable: false ),
                start_time = table.Column<DateTime>( type: "timestamp with time zone", nullable: true ),
                end_time = table.Column<DateTime>( type: "timestamp with time zone", nullable: true ),
                job_id = table.Column<Guid>( type: "uuid", nullable: true ),
                error_message = table.Column<string>( type: "character varying(4000)", maxLength: 4000, nullable: true ),
                skip_reason = table.Column<string>( type: "character varying(2000)", maxLength: 2000, nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_step_executions", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_workflow_step_executions_jobs_job_id",
                    column: x => x.job_id,
                    principalSchema: "werkr",
                    principalTable: "jobs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull );
                _ = table.ForeignKey(
                    name: "fk_workflow_step_executions_workflow_runs_workflow_run_id",
                    column: x => x.workflow_run_id,
                    principalSchema: "werkr",
                    principalTable: "workflow_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_steps",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                workflow_id = table.Column<long>( type: "bigint", nullable: false ),
                task_id = table.Column<long>( type: "bigint", nullable: true ),
                order = table.Column<int>( type: "integer", nullable: false ),
                control_statement = table.Column<string>( type: "text", nullable: false ),
                condition_expression = table.Column<string>( type: "character varying(2000)", maxLength: 2000, nullable: true ),
                max_iterations = table.Column<int>( type: "integer", nullable: false ),
                agent_connection_id_override = table.Column<Guid>( type: "uuid", nullable: true ),
                dependency_mode = table.Column<string>( type: "text", nullable: false ),
                input_variable_name = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                output_variable_name = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                is_composite = table.Column<bool>( type: "boolean", nullable: false ),
                composite_type = table.Column<string>( type: "text", nullable: false ),
                child_workflow_id = table.Column<long>( type: "bigint", nullable: true ),
                iteration_variable_name = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                collection_variable_name = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: true ),
                task_version_id = table.Column<long>( type: "bigint", nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_steps", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_workflow_steps_registered_connections_agent_connection_id_o",
                    column: x => x.agent_connection_id_override,
                    principalSchema: "werkr",
                    principalTable: "registered_connections",
                    principalColumn: "id" );
                _ = table.ForeignKey(
                    name: "fk_workflow_steps_task_versions_task_version_id",
                    column: x => x.task_version_id,
                    principalSchema: "werkr",
                    principalTable: "task_versions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull );
                _ = table.ForeignKey(
                    name: "fk_workflow_steps_tasks_task_id",
                    column: x => x.task_id,
                    principalSchema: "werkr",
                    principalTable: "tasks",
                    principalColumn: "id" );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_variables",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                workflow_id = table.Column<long>( type: "bigint", nullable: false ),
                name = table.Column<string>( type: "character varying(128)", maxLength: 128, nullable: false ),
                description = table.Column<string>( type: "character varying(500)", maxLength: 500, nullable: true ),
                default_value = table.Column<string>( type: "text", nullable: true ),
                data_type = table.Column<string>( type: "character varying(32)", maxLength: 32, nullable: true ),
                is_required = table.Column<bool>( type: "boolean", nullable: false ),
                log_redaction = table.Column<bool>( type: "boolean", nullable: false ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_variables", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflow_versions",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                workflow_id = table.Column<long>( type: "bigint", nullable: false ),
                version_number = table.Column<int>( type: "integer", nullable: false ),
                definition = table.Column<string>( type: "text", nullable: false ),
                created_by_user_id = table.Column<string>( type: "character varying(450)", maxLength: 450, nullable: true ),
                change_description = table.Column<string>( type: "character varying(500)", maxLength: 500, nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflow_versions", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "workflows",
            schema: "werkr",
            columns: table => new {
                id = table.Column<long>( type: "bigint", nullable: false )
                    .Annotation( "Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn ),
                name = table.Column<string>( type: "character varying(256)", maxLength: 256, nullable: false ),
                description = table.Column<string>( type: "character varying(2000)", maxLength: 2000, nullable: false ),
                enabled = table.Column<bool>( type: "boolean", nullable: false ),
                target_tags = table.Column<string>( type: "text", nullable: true ),
                annotations = table.Column<string>( type: "text", nullable: true ),
                parent_step_id = table.Column<long>( type: "bigint", nullable: true ),
                is_child_workflow = table.Column<bool>( type: "boolean", nullable: false ),
                current_version_id = table.Column<long>( type: "bigint", nullable: true ),
                created = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                last_updated = table.Column<DateTime>( type: "timestamp with time zone", nullable: false ),
                version = table.Column<int>( type: "integer", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_workflows", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_workflows_workflow_steps_parent_step_id",
                    column: x => x.parent_step_id,
                    principalSchema: "werkr",
                    principalTable: "workflow_steps",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull );
                _ = table.ForeignKey(
                    name: "fk_workflows_workflow_versions_current_version_id",
                    column: x => x.current_version_id,
                    principalSchema: "werkr",
                    principalTable: "workflow_versions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull );
            } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_audit_events_actor_id",
            schema: "werkr",
            table: "audit_events",
            column: "actor_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_audit_events_entity_type_entity_id",
            schema: "werkr",
            table: "audit_events",
            columns: new[] { "entity_type", "entity_id" } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_audit_events_event_category",
            schema: "werkr",
            table: "audit_events",
            column: "event_category" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_audit_events_event_type_id",
            schema: "werkr",
            table: "audit_events",
            column: "event_type_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_audit_events_timestamp_utc",
            schema: "werkr",
            table: "audit_events",
            column: "timestamp_utc",
            descending: new bool[0] );

        _ = migrationBuilder.CreateIndex(
            name: "ix_configuration_change_logs_configuration_entry_id",
            schema: "werkr",
            table: "configuration_change_logs",
            column: "configuration_entry_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_configuration_entries_category",
            schema: "werkr",
            table: "configuration_entries",
            column: "category" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_configuration_entries_key_scope_level_scope_id",
            schema: "werkr",
            table: "configuration_entries",
            columns: new[] { "key", "scope_level", "scope_id" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_configuration_entries_scope_id",
            schema: "werkr",
            table: "configuration_entries",
            column: "scope_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_configuration_entries_sync_version",
            schema: "werkr",
            table: "configuration_entries",
            column: "sync_version" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_credential_agent_scopes_agent_connection_id",
            schema: "werkr",
            table: "credential_agent_scopes",
            column: "agent_connection_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_credentials_name",
            schema: "werkr",
            table: "credentials",
            column: "name",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_file_monitor_triggers_current_version_id",
            schema: "werkr",
            table: "file_monitor_triggers",
            column: "current_version_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_file_monitor_triggers_pinned_workflow_version_id",
            schema: "werkr",
            table: "file_monitor_triggers",
            column: "pinned_workflow_version_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_file_monitor_triggers_workflow_id",
            schema: "werkr",
            table: "file_monitor_triggers",
            column: "workflow_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_holiday_calendars_name",
            schema: "werkr",
            table: "holiday_calendars",
            column: "name",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_holiday_dates_holiday_calendar_id_date",
            schema: "werkr",
            table: "holiday_dates",
            columns: new[] { "holiday_calendar_id", "date" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_holiday_dates_holiday_rule_id",
            schema: "werkr",
            table: "holiday_dates",
            column: "holiday_rule_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_holiday_rules_holiday_calendar_id",
            schema: "werkr",
            table: "holiday_rules",
            column: "holiday_calendar_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_jobs_agent_connection_id",
            schema: "werkr",
            table: "jobs",
            column: "agent_connection_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_jobs_schedule_id",
            schema: "werkr",
            table: "jobs",
            column: "schedule_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_jobs_step_id",
            schema: "werkr",
            table: "jobs",
            column: "step_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_jobs_task_id",
            schema: "werkr",
            table: "jobs",
            column: "task_id" );

        _ = migrationBuilder.CreateIndex(
            name: "IX_jobs_WorkflowRunId_StepId",
            schema: "werkr",
            table: "jobs",
            columns: new[] { "workflow_run_id", "step_id" } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_notification_channels_name",
            schema: "werkr",
            table: "notification_channels",
            column: "name",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_notification_deliveries_channel_id",
            schema: "werkr",
            table: "notification_deliveries",
            column: "channel_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_notification_deliveries_status_next_retry_utc",
            schema: "werkr",
            table: "notification_deliveries",
            columns: new[] { "status", "next_retry_utc" } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_notification_subscriptions_channel_id",
            schema: "werkr",
            table: "notification_subscriptions",
            column: "channel_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_notification_subscriptions_event_category_id",
            schema: "werkr",
            table: "notification_subscriptions",
            column: "event_category_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_notification_subscriptions_tag",
            schema: "werkr",
            table: "notification_subscriptions",
            column: "tag" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_notification_subscriptions_workflow_id",
            schema: "werkr",
            table: "notification_subscriptions",
            column: "workflow_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_notification_templates_event_type_id_channel_type",
            schema: "werkr",
            table: "notification_templates",
            columns: new[] { "event_type_id", "channel_type" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_pending_agent_notifications_connection_id_created_utc",
            schema: "werkr",
            table: "pending_agent_notifications",
            columns: new[] { "connection_id", "created_utc" } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_registered_connections_connection_name",
            schema: "werkr",
            table: "registered_connections",
            column: "connection_name" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_registered_connections_remote_url",
            schema: "werkr",
            table: "registered_connections",
            column: "remote_url" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_registration_bundles_bundle_id",
            schema: "werkr",
            table: "registration_bundles",
            column: "bundle_id",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_retention_policies_entity_type",
            schema: "werkr",
            table: "retention_policies",
            column: "entity_type",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_saved_filters_page_key_is_shared",
            schema: "werkr",
            table: "saved_filters",
            columns: new[] { "page_key", "is_shared" } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_saved_filters_page_key_owner_id",
            schema: "werkr",
            table: "saved_filters",
            columns: new[] { "page_key", "owner_id" } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_schedule_holiday_calendars_holiday_calendar_id",
            schema: "werkr",
            table: "schedule_holiday_calendars",
            column: "holiday_calendar_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_schedule_holiday_calendars_schedule_id",
            schema: "werkr",
            table: "schedule_holiday_calendars",
            column: "schedule_id",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_task_schedules_schedule_id",
            schema: "werkr",
            table: "task_schedules",
            column: "schedule_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_task_versions_task_id",
            schema: "werkr",
            table: "task_versions",
            column: "task_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_task_versions_task_id_version_number",
            schema: "werkr",
            table: "task_versions",
            columns: new[] { "task_id", "version_number" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_tasks_current_version_id",
            schema: "werkr",
            table: "tasks",
            column: "current_version_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_tasks_workflow_id",
            schema: "werkr",
            table: "tasks",
            column: "workflow_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_trigger_versions_trigger_id",
            schema: "werkr",
            table: "trigger_versions",
            column: "trigger_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_trigger_versions_trigger_id_version_number",
            schema: "werkr",
            table: "trigger_versions",
            columns: new[] { "trigger_id", "version_number" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_user_notification_preferences_user_id_event_category_id",
            schema: "werkr",
            table: "user_notification_preferences",
            columns: new[] { "user_id", "event_category_id" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_user_notifications_user_id_is_read_created_utc",
            schema: "werkr",
            table: "user_notifications",
            columns: new[] { "user_id", "is_read", "created_utc" },
            descending: new[] { false, false, true } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_user_preferences_user_id_key",
            schema: "werkr",
            table: "user_preferences",
            columns: new[] { "user_id", "key" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_run_variables_produced_by_job_id",
            schema: "werkr",
            table: "workflow_run_variables",
            column: "produced_by_job_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_run_variables_produced_by_step_id",
            schema: "werkr",
            table: "workflow_run_variables",
            column: "produced_by_step_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_run_variables_workflow_run_id_variable_name_version",
            schema: "werkr",
            table: "workflow_run_variables",
            columns: new[] { "workflow_run_id", "variable_name", "version" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_runs_workflow_id",
            schema: "werkr",
            table: "workflow_runs",
            column: "workflow_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_runs_workflow_version_id",
            schema: "werkr",
            table: "workflow_runs",
            column: "workflow_version_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_schedules_schedule_id",
            schema: "werkr",
            table: "workflow_schedules",
            column: "schedule_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_step_dependencies_depends_on_step_id",
            schema: "werkr",
            table: "workflow_step_dependencies",
            column: "depends_on_step_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_step_executions_job_id",
            schema: "werkr",
            table: "workflow_step_executions",
            column: "job_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_step_executions_step_id",
            schema: "werkr",
            table: "workflow_step_executions",
            column: "step_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_step_executions_workflow_run_id",
            schema: "werkr",
            table: "workflow_step_executions",
            column: "workflow_run_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_step_executions_workflow_run_id_step_id_attempt",
            schema: "werkr",
            table: "workflow_step_executions",
            columns: new[] { "workflow_run_id", "step_id", "attempt" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_steps_agent_connection_id_override",
            schema: "werkr",
            table: "workflow_steps",
            column: "agent_connection_id_override" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_steps_child_workflow_id",
            schema: "werkr",
            table: "workflow_steps",
            column: "child_workflow_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_steps_task_id",
            schema: "werkr",
            table: "workflow_steps",
            column: "task_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_steps_task_version_id",
            schema: "werkr",
            table: "workflow_steps",
            column: "task_version_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_steps_workflow_id",
            schema: "werkr",
            table: "workflow_steps",
            column: "workflow_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_variables_workflow_id_name",
            schema: "werkr",
            table: "workflow_variables",
            columns: new[] { "workflow_id", "name" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_versions_workflow_id",
            schema: "werkr",
            table: "workflow_versions",
            column: "workflow_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflow_versions_workflow_id_version_number",
            schema: "werkr",
            table: "workflow_versions",
            columns: new[] { "workflow_id", "version_number" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflows_current_version_id",
            schema: "werkr",
            table: "workflows",
            column: "current_version_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_workflows_parent_step_id",
            schema: "werkr",
            table: "workflows",
            column: "parent_step_id" );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_file_monitor_triggers_trigger_versions_current_version_id",
            schema: "werkr",
            table: "file_monitor_triggers",
            column: "current_version_id",
            principalSchema: "werkr",
            principalTable: "trigger_versions",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_file_monitor_triggers_workflow_versions_pinned_workflow_ver",
            schema: "werkr",
            table: "file_monitor_triggers",
            column: "pinned_workflow_version_id",
            principalSchema: "werkr",
            principalTable: "workflow_versions",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_file_monitor_triggers_workflows_workflow_id",
            schema: "werkr",
            table: "file_monitor_triggers",
            column: "workflow_id",
            principalSchema: "werkr",
            principalTable: "workflows",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_jobs_tasks_task_id",
            schema: "werkr",
            table: "jobs",
            column: "task_id",
            principalSchema: "werkr",
            principalTable: "tasks",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_jobs_workflow_runs_workflow_run_id",
            schema: "werkr",
            table: "jobs",
            column: "workflow_run_id",
            principalSchema: "werkr",
            principalTable: "workflow_runs",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_jobs_workflow_steps_step_id",
            schema: "werkr",
            table: "jobs",
            column: "step_id",
            principalSchema: "werkr",
            principalTable: "workflow_steps",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_notification_subscriptions_workflows_workflow_id",
            schema: "werkr",
            table: "notification_subscriptions",
            column: "workflow_id",
            principalSchema: "werkr",
            principalTable: "workflows",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_task_schedules_tasks_task_id",
            schema: "werkr",
            table: "task_schedules",
            column: "task_id",
            principalSchema: "werkr",
            principalTable: "tasks",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_task_versions_tasks_task_id",
            schema: "werkr",
            table: "task_versions",
            column: "task_id",
            principalSchema: "werkr",
            principalTable: "tasks",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_tasks_workflows_workflow_id",
            schema: "werkr",
            table: "tasks",
            column: "workflow_id",
            principalSchema: "werkr",
            principalTable: "workflows",
            principalColumn: "id" );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_run_variables_workflow_runs_workflow_run_id",
            schema: "werkr",
            table: "workflow_run_variables",
            column: "workflow_run_id",
            principalSchema: "werkr",
            principalTable: "workflow_runs",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_run_variables_workflow_steps_produced_by_step_id",
            schema: "werkr",
            table: "workflow_run_variables",
            column: "produced_by_step_id",
            principalSchema: "werkr",
            principalTable: "workflow_steps",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_runs_workflow_versions_workflow_version_id",
            schema: "werkr",
            table: "workflow_runs",
            column: "workflow_version_id",
            principalSchema: "werkr",
            principalTable: "workflow_versions",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_runs_workflows_workflow_id",
            schema: "werkr",
            table: "workflow_runs",
            column: "workflow_id",
            principalSchema: "werkr",
            principalTable: "workflows",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_schedules_workflows_workflow_id",
            schema: "werkr",
            table: "workflow_schedules",
            column: "workflow_id",
            principalSchema: "werkr",
            principalTable: "workflows",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_step_dependencies_workflow_steps_depends_on_step_id",
            schema: "werkr",
            table: "workflow_step_dependencies",
            column: "depends_on_step_id",
            principalSchema: "werkr",
            principalTable: "workflow_steps",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_step_dependencies_workflow_steps_step_id",
            schema: "werkr",
            table: "workflow_step_dependencies",
            column: "step_id",
            principalSchema: "werkr",
            principalTable: "workflow_steps",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_step_executions_workflow_steps_step_id",
            schema: "werkr",
            table: "workflow_step_executions",
            column: "step_id",
            principalSchema: "werkr",
            principalTable: "workflow_steps",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_steps_workflows_child_workflow_id",
            schema: "werkr",
            table: "workflow_steps",
            column: "child_workflow_id",
            principalSchema: "werkr",
            principalTable: "workflows",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_steps_workflows_workflow_id",
            schema: "werkr",
            table: "workflow_steps",
            column: "workflow_id",
            principalSchema: "werkr",
            principalTable: "workflows",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_variables_workflows_workflow_id",
            schema: "werkr",
            table: "workflow_variables",
            column: "workflow_id",
            principalSchema: "werkr",
            principalTable: "workflows",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );

        _ = migrationBuilder.AddForeignKey(
            name: "fk_workflow_versions_workflows_workflow_id",
            schema: "werkr",
            table: "workflow_versions",
            column: "workflow_id",
            principalSchema: "werkr",
            principalTable: "workflows",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade );
    }

    /// <inheritdoc />
    protected override void Down( MigrationBuilder migrationBuilder ) {
        _ = migrationBuilder.DropForeignKey(
            name: "fk_workflow_steps_registered_connections_agent_connection_id_o",
            schema: "werkr",
            table: "workflow_steps" );

        _ = migrationBuilder.DropForeignKey(
            name: "fk_file_monitor_triggers_trigger_versions_current_version_id",
            schema: "werkr",
            table: "file_monitor_triggers" );

        _ = migrationBuilder.DropForeignKey(
            name: "fk_workflows_workflow_versions_current_version_id",
            schema: "werkr",
            table: "workflows" );

        _ = migrationBuilder.DropForeignKey(
            name: "fk_tasks_workflows_workflow_id",
            schema: "werkr",
            table: "tasks" );

        _ = migrationBuilder.DropForeignKey(
            name: "fk_workflow_steps_workflows_child_workflow_id",
            schema: "werkr",
            table: "workflow_steps" );

        _ = migrationBuilder.DropForeignKey(
            name: "fk_workflow_steps_workflows_workflow_id",
            schema: "werkr",
            table: "workflow_steps" );

        _ = migrationBuilder.DropForeignKey(
            name: "fk_task_versions_tasks_task_id",
            schema: "werkr",
            table: "task_versions" );

        _ = migrationBuilder.DropTable(
            name: "audit_events",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "configuration_change_logs",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "credential_agent_scopes",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "daily_recurrence",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "holiday_dates",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "monthly_recurrence",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "notification_deliveries",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "notification_subscriptions",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "notification_templates",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "pending_agent_notifications",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "registration_bundles",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "retention_policies",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "saved_filters",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "schedule_expiration",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "schedule_holiday_calendars",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "schedule_repeat_options",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "schedule_start_datetimeinfo",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "task_schedules",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "user_notification_preferences",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "user_notifications",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "user_preferences",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "weekly_recurrence",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "workflow_run_variables",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "workflow_schedules",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "workflow_step_dependencies",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "workflow_step_executions",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "workflow_variables",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "configuration_entries",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "credentials",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "holiday_rules",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "notification_channels",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "jobs",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "holiday_calendars",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "schedules",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "workflow_runs",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "registered_connections",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "trigger_versions",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "file_monitor_triggers",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "workflow_versions",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "workflows",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "workflow_steps",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "tasks",
            schema: "werkr" );

        _ = migrationBuilder.DropTable(
            name: "task_versions",
            schema: "werkr" );
    }
}
