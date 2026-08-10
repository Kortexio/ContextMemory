using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Activation",
                table: "agentic_skill_catalog",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "skill");

            migrationBuilder.AddColumn<string>(
                name: "Activation",
                table: "agentic_app_skills",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "skill");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Activation",
                table: "agentic_skill_catalog");

            migrationBuilder.DropColumn(
                name: "Activation",
                table: "agentic_app_skills");
        }
    }
}
