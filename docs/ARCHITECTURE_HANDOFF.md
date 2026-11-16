# Horizon Multi-Tenant Fleet Control Plane & Watchdog Agent - Architecture Handoff

## 1. System Overview & Topology
A containerized Linux Central Control Plane + Consolidated Windows Endpoint Agent designed to monitor, heal, and orchestrate **VMware / Omnissa Horizon Published Application (RDSH)** sessions across **domain-joined and non-domain (workgroup) Windows workstations**.

### Network & Container Topology
- **Edge Ingress (Caddy 2 on Linux Host - Port 8443/443 TLS 1.3):**
  - **Public + VPN Route:** `/api/v1/agent/*` & `/hubs/agent` (Agent enrollment, SignalR persistent bi-directional control channel, heartbeats, metrics, logs).
  - **VPN / LAN Restricted Route:** `/*` (React Admin SPA), `/swagger/*`, `/api/v1/external/*` (Northbound REST API for ServiceNow/Zabbix/automation).
- **Backend Containers:**
  1. `control-api`: .NET 8 Web API + SignalR Hub + Background Horizon Connection Server REST Poller.
  2. `web-gui`: Nginx serving React/TypeScript SPA.
  3. `postgres`: PostgreSQL 16 (`ltree` + Row-Level Security).
  4. `redis`: Ephemeral presence keys (`agent:alive:{tenant}:{agent_id}` with 45s TTL) + SignalR backplane.
  5. `victoriametrics`: Time-series metrics store.
  6. `loki`: Multi-tenant structured log aggregation (`X-Scope-OrgID: {tenant_id}`).
  7. `minio`: S3-compatible storage for signed OTA agent/RustDesk/Horizon binaries & diagnostic bundles.
  8. `rustdesk-hbbs` & `rustdesk-hbbr`: Self-hosted RustDesk ID/Relay containers for on-demand remote assistance.

## 2. Consolidated Windows Agent Architecture
Installed silently via single MSI (`msiexec /i HorizonWatchdogSetup.msi /qn SERVER="https://..." TOKEN="..."`) into `C:\Program Files\HorizonWatchdog\`.

### Binaries & Responsibilities
1. **`HorizonWatchdog.Service.exe` (Session 0 - `LocalSystem`):**
   - Generates immutable `agent_uuid` + hardware fingerprint hash (`Win32_ComputerSystemProduct.UUID`) stored in `HKLM\SOFTWARE\HorizonWatchdog`.
   - Enrolls via `POST /api/v1/agent/enroll`, stores returned `agent_secret` encrypted via Windows **DPAPI (`DataProtectionScope.LocalMachine`)**.
   - Maintains persistent **SignalR** connection (with automatic REST polling fallback) to the Central Controller.
   - Caches active Virtual OU policy locally (`%ProgramData%\HorizonWatchdog\policy.json`) so self-healing functions offline.
   - Executes OS power actions (`REBOOT_GRACEFUL`, `REBOOT_FORCE`, `SHUTDOWN`, `LOCK`), acts as a subnet **Wake-on-LAN (WoL) Relay**, collects Windows Event Logs (`Event ID 1000, 1002, 41`), and manages `packages\rustdesk\rustdesk.exe` + OTA updates.
2. **`HorizonWatchdog.Tray.exe` (Interactive User Session - Launched per logged-in user):**
   - Connects to `HorizonWatchdog.Service.exe` over an ACL-secured Named Pipe (`\\.\pipe\HorizonWatchdogIPC`).
   - Discovers every `vmware-view.exe` / `horizon-client.exe` process in the user session.
   - Assigns each valid client root PID to a dedicated **Win32 Job Object** (`CreateJobObject` + `AssignProcessToJobObject`).
   - Maps all child processes (`remotemks.exe`, USB/clipboard helpers) and seamless published app top-level windows (`EnumWindows` -> `GetWindowThreadProcessId` -> `IsProcessInJob`) to their exact parent `instance_id`.
   - Checks UI responsiveness per seamless window (`SendMessageTimeout` `WM_NULL`) and tails `%LOCALAPPDATA%\VMware\VDM\logs\` (and Omnissa paths) for Blast/PCoIP heartbeat drops.
   - Enforces **Anti-Panic-Click Policy** (`Allow`, `Focus_Existing`, `Replace_If_Hung`) based on Session Signature: `{Broker_FQDN} + {Horizon_User} + {Target_App}`.
3. **`HorizonWatchdog.Updater.exe`:**
   - Spawned by the Service when an OTA update arrives; verifies SHA-256 hash, stops Service, swaps `.exe` binaries, and restarts Service.

## 3. Multi-Tenant PostgreSQL Schema (Core Tables)

```sql
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "ltree";

CREATE TABLE tenants (
    tenant_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name TEXT NOT NULL,
    slug TEXT UNIQUE NOT NULL,
    status TEXT NOT NULL DEFAULT 'ACTIVE',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE virtual_ous (
    ou_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL REFERENCES tenants(tenant_id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    path LTREE NOT NULL,
    policy_override JSONB NOT NULL DEFAULT '{}'::jsonb,
    UNIQUE (tenant_id, path)
);
CREATE INDEX idx_virtual_ous_path ON virtual_ous USING GIST (path);

CREATE TABLE enrollment_tokens (
    token_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL REFERENCES tenants(tenant_id) ON DELETE CASCADE,
    ou_id UUID NOT NULL REFERENCES virtual_ous(ou_id) ON DELETE CASCADE,
    token_hash TEXT UNIQUE NOT NULL,
    expires_at TIMESTAMPTZ,
    max_uses INT,
    use_count INT NOT NULL DEFAULT 0
);

CREATE TABLE horizon_brokers (
    broker_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL REFERENCES tenants(tenant_id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    base_url TEXT NOT NULL,
    public_fqdn TEXT NOT NULL,
    encrypted_credentials BYTEA NOT NULL,
    poll_interval_sec INT NOT NULL DEFAULT 30
);

CREATE TABLE endpoints (
    agent_uuid UUID PRIMARY KEY,
    tenant_id UUID NOT NULL REFERENCES tenants(tenant_id) ON DELETE CASCADE,
    ou_id UUID NOT NULL REFERENCES virtual_ous(ou_id),
    hostname TEXT NOT NULL,
    hardware_hash TEXT NOT NULL,
    is_domain_joined BOOLEAN NOT NULL DEFAULT FALSE,
    ad_ou_dn TEXT,
    os_version TEXT,
    agent_version TEXT,
    horizon_client_version TEXT,
    rustdesk_id TEXT,
    local_ip INET,
    public_ip INET,
    mac_address MACADDR,
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (tenant_id, hardware_hash)
);

CREATE TABLE managed_instances (
    instance_id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL REFERENCES tenants(tenant_id) ON DELETE CASCADE,
    agent_uuid UUID NOT NULL REFERENCES endpoints(agent_uuid) ON DELETE CASCADE,
    broker_fqdn TEXT NOT NULL,
    horizon_user TEXT,
    root_pid INT NOT NULL,
    job_object_name TEXT NOT NULL,
    state TEXT NOT NULL,
    seamless_windows JSONB NOT NULL DEFAULT '[]'::jsonb,
    child_processes JSONB NOT NULL DEFAULT '[]'::jsonb,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE audit_logs (
    audit_id BIGSERIAL PRIMARY KEY,
    tenant_id UUID NOT NULL,
    actor_type TEXT NOT NULL,
    actor_id TEXT NOT NULL,
    action TEXT NOT NULL,
    target_type TEXT NOT NULL,
    target_id UUID NOT NULL,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

## 4. Policy JSON Schema (Inherited via Virtual OU `ltree`)
```json
{
  "policy_version": 1,
  "monitoring": {
    "check_interval_sec": 5,
    "hang_threshold_sec": 20,
    "consecutive_failures_required": 3
  },
  "remediation": {
    "auto_remediate": true,
    "max_restarts_per_hour": 3,
    "flush_cache_after_failed_restarts": 2,
    "notify_user_on_restart": true
  },
  "multi_instance": {
    "duplicate_same_broker_action": "Replace_If_Hung",
    "allow_multi_broker_connections": true,
    "max_concurrent_client_instances": 3
  },
  "lifecycle": {
    "pinned_apps": [
      { "broker_url": "[https://vdi.acme.com](https://vdi.acme.com)", "app_name": "SAP_ERP", "auto_launch": true }
    ],
    "scheduled_reboot_cron": "0 2 * * 0"
  },
  "packages": {
    "desired_agent_version": "1.0.0",
    "desired_rustdesk_version": "1.3.8",
    "rustdesk_on_demand_only": true
  }
}
```

## 5. Control Plane Correlation & Command Flows

### A. Cross-Layer RDSH App Hang Correlation
1. **Endpoint Tray** detects `HWND "SAP ERP"` inside `Job_4120` (`instance_id: inst_01`) failed `SendMessageTimeout` 3x while sibling `Job_9102` (`instance_id: inst_02`) is healthy.
2. **Endpoint Service** emits `InstanceDegradedEvent` over SignalR for `inst_01` only.
3. **Central Control Plane** queries the tenant matching `horizon_broker` (`/rest/monitor/v1/sessions`) for `horizon_user` + `broker_fqdn`:
   - **If Broker/RDSH shows healthy session & local Blast logs show packet drop:** Control Plane sends `RESTART_CLIENT_INSTANCE` targeting `inst_01`. Endpoint calls `TerminateJobObject` on `Job_4120` and relaunches only `inst_01` (preserving `inst_02`).
   - **If RDSH app process is deadlocked on server:** Control Plane calls Horizon REST API to reset/logoff the stuck RDSH session (or triggers targeted process kill) and notifies the endpoint.

### B. Ephemeral RustDesk Remote Assist Flow
1. Helpdesk Admin clicks **"Remote Assist"** on `agent_uuid` in Web GUI.
2. Control Plane generates a one-time 12-char password and sends `START_REMOTE_ASSIST` command via SignalR.
3. `Watchdog.Service.exe` configures `packages\rustdesk\rustdesk.exe` with the tenant self-hosted `hbbs/hbbr` relay key, sets the ephemeral password, starts the service, and returns `{ "rustdesk_id": "...", "one_time_password": "..." }`.
4. Admin connects via `rustdesk://<id>` or embedded web viewer; upon disconnect or 30-min TTL expiration, Service stops RustDesk and rotates the password.

## 6. Recommended Build Order in Cursor AI
1. **`HorizonControl.Shared`**: Shared C# DTOs, Policy models, SignalR hub interfaces, and Named Pipe IPC contracts.
2. **`HorizonControl.Api`**: .NET 8 Web API, PostgreSQL + `ltree` + RLS migrations, Enrollment/Auth, SignalR Agent Hub, Loki/VictoriaMetrics ingestion forwarders, and Horizon Connection Server REST client.
3. **`HorizonWatchdog.Service` & `HorizonWatchdog.Tray`**: Windows .NET 8 Worker Service, Named Pipe IPC server/client, Win32 Job Object manager, `EnumWindows` / `SendMessageTimeout` hang detector, VDM log tailer, WoL relay, and RustDesk/OTA package manager.
4. **`HorizonControl.Web`**: React + TypeScript Admin Console (Tenant switcher, Virtual OU tree, Live Endpoint & Instance drill-down, Log Explorer, Policy Editor, Swagger UI).
5. **`deploy/`**: `docker-compose.yml`, `Caddyfile`, and WiX v4 MSI installer definition (`HorizonWatchdogSetup.wxs`).
