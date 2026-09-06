using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaultaffe.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AccessSummary : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "secret_access",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                secret_id = table.Column<Guid>(type: "uuid", nullable: false),
                identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                identity_type = table.Column<int>(type: "integer", nullable: false),
                identity_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                first_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_secret_access", x => x.id);
                table.CheckConstraint("ck_secret_access_order", "last_at >= first_at");
                table.ForeignKey(
                    name: "fk_secret_access_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_secret_access_secret",
                    column: x => x.secret_id,
                    principalTable: "secret",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_secret_access_organization_id",
            table: "secret_access",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "ux_secret_access_secret_identity",
            table: "secret_access",
            columns: new[] { "secret_id", "identity_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "secret_access");
    }
}
