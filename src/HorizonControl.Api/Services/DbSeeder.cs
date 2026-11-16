using System.Text.Json;
using HorizonControl.Api.Data;
using HorizonControl.Api.Options;
using HorizonControl.Api.Security;
using HorizonControl.Shared.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HorizonControl.Api.Services;

public sealed class DbSeeder(
    FleetDbContext db,
    IOptions<FleetControlOptions> options,
    ILogger<DbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""CREATE EXTENSION IF NOT EXISTS "uuid-ossp";""", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""CREATE EXTENSION IF NOT EXISTS "ltree";""", cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await EnsureSchemaExtrasAsync(cancellationToken);

        await EnsureRlsAsync(cancellationToken);

        var opt = options.Value;
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == opt.LabTenantSlug, cancellationToken);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                TenantId = Guid.NewGuid(),
                Name = opt.LabTenantName,
                Slug = opt.LabTenantSlug,
                Status = "ACTIVE",
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Tenants.Add(tenant);
        }

        var ou = await db.VirtualOus.FirstOrDefaultAsync(o => o.TenantId == tenant.TenantId && o.Path == "root", cancellationToken);
        if (ou is null)
        {
            ou = new VirtualOu
            {
                OuId = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                Name = "Root",
                Path = "root",
                PolicyOverrideJson = JsonSerializer.Serialize(new OuPolicy())
            };
            db.VirtualOus.Add(ou);
        }

        var tokenHash = SecretHasher.Hash(opt.LabEnrollmentToken);
        var token = await db.EnrollmentTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
        if (token is null)
        {
            db.EnrollmentTokens.Add(new EnrollmentToken
            {
                TokenId = Guid.NewGuid(),
                TenantId = tenant.TenantId,
                OuId = ou.OuId,
                TokenHash = tokenHash,
                UseCount = 0
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Lab seed ready for tenant {TenantSlug}. Enrollment token is configured via FleetControl__LabEnrollmentToken (hash only in DB).",
            tenant.Slug);
    }

    private async Task EnsureSchemaExtrasAsync(CancellationToken cancellationToken)
    {
        // Additive columns/tables for post-V1 slices without forcing a new EF migration in lab.
        const string sql = """
            ALTER TABLE processed_commands ADD COLUMN IF NOT EXISTS success boolean NOT NULL DEFAULT true;
            ALTER TABLE processed_commands ADD COLUMN IF NOT EXISTS message text NULL;
            ALTER TABLE processed_commands ADD COLUMN IF NOT EXISTS instance_id uuid NULL;
            CREATE TABLE IF NOT EXISTS horizon_brokers (
              broker_id uuid PRIMARY KEY,
              tenant_id uuid NOT NULL,
              name text NOT NULL,
              base_url text NOT NULL,
              public_fqdn text NOT NULL,
              encrypted_credentials text NOT NULL DEFAULT '',
              poll_interval_sec int NOT NULL DEFAULT 30,
              last_poll_at timestamptz NULL,
              last_poll_error text NULL
            );
            CREATE TABLE IF NOT EXISTS broker_session_snapshots (
              snapshot_id uuid PRIMARY KEY,
              tenant_id uuid NOT NULL,
              broker_id uuid NOT NULL,
              session_id text NOT NULL,
              user_name text NULL,
              dns_name text NULL,
              broker_fqdn text NULL,
              observed_at timestamptz NOT NULL
            );
            """;
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private async Task EnsureRlsAsync(CancellationToken cancellationToken)
    {
        // RLS policies: tenant_id = current_setting('app.current_tenant')::uuid
        // Bypass for migrations/seed when setting is empty by using FORCE only after policies exist.
        const string sql = """
            DO $$
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'endpoints' AND policyname = 'tenant_isolation') THEN
                ALTER TABLE endpoints ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON endpoints
                  USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
              END IF;
              IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'managed_instances' AND policyname = 'tenant_isolation') THEN
                ALTER TABLE managed_instances ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON managed_instances
                  USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
              END IF;
              IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'audit_logs' AND policyname = 'tenant_isolation') THEN
                ALTER TABLE audit_logs ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON audit_logs
                  USING (tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid);
              END IF;
            END $$;
            """;
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
