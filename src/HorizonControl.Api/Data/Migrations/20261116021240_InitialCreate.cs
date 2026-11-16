using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HorizonControl.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:ltree", ",,");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    audit_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_type = table.Column<string>(type: "text", nullable: false),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    target_type = table.Column<string>(type: "text", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.audit_id);
                });

            migrationBuilder.CreateTable(
                name: "endpoints",
                columns: table => new
                {
                    agent_uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ou_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hostname = table.Column<string>(type: "text", nullable: false),
                    hardware_hash = table.Column<string>(type: "text", nullable: false),
                    agent_secret_hash = table.Column<string>(type: "text", nullable: false),
                    is_domain_joined = table.Column<bool>(type: "boolean", nullable: false),
                    ad_ou_dn = table.Column<string>(type: "text", nullable: true),
                    os_version = table.Column<string>(type: "text", nullable: true),
                    agent_version = table.Column<string>(type: "text", nullable: true),
                    horizon_client_version = table.Column<string>(type: "text", nullable: true),
                    rustdesk_id = table.Column<string>(type: "text", nullable: true),
                    local_ip = table.Column<string>(type: "text", nullable: true),
                    public_ip = table.Column<string>(type: "text", nullable: true),
                    mac_address = table.Column<string>(type: "text", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_endpoints", x => x.agent_uuid);
                });

            migrationBuilder.CreateTable(
                name: "enrollment_tokens",
                columns: table => new
                {
                    token_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ou_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    use_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrollment_tokens", x => x.token_id);
                });

            migrationBuilder.CreateTable(
                name: "managed_instances",
                columns: table => new
                {
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    broker_fqdn = table.Column<string>(type: "text", nullable: false),
                    horizon_user = table.Column<string>(type: "text", nullable: true),
                    root_pid = table.Column<int>(type: "integer", nullable: false),
                    job_object_name = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    seamless_windows = table.Column<string>(type: "jsonb", nullable: false),
                    child_processes = table.Column<string>(type: "jsonb", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_instances", x => x.instance_id);
                });

            migrationBuilder.CreateTable(
                name: "processed_commands",
                columns: table => new
                {
                    command_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_commands", x => x.command_id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "virtual_ous",
                columns: table => new
                {
                    ou_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    path = table.Column<string>(type: "ltree", nullable: false),
                    policy_override = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_virtual_ous", x => x.ou_id);
                    table.ForeignKey(
                        name: "FK_virtual_ous_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_endpoints_tenant_id_hardware_hash",
                table: "endpoints",
                columns: new[] { "tenant_id", "hardware_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_enrollment_tokens_token_hash",
                table: "enrollment_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_virtual_ous_tenant_id_path",
                table: "virtual_ous",
                columns: new[] { "tenant_id", "path" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "endpoints");

            migrationBuilder.DropTable(
                name: "enrollment_tokens");

            migrationBuilder.DropTable(
                name: "managed_instances");

            migrationBuilder.DropTable(
                name: "processed_commands");

            migrationBuilder.DropTable(
                name: "virtual_ous");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
