using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Werkr.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddCompositeNodeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_child_workflow",
                schema: "werkr",
                table: "workflows",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "parent_step_id",
                schema: "werkr",
                table: "workflows",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "child_workflow_id",
                schema: "werkr",
                table: "workflow_steps",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "collection_variable_name",
                schema: "werkr",
                table: "workflow_steps",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "composite_type",
                schema: "werkr",
                table: "workflow_steps",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_composite",
                schema: "werkr",
                table: "workflow_steps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "iteration_variable_name",
                schema: "werkr",
                table: "workflow_steps",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflows_parent_step_id",
                schema: "werkr",
                table: "workflows",
                column: "parent_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_steps_child_workflow_id",
                schema: "werkr",
                table: "workflow_steps",
                column: "child_workflow_id");

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_steps_workflows_child_workflow_id",
                schema: "werkr",
                table: "workflow_steps",
                column: "child_workflow_id",
                principalSchema: "werkr",
                principalTable: "workflows",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_workflows_workflow_steps_parent_step_id",
                schema: "werkr",
                table: "workflows",
                column: "parent_step_id",
                principalSchema: "werkr",
                principalTable: "workflow_steps",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_workflow_steps_workflows_child_workflow_id",
                schema: "werkr",
                table: "workflow_steps");

            migrationBuilder.DropForeignKey(
                name: "fk_workflows_workflow_steps_parent_step_id",
                schema: "werkr",
                table: "workflows");

            migrationBuilder.DropIndex(
                name: "ix_workflows_parent_step_id",
                schema: "werkr",
                table: "workflows");

            migrationBuilder.DropIndex(
                name: "ix_workflow_steps_child_workflow_id",
                schema: "werkr",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "is_child_workflow",
                schema: "werkr",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "parent_step_id",
                schema: "werkr",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "child_workflow_id",
                schema: "werkr",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "collection_variable_name",
                schema: "werkr",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "composite_type",
                schema: "werkr",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "is_composite",
                schema: "werkr",
                table: "workflow_steps");

            migrationBuilder.DropColumn(
                name: "iteration_variable_name",
                schema: "werkr",
                table: "workflow_steps");
        }
    }
}
