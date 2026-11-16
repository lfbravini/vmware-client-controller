# Horizon Multi-Tenant Fleet Control Plane & Self-Healing Watchdog Agent

> Self-hosted, multi-tenant EUC orchestration and automated remediation for **VMware / Omnissa Horizon Published Applications (RDSH)** on domain-joined and workgroup Windows endpoints.

## Gaps addressed

1. Local Horizon Client / seamless HWND hangs  
2. Workgroup endpoints without AD GPOs (Virtual OUs)  
3. Per-instance Win32 Job Object isolation (no global process-name kills)  
4. Surgical `RESTART_CLIENT_INSTANCE` by `instance_id`  
5. (Post-MVP) WoL, OTA, RustDesk, Loki/VictoriaMetrics — see [`docs/MVP_NON_GOALS.md`](docs/MVP_NON_GOALS.md)

## Repository layout

```text
.cursor/rules/           # Cursor AI rules (*.mdc)
docs/                    # Master spec, handoff, MVP non-goals
src/HorizonControl.Shared/
src/HorizonControl.Api/
src/HorizonWatchdog.Service/
src/HorizonWatchdog.Tray/
src/HorizonWatchdog.FakeClient/   # Lab hang harness
src/HorizonWatchdog.Updater/      # Deferred
src/HorizonControl.Web/           # Deferred
deploy/                  # Caddy + API + Postgres MVP compose
```

North star: [`docs/horizon_multi_tenant_fleet_control_plane_master_specification.md`](docs/horizon_multi_tenant_fleet_control_plane_master_specification.md).

## Build

```powershell
dotnet build HorizonFleetControl.sln -c Release
```

Requires .NET 8 SDK. CI: [`.github/workflows/build.yml`](.github/workflows/build.yml).

## MVP lab (Caddy + API + Postgres + agent)

### Invariants

- Agents reach the **controller over the Internet and/or VPN** (`HORIZON_SERVER` / MSI `SERVER=`). They never need routes to Horizon brokers or Postgres.
- Edge for MVP is **Caddy only**. Cloudflare Tunnel is a post-MVP option.
- Prefer **lab HTTP** (or a trusted Caddy CA) so Windows agents do not fail enroll on untrusted HTTPS.
- Future rename to `horizon-fleet-control` is recommended (path spaces hurt CLI/docker).

### 1. Start control plane

```powershell
docker compose -f deploy/docker-compose.yml up --build
```

Or run API against a local Postgres 16 with `ltree` enabled, using connection string in `src/HorizonControl.Api/appsettings.json`.

- Edge/Swagger (via Caddy): `http://localhost:8080/swagger`  
- Admin header: `X-Admin-Api-Key: lab-admin-key-change-me`  
- Lab enrollment token: `lab-enroll-token-change-me` (hash only stored in DB)

### 2. Swagger admin loop

1. Open Swagger → authorize with `X-Admin-Api-Key`.  
2. `GET /api/v1/admin/endpoints` — empty until an agent enrolls.  
3. After agent + FakeClient: `GET /api/v1/admin/instances`.  
4. `POST /api/v1/admin/instances/restart` with `{ "instanceId": "<guid>", "ttlSec": 120 }`.

### 3. Run Windows agent (lab)

Elevated shell recommended (HKLM + Job Objects):

```powershell
$env:HORIZON_SERVER = "http://localhost:8080"
$env:HORIZON_ENROLLMENT_TOKEN = "lab-enroll-token-change-me"
dotnet run --project src/HorizonWatchdog.Service -c Release
dotnet run --project src/HorizonWatchdog.Tray -c Release
dotnet run --project src/HorizonWatchdog.FakeClient -c Release
```

1. FakeClient window appears — Tray assigns it to a Job Object and reports inventory via Named Pipe → Service → SignalR/HTTP.  
2. Click **Simulate UI hang** — after 3 consecutive `SendMessageTimeout` failures, Tray emits `InstanceDegraded`.  
3. With auto-remediate policy (default), Service issues `RESTART_CLIENT_INSTANCE` for that `instance_id` only → `TerminateJobObject`.  
4. Or trigger the same restart from Swagger admin.

### 4. Real Horizon Client

Same path: Tray discovers `vmware-view` / `horizon-client`, wraps each root PID in its own Job Object, probes VMware + Omnissa log directories under LocalAppData.

## MVP success criteria

- [x] Shared contracts + solution build  
- [x] Deploy compose/Caddy/Postgres (Docker required on the host to run)  
- [x] API enroll (hash-only tokens/secrets), heartbeat, AgentHub, admin restart + audit  
- [x] Service + Tray Job Objects, 3-strike hang, command TTL/dedupe  
- [x] FakeClient hang harness  
- [ ] Live lab verification on your machine (compose + agent run)

## Defaults (change for anything beyond lab)

| Setting | Default |
|--------|---------|
| Admin API key | `lab-admin-key-change-me` |
| Enrollment token | `lab-enroll-token-change-me` |
| Agent auth | `Authorization: Bearer <agent_uuid>:<agent_secret>` |
