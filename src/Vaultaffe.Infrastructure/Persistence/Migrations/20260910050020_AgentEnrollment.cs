using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaultaffe.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AgentEnrollment : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "approved_name",
            table: "device_authorization",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "approved_scopes",
            table: "device_authorization",
            type: "integer",
            nullable: true);

        // 1 is `Handover.Session`, and it is the default rather than EF's 0
        // because of the rows already in this table: everything that existed
        // before this migration is a login, and 0 is not a handover at all —
        // the check constraint below would refuse the very rows the upgrade
        // was meant to carry across.
        migrationBuilder.AddColumn<int>(
            name: "produces",
            table: "device_authorization",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "requested_name",
            table: "device_authorization",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "enrollment_binding",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                authorization_id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                environment_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_enrollment_binding", x => x.id);
                table.ForeignKey(
                    name: "fk_enrollment_binding_authorization",
                    column: x => x.authorization_id,
                    principalTable: "device_authorization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_enrollment_binding_environment",
                    column: x => x.environment_id,
                    principalTable: "environment",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_enrollment_binding_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_enrollment_binding_project",
                    column: x => x.project_id,
                    principalTable: "project",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_device_authorization_produces",
            table: "device_authorization",
            sql: "produces between 1 and 2");

        migrationBuilder.CreateIndex(
            name: "IX_enrollment_binding_environment_id",
            table: "enrollment_binding",
            column: "environment_id");

        migrationBuilder.CreateIndex(
            name: "IX_enrollment_binding_organization_id",
            table: "enrollment_binding",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "IX_enrollment_binding_project_id",
            table: "enrollment_binding",
            column: "project_id");

        migrationBuilder.CreateIndex(
            name: "ux_enrollment_binding_authorization_project_environment",
            table: "enrollment_binding",
            columns: new[] { "authorization_id", "project_id", "environment_id" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "enrollment_binding");

        migrationBuilder.DropCheckConstraint(
            name: "ck_device_authorization_produces",
            table: "device_authorization");

        migrationBuilder.DropColumn(
            name: "approved_name",
            table: "device_authorization");

        migrationBuilder.DropColumn(
            name: "approved_scopes",
            table: "device_authorization");

        migrationBuilder.DropColumn(
            name: "produces",
            table: "device_authorization");

        migrationBuilder.DropColumn(
            name: "requested_name",
            table: "device_authorization");
    }
}
