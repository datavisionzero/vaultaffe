using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaultaffe.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class People : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "deactivated_at",
            table: "app_user",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "invitation",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                is_administrator = table.Column<bool>(type: "boolean", nullable: false),
                code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_invitation", x => x.id);
                table.CheckConstraint("ck_invitation_email", "email ~ '^[^@\\s]+@[^@\\s]+$'");
                table.ForeignKey(
                    name: "fk_invitation_invited_by",
                    column: x => x.invited_by_user_id,
                    principalTable: "app_user",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_invitation_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_invitation_invited_by_user_id",
            table: "invitation",
            column: "invited_by_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_invitation_organization_id",
            table: "invitation",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "ix_invitation_email",
            table: "invitation",
            column: "email");

        migrationBuilder.CreateIndex(
            name: "ux_invitation_code_hash",
            table: "invitation",
            column: "code_hash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "invitation");

        migrationBuilder.DropColumn(
            name: "deactivated_at",
            table: "app_user");
    }
}
