using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Werkr.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddCompositeNodeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_child_workflow",
                table: "workflows",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "parent_step_id",
                table: "workflows",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "child_workflow_id",
                table: "workflow_steps",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "collection_variable_name",
                table: "workflow_steps",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "composite_type",
                table: "workflow_steps",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_composite",
                table: "workflow_steps",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "iteration_variable_name",
                table: "workflow_steps",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflows_parent_step_id",
                table: "workflows",
                column: "parent_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_child_workflow_id",
                table: "workflow_steps",
                column: "child_workflow_id");

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_steps_workflows_child_workflow_id",
                table: "workflow_steps",
                column: "child_workflow_id",
                principalTable: "workflows",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_workflows_workflow_steps_parent_step_id",
                table: "workflows",
                column: "parent_step_id",
                principalTable: "workflow_steps",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_workflow_steps_workflows_child_workflow_id",
                table: "workflow_steps");

            migrationBuilder.DropForeignKey(
                name: "fk_workflows_workflow_steps_parent_step_id",
                table: "workflows");

            migrationBuilder.DropIndex(
                name: "ix_workflows_parent_step_id",
                table: "workflows");

            migrationBuilder.DropIndex(
                name: "ix_workflow_steps_child_workflow_id",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "is_child_workflow",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "parent_step_id",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "child_workflow_id",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "collection_variable_name",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "composite_type",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "is_composite",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "iteration_variable_name",
                table: "workflow_steps");
        }
    }
}
