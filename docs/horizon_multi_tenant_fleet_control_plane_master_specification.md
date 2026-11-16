# Horizon Multi-Tenant Fleet Control Plane & Self-Healing Watchdog Platform
**Master Architectural, Functional & AI Engineering Specification**

---

## 1. Executive Summary & Strategic Vision

This project is a self-hosted, multi-tenant **End-User Computing (EUC) Orchestration, Telemetry, Remote Assistance, and Automated Remediation Platform** built specifically for **VMware / Omnissa Horizon environments publishing RDSH Applications** (rather than full VDI desktops) across **mixed domain-joined and non-domain (workgroup / remote / kiosk) Windows endpoints**.

### Why This Platform Exists (Operational Gaps Solved)
While native Horizon tools (Horizon Console, Horizon Help Desk Tool) provide basic server-side visibility and enterprise add-ons (Workspace ONE DEX, ControlUp) offer heavy monitoring suites, critical operational gaps persist in real-world RDSH application deployments:
1. **Endpoint Blindness:** Server-side tools cannot detect when the local `vmware-view.exe` or `horizon-client.exe` UI thread or `remotemks.exe` display engine deadlocks on a user's workstation.
2. **Non-Domain / Workgroup Endpoints:** Branch kiosks, remote contractor laptops, and workgroup Windows PCs lack Active Directory Group Policy (GPO), SCCM, or Intune management, making deployment, organizational grouping, and credential handling difficult.
3. **Lack of Per-Instance Process Isolation:** During outages or freezes, users routinely panic-click shortcuts and spawn multiple concurrent Horizon Client processes—or legitimately connect to multiple Horizon Connection Servers simultaneously. Killing `vmware-view.exe` or `remotemks.exe` by global process name destroys **all** active sessions on that workstation.
4. **All-or-Nothing Session Disconnects on RDSH:** Because published applications multiplex over a single local Blast/PCoIP stream, blindly restarting the Horizon Client when one app (e.g., an ERP client) hangs severs healthy remote apps (e.g., Outlook, Excel) unnecessarily.
5. **Fragmented Tooling:** Teams lack a unified, self-hosted control plane that combines automated self-healing, session/app lifecycle control, workstation power & Wake-on-LAN (WoL) management, centralized logging, time-series metrics, Over-the-Air (OTA) binary updates, and on-demand remote desktop assistance.

---

## 2. End-to-End System Architecture & Network Topology

```text
+---------------------------------------------------------------------------------------------------+
|                           WINDOWS WORKSTATION (Domain-Joined OR Workgroup)                        |
|                                                                                                   |
|  +---------------------------------------------+   +-------------------------------------------+  |
|  | HorizonWatchdog.Tray.exe (User Session)     |   | HorizonWatchdog.Service.exe (Session 0)   |  |
|  | - Discovers Horizon Client root PIDs        |   | - Runs as LocalSystem (survives logoffs)  |  |
|  | - Wraps each valid instance in a Win32      |   | - Persistent SignalR / HTTPS Control Loop |  |
|  |   Job Object (isolates remotemks, USB, etc.)|   | - Offline Policy Cache & DPAPI Secrets    |  |
|  | - Checks per-window responsiveness via      |<=>| - OS Power Control & Subnet WoL Relay     |  |
|  |   EnumWindows + SendMessageTimeout          |IPC| - Windows Event Log Collector             |  |
|  | - Tails local VDM/Blast protocol logs       |   | - Package Manager (RustDesk + OTA Sync)   |  |
|  | - Enforces Anti-Panic-Click deduplication   |   | - Spawns HorizonWatchdog.Updater.exe      |  |
|  +---------------------------------------------+   +---------------------+---------------------+  |
+--------------------------------------------------------------------------|------------------------+
                                                                           | TLS 1.3 (Port 8443/443)
                                                                           | Internet OR VPN Backplane
                                                                           v
+---------------------------------------------------------------------------------------------------+
|                             LINUX SERVER (Containerized Control Plane)                            |
|                                                                                                   |
|  +---------------------------------------------------------------------------------------------+  |
|  | Caddy 2 Reverse Proxy (Edge Ingress & Security Router)                                      |  |
|  | - /api/v1/agent/* & /hubs/agent  ──► Open to Internet + VPN (Zero-Trust Device Auth)       |  |
|  | - /* (Web GUI) & /api/v1/external/* ──► Restricted to VPN / LAN Admin Subnets              |  |
|  +----------------------------------------------+----------------------------------------------+  |
|                                                 |                                                 |
|         +---------------------------------------+---------------------------------------+         |
|         v                                                                               v         |
|  +-----------------------------------------------+   +-----------------------------------------+  |
|  | HorizonControl.Api (.NET 8 Web API + SignalR) |   | HorizonControl.Web (React + TypeScript) |  |
|  | - Multi-Tenant RBAC & Enrollment Engine       |   | - Multi-Tenant Switcher & Admin Console |  |
|  | - Virtual OU Policy Inheritance Engine        |<->| - Virtual OU Tree & Live Fleet Map      |  |
|  | - Correlation Engine (Endpoint <-> Broker)    |   | - Host & Per-Instance Process Drilldown |  |
|  | - Horizon Connection Server REST Poller       |   | - Centralized Log & Metrics Explorer    |  |
|  | - Northbound OpenAPI / Swagger Gateway        |   | - Embedded RustDesk / One-Click Assist  |  |
|  +--+--------+--------+--------+--------+--------+   +-----------------------------------------+  |
|     |        |        |        |        |                                                         |
|     v        v        v        v        v                                                         |
|  [Postgres] [Redis] [Victoria] [Loki]  [MinIO]   +---------------------+  +--------------------+  |
|  (ltree +   (TTL &  (Metrics)  (Logs)  (OTA &    | Horizon Connection  |  | Self-Hosted        |  |
|   RLS DB)   Queues)                    Bundles)  | Server REST API     |  | RustDesk hbbs/hbbr |  |
|                                                  | (Over VPN/LAN)      |  | (On-Demand Assist) |  |
|                                                  +---------------------+  +--------------------+  |
+---------------------------------------------------------------------------------------------------+
```

### Transport & Edge Routing Design
* **Single Multiplexed TLS 1.3 Port (`8443` or `443`):** All agent communication (enrollment, SignalR bi-directional WebSockets/HTTP2, REST polling fallback, metrics, logs, and OTA binary downloads) runs over standard HTTPS/TLS so branch firewalls, ISP NATs, and proxies pass traffic without requiring custom raw TCP ports.
* **Path-Based Edge Security (`Caddy 2`):**
  * `/api/v1/agent/*` and `/hubs/agent` are reachable from both **Internet** and **VPN** endpoints, protected by Zero-Trust per-device authentication tokens.
  * `/*` (Admin Web GUI), `/swagger/*`, and `/api/v1/external/*` (Northbound REST API) are restricted via reverse-proxy IP allowlists to trusted corporate **VPN/LAN subnets**.
  * Internet-facing endpoints **never** talk directly to internal Horizon Connection Server management APIs; only the Linux Control Plane container reaches Horizon brokers over the internal VPN/LAN backplane.

---

## 3. Multi-Tenancy & Non-Domain Organizational Model

### Strict Multi-Tenant Isolation
Designed for MSPs, multi-subsidiary enterprises, or isolated business units hosted on a single Linux control plane:
1. **Cryptographic Enrollment Binding:** Group enrollment tokens map strictly to `(tenant_id + virtual_ou_id)`. Upon enrollment, the agent receives a unique 256-bit `agent_secret` bound to its `tenant_id` and `agent_uuid`. Every API request and SignalR frame derives `tenant_id` exclusively from server-verified claims—never trusting client-supplied tenant IDs.
2. **Database Row-Level Security (PostgreSQL RLS):** RLS policies enforce `tenant_id = current_setting('app.current_tenant')::uuid` across all relational tables.
3. **Observability & Storage Partitioning:**
   * **Grafana Loki:** Multi-tenant isolation via `X-Scope-OrgID: <tenant_id>`.
   * **VictoriaMetrics:** Every time-series metric is tagged with `tenant_id`.
   * **MinIO (S3):** OTA packages and diagnostic bundles are isolated under tenant-prefixed object paths.
4. **Tenant-Scoped Connectors:** Each tenant configures its own Horizon Connection Server REST API connectors (credentials encrypted at rest with AES-256-GCM) and its own RustDesk relay keys.

### Non-Domain Device Identity & Virtual OUs
* **Immutable Hardware Fingerprint:** During installation, the agent generates a persistent `agent_uuid` stored in `HKLM\SOFTWARE\HorizonWatchdog` paired with a SHA-256 hash of the motherboard SMBIOS UUID (`Win32_ComputerSystemProduct.UUID`). Renaming a workgroup PC or changing its IP address never creates duplicate records.
* **Hierarchical Virtual OUs (PostgreSQL `ltree`):** Admins organize endpoints into arbitrary trees (e.g., `acme.latam.bogota.callcenter`). Policies inherit from parent OUs down to child OUs with granular JSON overrides.
* **Three Group Assignment Mechanisms:**
  1. *Zero-Touch Enrollment Tokens:* Passing `TOKEN=tok_callcenter` to the MSI automatically assigns the machine to that Virtual OU on first check-in.
  2. *Dynamic Auto-Tagging Rules:* Automatically routes unassigned endpoints into Virtual OUs based on local subnet/CIDR, public egress IP, hostname prefix/regex, or Active Directory Distinguished Name (when domain-joined).
  3. *Manual Drag-and-Drop:* Admins can reassign endpoints across Virtual OUs in the Web GUI at any time, pushing updated policies immediately.

---

## 4. Consolidated Windows Endpoint Agent & Bundled Installer

### Single Signed MSI Package (`HorizonWatchdogSetup.msi`)
Packaged with **WiX Toolset v4** for silent one-command deployment across both domain and workgroup machines:
```powershell
msiexec /i HorizonWatchdogSetup.msi /qn SERVER="https://ctrl.corp.com:8443" TOKEN="tok_tnt01_callcenter"
```
All binaries and managed packages live under a single directory (`C:\Program Files\HorizonWatchdog\`) and uninstall cleanly in one action:
```text
C:\Program Files\HorizonWatchdog\
 ├── HorizonWatchdog.Service.exe    # Session-0 Windows Service (LocalSystem)
 ├── HorizonWatchdog.Tray.exe       # Interactive User-Session Monitor & Job Object Manager
 ├── HorizonWatchdog.Updater.exe    # Atomic binary swapper for safe OTA self-updates
 └── packages\                      # Agent-managed payload repository
      ├── rustdesk\
      │    └── rustdesk.exe         # Bundled self-hosted RustDesk binary
      └── horizon\                  # Optional cached Horizon Client installer for disaster self-repair
```

### Split-Process Windows Architecture (ACL-Secured Named Pipe IPC)
1. **`HorizonWatchdog.Service.exe` (Session 0 — `LocalSystem`):**
   * Runs continuously regardless of user logon state.
   * Encrypts `agent_secret` at rest using Windows **DPAPI (`DataProtectionScope.LocalMachine`)** and caches the active Virtual OU policy locally (`%ProgramData%\HorizonWatchdog\policy.json`) so self-healing continues working during internet outages.
   * Maintains the persistent **SignalR** control channel (with automatic REST polling fallback).
   * Executes privileged OS power commands, acts as a subnet **Wake-on-LAN (WoL) Relay**, collects Windows Event Logs, orchestrates `rustdesk.exe`, and manages OTA package syncs.
2. **`HorizonWatchdog.Tray.exe` (Interactive User Session — Launched per logged-in user):**
   * Communicates with `HorizonWatchdog.Service.exe` over an ACL-secured **Windows Named Pipe** (`\\.\pipe\HorizonWatchdogIPC`) using length-prefixed JSON frames.
   * Manages Win32 Job Objects, inspects seamless GUI windows, tails user-profile VDM/Blast logs, displays native Windows Toast alerts, and launches/restarts Horizon Client instances inside the user's desktop context without ever passing passwords via CLI arguments.
3. **`HorizonWatchdog.Updater.exe`:**
   * Spawned asynchronously by the Service during OTA updates; verifies SHA-256 checksums and Authenticode signatures, stops the Service, swaps locked `.exe` binaries, and restarts the Service.

---

## 5. Per-Instance Process Isolation & RDSH App Hang Detection

### A. Win32 Job Object Isolation (Zero Cross-Session Collateral Damage)
* When `HorizonWatchdog.Tray.exe` discovers a valid `vmware-view.exe` or `horizon-client.exe` root process, it assigns that PID to a dedicated **Win32 Job Object** (`CreateJobObject` + `AssignProcessToJobObject`).
* Every child process spawned by that instance—`remotemks.exe` (Blast/PCoIP display engine), `vmware-usbd.exe`, clipboard/smartcard virtual channels, and seamless window hosts—automatically inherits membership in that exact Job Object.
* When the Control Plane or local watchdog executes `RESTART_CLIENT_INSTANCE` or `KILL_CLIENT_INSTANCE` for `instance_id: inst_01`, the agent invokes `TerminateJobObject` on that specific handle—atomically terminating **only** `inst_01`'s entire process tree while leaving sibling Horizon Client sessions (`inst_02`) completely untouched.

### B. Session Signatures & "Anti-Panic-Click" Deduplication
Every client instance is fingerprinted by `Signature = {Broker_FQDN} + {Horizon_User} + {Target_App}`:
* **Different Signature** (e.g., connecting to a second Connection Server or a distinct valid app pool): Automatically registered in the Control Plane as an independent Managed Instance with its own `instance_id` and Job Object.
* **Identical Signature** (user accidentally double-clicks the same shortcut repeatedly during an outage): Governed by the Virtual OU policy:
  * `Allow`: Permits multiple identical instances.
  * `Focus_Existing`: Terminates the duplicate PID and brings the healthy existing window to the foreground with a Windows Toast alert.
  * `Replace_If_Hung` *(Default)*: If the original instance is hung, terminates the frozen Job Object and allows the fresh instance to connect cleanly.

### C. Per-Window Seamless RDSH Inspection & Cross-Layer Correlation
1. **Client-Side Seamless Window Checks:** `HorizonWatchdog.Tray.exe` enumerates all top-level windows (`EnumWindows`), maps each `HWND` to its owning Job Object (`GetWindowThreadProcessId` -> `IsProcessInJob`), and tests UI responsiveness via `SendMessageTimeout(hwnd, WM_NULL, SMTO_ABORTIFHUNG, 2000)`. Requires **3 consecutive failures** (e.g., over 20 seconds) to prevent false positives. Identifies the exact frozen published app window title (e.g., `"SAP ERP"`) while sibling apps (`"Microsoft Excel"`) remain healthy.
2. **Local Protocol Log Tailing:** Tails `%LOCALAPPDATA%\VMware\VDM\logs\` (and Omnissa paths) for Blast heartbeat timeouts, SSL/tunnel drops, and packet loss spikes.
3. **Central Control Plane Correlation:** When an instance reports `DEGRADED_APP_HANG`, the Control Plane cross-references the user's session against the tenant's **Horizon Connection Server REST API** (`/rest/monitor/v1/sessions`):
   * *Local Blast/UI Deadlock:* Control Plane dispatches `RESTART_CLIENT_INSTANCE` targeting only that `instance_id`.
   * *Remote RDSH Session/App Deadlock:* Control Plane calls the Horizon REST API to reset/logoff only that backend RDSH session without killing healthy local client instances.

---

## 6. Complete Add-On Modules & Operational Capabilities

| Module | Key Functions & Technical Workflows |
| :--- | :--- |
| **1. Session & App Lifecycle Control** | • Live inventory of all active client instances per PC (PIDs, child processes, Blast RTT/FPS, seamless window states).<br>• **Pinned Apps Policy:** Auto-launch critical published apps on user logon or trigger ad-hoc app launches from the Web GUI (using cached broker URLs/usernames, Windows Credential Manager, or `-logInAsCurrentUser`—never plain-text CLI passwords).<br>• Surgical actions: `START_APP`, `RESTART_CLIENT_INSTANCE`, `KILL_CLIENT_INSTANCE`, `FLUSH_LOCAL_CACHE`, `RESET_BROKER_SESSION`. |
| **2. Workstation Power & WoL Relay** | • Remote OS power commands: `REBOOT_GRACEFUL` (with customizable user countdown toast), `REBOOT_FORCE`, `SHUTDOWN`, `LOCK_WORKSTATION`.<br>• **Subnet Wake-on-LAN (WoL) Relay:** Clicking *"Power On"* on an offline workstation instructs an online peer agent on the same L2 subnet to broadcast a Magic Packet (`UDP 9`).<br>• **Maintenance Windows:** Cron-scheduled off-hours reboots per Virtual OU. |
| **3. On-Demand Remote Assist (RustDesk)** | • Self-hosted RustDesk containers (`hbbs` + `hbbr`) run inside the Linux Control Plane stack.<br>• **Zero Idle Overhead:** `rustdesk.exe` remains stopped on endpoints during normal operation.<br>• **Ephemeral Session Flow:** When an admin clicks *"Remote Assist"*, the Control Plane dispatches `START_REMOTE_ASSIST`, the agent configures `rustdesk.exe` with the tenant's relay key, generates a random 12-character one-time password, returns `{ rustdesk_id, one_time_password }` to the admin UI, and stops the service + rotates the password upon disconnect or 30-minute TTL expiration. |
| **4. OTA Package & Binary Manager** | • Admins upload vetted versions of `HorizonWatchdog`, `rustdesk.exe`, or a full **Horizon Client MSI** to MinIO and assign target versions per Virtual OU.<br>• Agents download over HTTPS, verify **SHA-256 checksums** and Authenticode signatures, and update silently via `HorizonWatchdog.Updater.exe`.<br>• **Disaster Self-Repair:** If Horizon Client DLLs/drivers become corrupted and process restarts fail repeatedly, the agent executes a silent repair/reinstall using the cached package in `packages\horizon\`. |
| **5. Centralized Log & Bundle Pipeline** | • Agents batch structured JSON logs every 5 seconds to `/api/v1/agent/logs` -> **Grafana Loki** (covering self-healing actions, Job Object terminations, filtered VDM/Blast errors, Windows Event IDs `1000`/`1002`/`41`, and immutable RBAC audit trails).<br>• **On-Demand Diagnostic Bundles:** One click archives raw VDM logs, `.evtx` exports, socket tables, and process snapshots into a `.zip` uploaded to **MinIO** for download in the Web GUI. |
| **6. Time-Series Metrics & Alerting** | • Streams workstation CPU %, RAM %, Disk Queue, network RTT/packet loss, and per-instance Blast latency/FPS/UI response ms to **VictoriaMetrics**.<br>• Outbound alert webhooks (Slack, Microsoft Teams, ServiceNow, Jira, PagerDuty) on OU-wide hang spikes or latency threshold breaches. |
| **7. Dual REST API Integration** | • **Southbound:** Control Plane polls Horizon Connection Server `/rest/monitor/v1/sessions` and `/rest/inventory/v1/machines` over internal VPN/LAN.<br>• **Northbound:** Full OpenAPI 3.1 / Swagger REST API (`/api/v1/external/*`) secured by tenant API keys for ITSM/monitoring automation. |

---

## 7. Multi-Tenant PostgreSQL Database Schema

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
    path LTREE NOT NULL, -- e.g., 'root.latam.bogota.callcenter'
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
    base_url TEXT NOT NULL, -- Internal VPN/LAN URL for REST API polling
    public_fqdn TEXT NOT NULL, -- FQDN clients connect to (for correlation)
    encrypted_credentials BYTEA NOT NULL, -- AES-256-GCM encrypted service account
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
    state TEXT NOT NULL, -- HEALTHY | APP_HUNG | BLAST_STALLED | RECOVERING | TERMINATED
    seamless_windows JSONB NOT NULL DEFAULT '[]'::jsonb,
    child_processes JSONB NOT NULL DEFAULT '[]'::jsonb,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE audit_logs (
    audit_id BIGSERIAL PRIMARY KEY,
    tenant_id UUID NOT NULL,
    actor_type TEXT NOT NULL, -- ADMIN_USER | AUTO_POLICY | EXTERNAL_API
    actor_id TEXT NOT NULL,
    action TEXT NOT NULL, -- RESTART_INSTANCE | REBOOT_OS | START_REMOTE_ASSIST | POLICY_UPDATE
    target_type TEXT NOT NULL, -- ENDPOINT | INSTANCE | VIRTUAL_OU
    target_id UUID NOT NULL,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

---

## 8. Inherited Virtual OU Policy Schema

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
      { "broker_url": "https://vdi.acme.com", "app_name": "SAP_ERP", "auto_launch": true }
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

---

## 9. Containerized Linux Deployment Stack

The entire server-side Control Plane runs on a Linux host (4 vCPU / 8 GB RAM baseline) via Docker Compose:
1. **`caddy` (Caddy 2):** TLS 1.3 ingress on port `8443`/`443` with path-based Internet vs. VPN subnet routing.
2. **`control-api` (.NET 8 Web API + SignalR):** Core multi-tenant backend, policy inheritance engine, SignalR hub, and Horizon Broker REST poller.
3. **`web-gui` (Nginx + React 18 / TypeScript SPA):** Multi-tenant admin dashboard (Tenant switcher, Virtual OU tree, Live Endpoint/Instance drilldown, VictoriaMetrics charts, Loki Log Explorer, Policy Editor, Swagger UI).
4. **`postgres` (PostgreSQL 16 + `ltree` + RLS):** Relational state, Virtual OUs, policies, devices, instances, and audit logs.
5. **`redis` (Redis 7):** 45-second TTL heartbeat presence keys, SignalR backplane, and command queues.
6. **`victoriametrics`:** High-compression time-series telemetry database.
7. **`loki` (Grafana Loki):** Multi-tenant structured log aggregation.
8. **`minio`:** S3-compatible object storage for signed OTA binaries and diagnostic `.zip` bundles.
9. **`rustdesk-hbbs` & `rustdesk-hbbr`:** Self-hosted RustDesk ID/Rendezvous and encrypted relay servers.

---

## 10. Repository Layout & Cursor AI Engineering Guardrails

### Repository Structure
```text
horizon-fleet-control/
├── .cursor/rules/                      # Cursor AI rules (*.mdc) — invariants & execution guardrails
├── docs/
│   └── horizon_multi_tenant_fleet_control_plane_master_specification.md  # This document
├── src/
│   ├── HorizonControl.Shared/          # Shared .NET 8 DTOs, Policy models, SignalR & IPC contracts
│   ├── HorizonControl.Api/             # Linux containerized .NET 8 Web API, SignalR Hub, Horizon Poller
│   ├── HorizonWatchdog.Service/        # Windows Session-0 Service (Enrollment, IPC, Power, WoL, OTA)
│   ├── HorizonWatchdog.Tray/           # Windows User-Session App (Job Objects, HWND Hang Check, Logs)
│   ├── HorizonWatchdog.Updater/        # Atomic binary swapper for self-updates
│   └── HorizonControl.Web/             # React + TypeScript Multi-Tenant Admin Web GUI
├── installer/
│   └── HorizonWatchdogSetup.wxs        # WiX v4 MSI packaging definition (bundles Service, Tray, RustDesk)
└── deploy/
    ├── docker-compose.yml              # Full production Linux stack
    ├── Caddyfile                       # TLS 1.3 ingress + Internet/VPN path-based access rules
    └── loki-config.yml                 # Multi-tenant Loki storage & retention configuration
```

### Mandatory AI Agent Development Rules (For `.cursor/rules/*.mdc`)
Project rules live under `.cursor/rules/` (not legacy root `.cursorrules`):
- `core-invariants.mdc` / `agent-execution.mdc` — always apply
- `shared-contracts.mdc`, `api-linux.mdc`, `windows-agent.mdc`, `web-frontend.mdc`, `deploy-infra.mdc` — path-scoped

1. **Contract-First Discipline:** Always define or update shared DTOs, Policy models, SignalR hub contracts, and Named Pipe IPC messages inside `src/HorizonControl.Shared` and verify `dotnet build` **before** implementing producer or consumer code.
2. **Strict OS Boundary Isolation:** `src/HorizonControl.Api` targets Linux (`net8.0`) and must **never** reference `System.Windows.*`, Win32 P/Invoke, or DPAPI. Windows-specific APIs belong exclusively in `HorizonWatchdog.Service`, `HorizonWatchdog.Tray`, and `HorizonWatchdog.Updater` (`net8.0-windows`).
3. **Atomic Vertical Slices & Build Verification:** Implement one cohesive vertical slice per turn and verify with `dotnet build` (C#), `tsc --noEmit && npm run build` (React/TS), or `docker compose config` (Infra) before concluding.
4. **Defensive Win32 & Network Engineering:** Wrap all Win32 native handles (`CreateJobObject`, `OpenProcess`) in `SafeHandle` subclasses; check `Marshal.GetLastWin32Error()`; enforce `CancellationToken` timeouts and exponential backoff with jitter on all external calls; use `ILogger<T>` structured templates exclusively.
5. **Recommended Implementation Sequence:**
   * **Step 1:** `HorizonControl.Shared` (DTOs, Policies, SignalR & IPC contracts)
   * **Step 2:** `deploy/` (`docker-compose.yml`, `Caddyfile`, `loki-config.yml`) — MVP may start with Caddy + API + Postgres only
   * **Step 3:** `HorizonControl.Api` (PostgreSQL `ltree` + RLS migrations, Enrollment, SignalR Hub, Horizon REST poller, Loki/VictoriaMetrics/MinIO forwarders)
   * **Step 4:** `HorizonWatchdog.Service`, `HorizonWatchdog.Tray`, & `HorizonWatchdog.Updater` (Win32 Job Objects, `SendMessageTimeout` hang detector, Named Pipe IPC, WoL relay, RustDesk/OTA manager)
   * **Step 5:** `HorizonControl.Web` (React + TypeScript Multi-Tenant Admin Console) & `installer/HorizonWatchdogSetup.wxs` (WiX v4 MSI).