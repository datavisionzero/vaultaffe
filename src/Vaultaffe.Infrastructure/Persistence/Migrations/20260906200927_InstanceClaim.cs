using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaultaffe.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InstanceClaim : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "instance_claim",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                secret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_instance_claim", x => x.id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "instance_claim");
    }
}
