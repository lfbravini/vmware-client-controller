using Microsoft.EntityFrameworkCore;

namespace HorizonControl.Api.Data;

public sealed class FleetDbContext(DbContextOptions<FleetDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<VirtualOu> VirtualOus => Set<VirtualOu>();
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();
    public DbSet<FleetEndpoint> Endpoints => Set<FleetEndpoint>();
    public DbSet<ManagedInstance> ManagedInstances => Set<ManagedInstance>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ProcessedCommand> ProcessedCommands => Set<ProcessedCommand>();
    public DbSet<HorizonBroker> HorizonBrokers => Set<HorizonBroker>();
    public DbSet<BrokerSessionSnapshot> BrokerSessionSnapshots => Set<BrokerSessionSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.TenantId);
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Slug).HasColumnName("slug").IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Status).HasColumnName("status").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<VirtualOu>(e =>
        {
            e.ToTable("virtual_ous");
            e.HasKey(x => x.OuId);
            e.Property(x => x.OuId).HasColumnName("ou_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Path).HasColumnName("path").HasColumnType("ltree").IsRequired();
            e.Property(x => x.PolicyOverrideJson).HasColumnName("policy_override").HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.Path }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany(t => t.VirtualOus).HasForeignKey(x => x.TenantId);
        });

        modelBuilder.Entity<EnrollmentToken>(e =>
        {
            e.ToTable("enrollment_tokens");
            e.HasKey(x => x.TokenId);
            e.Property(x => x.TokenId).HasColumnName("token_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.OuId).HasColumnName("ou_id");
            e.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.MaxUses).HasColumnName("max_uses");
            e.Property(x => x.UseCount).HasColumnName("use_count");
        });

        modelBuilder.Entity<FleetEndpoint>(e =>
        {
            e.ToTable("endpoints");
            e.HasKey(x => x.AgentUuid);
            e.Property(x => x.AgentUuid).HasColumnName("agent_uuid");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.OuId).HasColumnName("ou_id");
            e.Property(x => x.Hostname).HasColumnName("hostname").IsRequired();
            e.Property(x => x.HardwareHash).HasColumnName("hardware_hash").IsRequired();
            e.Property(x => x.AgentSecretHash).HasColumnName("agent_secret_hash").IsRequired();
            e.Property(x => x.IsDomainJoined).HasColumnName("is_domain_joined");
            e.Property(x => x.AdOuDn).HasColumnName("ad_ou_dn");
            e.Property(x => x.OsVersion).HasColumnName("os_version");
            e.Property(x => x.AgentVersion).HasColumnName("agent_version");
            e.Property(x => x.HorizonClientVersion).HasColumnName("horizon_client_version");
            e.Property(x => x.RustdeskId).HasColumnName("rustdesk_id");
            e.Property(x => x.LocalIp).HasColumnName("local_ip");
            e.Property(x => x.PublicIp).HasColumnName("public_ip");
            e.Property(x => x.MacAddress).HasColumnName("mac_address");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
            e.HasIndex(x => new { x.TenantId, x.HardwareHash }).IsUnique();
        });

        modelBuilder.Entity<ManagedInstance>(e =>
        {
            e.ToTable("managed_instances");
            e.HasKey(x => x.InstanceId);
            e.Property(x => x.InstanceId).HasColumnName("instance_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.AgentUuid).HasColumnName("agent_uuid");
            e.Property(x => x.BrokerFqdn).HasColumnName("broker_fqdn").IsRequired();
            e.Property(x => x.HorizonUser).HasColumnName("horizon_user");
            e.Property(x => x.RootPid).HasColumnName("root_pid");
            e.Property(x => x.JobObjectName).HasColumnName("job_object_name").IsRequired();
            e.Property(x => x.State).HasColumnName("state").IsRequired();
            e.Property(x => x.SeamlessWindowsJson).HasColumnName("seamless_windows").HasColumnType("jsonb");
            e.Property(x => x.ChildProcessesJson).HasColumnName("child_processes").HasColumnType("jsonb");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.AuditId);
            e.Property(x => x.AuditId).HasColumnName("audit_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.ActorType).HasColumnName("actor_type");
            e.Property(x => x.ActorId).HasColumnName("actor_id");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.TargetType).HasColumnName("target_type");
            e.Property(x => x.TargetId).HasColumnName("target_id");
            e.Property(x => x.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<ProcessedCommand>(e =>
        {
            e.ToTable("processed_commands");
            e.HasKey(x => x.CommandId);
            e.Property(x => x.CommandId).HasColumnName("command_id");
            e.Property(x => x.AgentUuid).HasColumnName("agent_uuid");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.Success).HasColumnName("success");
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.InstanceId).HasColumnName("instance_id");
        });

        modelBuilder.Entity<HorizonBroker>(e =>
        {
            e.ToTable("horizon_brokers");
            e.HasKey(x => x.BrokerId);
            e.Property(x => x.BrokerId).HasColumnName("broker_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.BaseUrl).HasColumnName("base_url").IsRequired();
            e.Property(x => x.PublicFqdn).HasColumnName("public_fqdn").IsRequired();
            e.Property(x => x.EncryptedCredentials).HasColumnName("encrypted_credentials");
            e.Property(x => x.PollIntervalSec).HasColumnName("poll_interval_sec");
            e.Property(x => x.LastPollAt).HasColumnName("last_poll_at");
            e.Property(x => x.LastPollError).HasColumnName("last_poll_error");
        });

        modelBuilder.Entity<BrokerSessionSnapshot>(e =>
        {
            e.ToTable("broker_session_snapshots");
            e.HasKey(x => x.SnapshotId);
            e.Property(x => x.SnapshotId).HasColumnName("snapshot_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.BrokerId).HasColumnName("broker_id");
            e.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
            e.Property(x => x.UserName).HasColumnName("user_name");
            e.Property(x => x.DnsName).HasColumnName("dns_name");
            e.Property(x => x.BrokerFqdn).HasColumnName("broker_fqdn");
            e.Property(x => x.ObservedAt).HasColumnName("observed_at");
            e.HasIndex(x => new { x.BrokerId, x.SessionId });
        });
    }
}
