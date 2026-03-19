using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Werkr.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddFileMonitorTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_monitor_triggers",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    workflow_id = table.Column<long>(type: "INTEGER", nullable: false),
                    watch_directory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    file_pattern = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    event_types = table.Column<string>(type: "TEXT", nullable: false),
                    debounce_ms = table.Column<int>(type: "INTEGER", nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    target_tags = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_monitor_triggers", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_monitor_triggers_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalTable: "workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_file_monitor_triggers_workflow_id",
                table: "file_monitor_triggers",
                column: "workflow_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_monitor_triggers");
        }
    }
}
