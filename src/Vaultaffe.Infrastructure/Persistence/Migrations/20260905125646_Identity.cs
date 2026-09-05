using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaultaffe.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class Identity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "user_id",
            table: "token",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.CreateTable(
            name: "app_user",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                is_administrator = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user", x => x.id);
                table.CheckConstraint("ck_user_email", "email ~ '^[^@\\s]+@[^@\\s]+$'");
                table.ForeignKey(
                    name: "fk_user_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "device_authorization",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                device_code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                user_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                denied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                issued_token_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_device_authorization", x => x.id);
                table.ForeignKey(
                    name: "fk_device_authorization_approved_by",
                    column: x => x.approved_by_user_id,
                    principalTable: "app_user",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_device_authorization_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_token_user",
            table: "token",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_app_user_organization_id",
            table: "app_user",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "ux_user_email",
            table: "app_user",
            column: "email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_device_authorization_approved_by_user_id",
            table: "device_authorization",
            column: "approved_by_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_device_authorization_organization_id",
            table: "device_authorization",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "ix_device_authorization_expires_at",
            table: "device_authorization",
            column: "expires_at");

        migrationBuilder.CreateIndex(
            name: "ux_device_authorization_device_code_hash",
            table: "device_authorization",
            column: "device_code_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_device_authorization_user_code",
            table: "device_authorization",
            column: "user_code",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "fk_token_user",
            table: "token",
            column: "user_id",
            principalTable: "app_user",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_token_user",
            table: "token");

        migrationBuilder.DropTable(
            name: "device_authorization");

        migrationBuilder.DropTable(
            name: "app_user");

        migrationBuilder.DropIndex(
            name: "ix_token_user",
            table: "token");

        migrationBuilder.DropColumn(
            name: "user_id",
            table: "token");
    }
}
