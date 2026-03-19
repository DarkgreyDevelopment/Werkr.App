using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Werkr.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddFileMonitorTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_monitor_triggers",
                schema: "werkr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_id = table.Column<long>(type: "bigint", nullable: false),
                    watch_directory = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_types = table.Column<string>(type: "text", nullable: false),
                    debounce_ms = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    target_tags = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_monitor_triggers", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_monitor_triggers_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalSchema: "werkr",
                        principalTable: "workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_file_monitor_triggers_workflow_id",
                schema: "werkr",
                table: "file_monitor_triggers",
                column: "workflow_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_monitor_triggers",
                schema: "werkr");
        }
    }
}
