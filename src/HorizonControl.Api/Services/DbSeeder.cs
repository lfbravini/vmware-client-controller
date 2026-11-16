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
