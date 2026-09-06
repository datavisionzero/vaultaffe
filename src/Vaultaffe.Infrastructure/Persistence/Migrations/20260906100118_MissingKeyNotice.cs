using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaultaffe.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class MissingKeyNotice : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "dismissed_key",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                dismissed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_dismissed_key", x => x.id);
                table.CheckConstraint("ck_dismissed_key_name", "name ~ '^[A-Z_][A-Z0-9_]*$'");
                table.ForeignKey(
                    name: "fk_dismissed_key_environment",
                    column: x => x.environment_id,
                    principalTable: "environment",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_dismissed_key_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_dismissed_key_organization_id",
            table: "dismissed_key",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "ux_dismissed_key_environment_name",
            table: "dismissed_key",
            columns: new[] { "environment_id", "name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "dismissed_key");
    }
}
