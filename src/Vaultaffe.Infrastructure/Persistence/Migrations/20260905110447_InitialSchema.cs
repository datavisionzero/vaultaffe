using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vaultaffe.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "change_log_entry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                identity_type = table.Column<int>(type: "integer", nullable: false),
                identity_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                action = table.Column<int>(type: "integer", nullable: false),
                project_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                environment_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                secret_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_change_log_entry", x => x.id);
                table.CheckConstraint("ck_change_log_entry_identity_type", "identity_type between 1 and 3");
            });

        migrationBuilder.CreateTable(
            name: "organization",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_organization", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "project",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_project", x => x.id);
                table.CheckConstraint("ck_project_name", "name ~ '^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$'");
                table.ForeignKey(
                    name: "fk_project_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "token",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                value_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                scopes = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_token", x => x.id);
                table.CheckConstraint("ck_token_kind", "kind between 1 and 3");
                table.ForeignKey(
                    name: "fk_token_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "environment",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_environment", x => x.id);
                table.CheckConstraint("ck_environment_name", "name ~ '^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$'");
                table.ForeignKey(
                    name: "fk_environment_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_environment_project",
                    column: x => x.project_id,
                    principalTable: "project",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "secret",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                wrapped_data_key = table.Column<byte[]>(type: "bytea", nullable: true),
                nonce = table.Column<byte[]>(type: "bytea", nullable: true),
                ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                value_written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_secret", x => x.id);
                table.CheckConstraint("ck_secret_name", "name ~ '^[A-Z_][A-Z0-9_]*$'");
                table.CheckConstraint("ck_secret_value_sealed_whole", "(ciphertext is null and nonce is null and value_written_at is null)\nor (ciphertext is not null and nonce is not null\n    and value_written_at is not null and wrapped_data_key is not null)");
                table.ForeignKey(
                    name: "fk_secret_environment",
                    column: x => x.environment_id,
                    principalTable: "environment",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_secret_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "token_binding",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_id = table.Column<Guid>(type: "uuid", nullable: false),
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                environment_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_token_binding", x => x.id);
                table.ForeignKey(
                    name: "fk_token_binding_environment",
                    column: x => x.environment_id,
                    principalTable: "environment",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_token_binding_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_token_binding_project",
                    column: x => x.project_id,
                    principalTable: "project",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_token_binding_token",
                    column: x => x.token_id,
                    principalTable: "token",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "secret_value_version",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                secret_id = table.Column<Guid>(type: "uuid", nullable: false),
                nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                replaced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_secret_value_version", x => x.id);
                table.ForeignKey(
                    name: "fk_secret_value_version_organization",
                    column: x => x.organization_id,
                    principalTable: "organization",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_secret_value_version_secret",
                    column: x => x.secret_id,
                    principalTable: "secret",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_change_log_entry_organization_occurred_at",
            table: "change_log_entry",
            columns: new[] { "organization_id", "occurred_at" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "IX_environment_organization_id",
            table: "environment",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "ux_environment_project_name",
            table: "environment",
            columns: new[] { "project_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_project_organization_name",
            table: "project",
            columns: new[] { "organization_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_secret_organization_id",
            table: "secret",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "ux_secret_environment_name",
            table: "secret",
            columns: new[] { "environment_id", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_secret_value_version_organization_id",
            table: "secret_value_version",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "ix_secret_value_version_secret_replaced_at",
            table: "secret_value_version",
            columns: new[] { "secret_id", "replaced_at" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "IX_token_organization_id",
            table: "token",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "ux_token_value_hash",
            table: "token",
            column: "value_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_token_binding_environment_id",
            table: "token_binding",
            column: "environment_id");

        migrationBuilder.CreateIndex(
            name: "IX_token_binding_organization_id",
            table: "token_binding",
            column: "organization_id");

        migrationBuilder.CreateIndex(
            name: "IX_token_binding_project_id",
            table: "token_binding",
            column: "project_id");

        migrationBuilder.CreateIndex(
            name: "ux_token_binding_token_project_environment",
            table: "token_binding",
            columns: new[] { "token_id", "project_id", "environment_id" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "change_log_entry");

        migrationBuilder.DropTable(
            name: "secret_value_version");

        migrationBuilder.DropTable(
            name: "token_binding");

        migrationBuilder.DropTable(
            name: "secret");

        migrationBuilder.DropTable(
            name: "token");

        migrationBuilder.DropTable(
            name: "environment");

        migrationBuilder.DropTable(
            name: "project");

        migrationBuilder.DropTable(
            name: "organization");
    }
}
