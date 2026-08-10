using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextMemory.Infrastructure.Migrations;

[DbContext(typeof(Persistence.Postgres.ContextMemoryDbContext))]
[Migration("20260810120000_AddSkillActivation")]
public partial class AddSkillActivation : Migration
{
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

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Activation", table: "agentic_app_skills");
        migrationBuilder.DropColumn(name: "Activation", table: "agentic_skill_catalog");
    }
}
