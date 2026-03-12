using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Werkr.Data.Identity.Migrations.Sqlite;
/// <inheritdoc />
public partial class InitialCreate : Migration {
    /// <inheritdoc />
    protected override void Up( MigrationBuilder migrationBuilder ) {
        _ = migrationBuilder.CreateTable(
            name: "config_settings",
            columns: table => new {
                id = table.Column<Guid>( type: "TEXT", nullable: false ),
                default_key_size = table.Column<int>( type: "INTEGER", nullable: false ),
                server_name = table.Column<string>( type: "TEXT", maxLength: 200, nullable: false ),
                allow_registration = table.Column<bool>( type: "INTEGER", nullable: false ),
                polling_interval_seconds = table.Column<int>( type: "INTEGER", nullable: false ),
                run_detail_polling_interval_seconds = table.Column<int>( type: "INTEGER", nullable: false ),
                created = table.Column<DateTime>( type: "TEXT", nullable: false ),
                last_updated = table.Column<DateTime>( type: "TEXT", nullable: false ),
                version = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_config_settings", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "roles",
            columns: table => new {
                id = table.Column<string>( type: "TEXT", nullable: false ),
                name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: true ),
                normalized_name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: true ),
                concurrency_stamp = table.Column<string>( type: "TEXT", nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_roles", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "users",
            columns: table => new {
                id = table.Column<string>( type: "TEXT", nullable: false ),
                name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: false ),
                enabled = table.Column<bool>( type: "INTEGER", nullable: false ),
                change_password = table.Column<bool>( type: "INTEGER", nullable: false ),
                requires2fa = table.Column<bool>( type: "INTEGER", nullable: false ),
                last_login_utc = table.Column<DateTime>( type: "TEXT", nullable: true ),
                user_name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: true ),
                normalized_user_name = table.Column<string>( type: "TEXT", maxLength: 256, nullable: true ),
                email = table.Column<string>( type: "TEXT", maxLength: 256, nullable: true ),
                normalized_email = table.Column<string>( type: "TEXT", maxLength: 256, nullable: true ),
                email_confirmed = table.Column<bool>( type: "INTEGER", nullable: false ),
                password_hash = table.Column<string>( type: "TEXT", nullable: true ),
                security_stamp = table.Column<string>( type: "TEXT", nullable: true ),
                concurrency_stamp = table.Column<string>( type: "TEXT", nullable: true ),
                phone_number = table.Column<string>( type: "TEXT", nullable: true ),
                phone_number_confirmed = table.Column<bool>( type: "INTEGER", nullable: false ),
                two_factor_enabled = table.Column<bool>( type: "INTEGER", nullable: false ),
                lockout_end = table.Column<DateTimeOffset>( type: "TEXT", nullable: true ),
                lockout_enabled = table.Column<bool>( type: "INTEGER", nullable: false ),
                access_failed_count = table.Column<int>( type: "INTEGER", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_users", x => x.id );
            } );

        _ = migrationBuilder.CreateTable(
            name: "role_claims",
            columns: table => new {
                id = table.Column<int>( type: "INTEGER", nullable: false )
                    .Annotation( "Sqlite:Autoincrement", true ),
                role_id = table.Column<string>( type: "TEXT", nullable: false ),
                claim_type = table.Column<string>( type: "TEXT", nullable: true ),
                claim_value = table.Column<string>( type: "TEXT", nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_role_claims", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_role_claims_roles_role_id",
                    column: x => x.role_id,
                    principalTable: "roles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "role_permissions",
            columns: table => new {
                id = table.Column<long>( type: "INTEGER", nullable: false )
                    .Annotation( "Sqlite:Autoincrement", true ),
                role_id = table.Column<string>( type: "TEXT", nullable: false ),
                permission = table.Column<string>( type: "TEXT", maxLength: 64, nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_role_permissions", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_role_permissions_roles_role_id",
                    column: x => x.role_id,
                    principalTable: "roles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "api_keys",
            columns: table => new {
                id = table.Column<Guid>( type: "TEXT", nullable: false ),
                key_hash = table.Column<string>( type: "TEXT", maxLength: 128, nullable: false ),
                key_prefix = table.Column<string>( type: "TEXT", maxLength: 16, nullable: false ),
                name = table.Column<string>( type: "TEXT", maxLength: 200, nullable: false ),
                role = table.Column<string>( type: "TEXT", maxLength: 64, nullable: false ),
                created_by_user_id = table.Column<string>( type: "TEXT", nullable: false ),
                created_utc = table.Column<DateTime>( type: "TEXT", nullable: false ),
                expires_utc = table.Column<DateTime>( type: "TEXT", nullable: true ),
                is_revoked = table.Column<bool>( type: "INTEGER", nullable: false ),
                last_used_utc = table.Column<DateTime>( type: "TEXT", nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_api_keys", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_api_keys_users_created_by_user_id",
                    column: x => x.created_by_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "user_claims",
            columns: table => new {
                id = table.Column<int>( type: "INTEGER", nullable: false )
                    .Annotation( "Sqlite:Autoincrement", true ),
                user_id = table.Column<string>( type: "TEXT", nullable: false ),
                claim_type = table.Column<string>( type: "TEXT", nullable: true ),
                claim_value = table.Column<string>( type: "TEXT", nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_user_claims", x => x.id );
                _ = table.ForeignKey(
                    name: "fk_user_claims_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "user_logins",
            columns: table => new {
                login_provider = table.Column<string>( type: "TEXT", maxLength: 128, nullable: false ),
                provider_key = table.Column<string>( type: "TEXT", maxLength: 128, nullable: false ),
                provider_display_name = table.Column<string>( type: "TEXT", nullable: true ),
                user_id = table.Column<string>( type: "TEXT", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_user_logins", x => new { x.login_provider, x.provider_key } );
                _ = table.ForeignKey(
                    name: "fk_user_logins_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "user_roles",
            columns: table => new {
                user_id = table.Column<string>( type: "TEXT", nullable: false ),
                role_id = table.Column<string>( type: "TEXT", nullable: false )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_user_roles", x => new { x.user_id, x.role_id } );
                _ = table.ForeignKey(
                    name: "fk_user_roles_roles_role_id",
                    column: x => x.role_id,
                    principalTable: "roles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
                _ = table.ForeignKey(
                    name: "fk_user_roles_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateTable(
            name: "user_tokens",
            columns: table => new {
                user_id = table.Column<string>( type: "TEXT", nullable: false ),
                login_provider = table.Column<string>( type: "TEXT", maxLength: 128, nullable: false ),
                name = table.Column<string>( type: "TEXT", maxLength: 128, nullable: false ),
                value = table.Column<string>( type: "TEXT", nullable: true )
            },
            constraints: table => {
                _ = table.PrimaryKey( "pk_user_tokens", x => new { x.user_id, x.login_provider, x.name } );
                _ = table.ForeignKey(
                    name: "fk_user_tokens_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade );
            } );

        _ = migrationBuilder.CreateIndex(
            name: "ix_api_keys_created_by_user_id",
            table: "api_keys",
            column: "created_by_user_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_api_keys_key_hash",
            table: "api_keys",
            column: "key_hash",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_api_keys_key_prefix",
            table: "api_keys",
            column: "key_prefix" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_role_claims_role_id",
            table: "role_claims",
            column: "role_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_role_permissions_role_id_permission",
            table: "role_permissions",
            columns: new[] { "role_id", "permission" },
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "RoleNameIndex",
            table: "roles",
            column: "normalized_name",
            unique: true );

        _ = migrationBuilder.CreateIndex(
            name: "ix_user_claims_user_id",
            table: "user_claims",
            column: "user_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_user_logins_user_id",
            table: "user_logins",
            column: "user_id" );

        _ = migrationBuilder.CreateIndex(
            name: "ix_user_roles_role_id",
            table: "user_roles",
            column: "role_id" );

        _ = migrationBuilder.CreateIndex(
            name: "EmailIndex",
            table: "users",
            column: "normalized_email" );

        _ = migrationBuilder.CreateIndex(
            name: "UserNameIndex",
            table: "users",
            column: "normalized_user_name",
            unique: true );
    }

    /// <inheritdoc />
    protected override void Down( MigrationBuilder migrationBuilder ) {
        _ = migrationBuilder.DropTable(
            name: "api_keys" );

        _ = migrationBuilder.DropTable(
            name: "config_settings" );

        _ = migrationBuilder.DropTable(
            name: "role_claims" );

        _ = migrationBuilder.DropTable(
            name: "role_permissions" );

        _ = migrationBuilder.DropTable(
            name: "user_claims" );

        _ = migrationBuilder.DropTable(
            name: "user_logins" );

        _ = migrationBuilder.DropTable(
            name: "user_roles" );

        _ = migrationBuilder.DropTable(
            name: "user_tokens" );

        _ = migrationBuilder.DropTable(
            name: "roles" );

        _ = migrationBuilder.DropTable(
            name: "users" );
    }
}
