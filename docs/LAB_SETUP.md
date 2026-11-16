# Lab Setup (V1+)

## Daily loop (preferred)

From an elevated-capable session on the Windows lab PC:

```powershell
cd "C:\projects\vmware client controller"
.\scripts\lab-up.ps1
```

This builds, pushes images to Portainer, recreates the Git stack, starts agents (UAC once), and runs UC1–UC5. Token: ignored `docs/api.txt`. Edge default: `http://172.16.100.81:18080`.

Pieces: `lab-redeploy.ps1`, `lab-agents.ps1`, `lab-test.ps1`. Agent zip: `.\scripts\Publish-AgentZip.ps1`.

Prepare a Windows PC to deploy and test the Horizon Fleet heal loop against the Portainer control plane (preferred) or local Mode A/B fallbacks.

## Preferred — Portainer edge

| Item | Value |
|------|--------|
| Edge | `http://172.16.100.81:18080` |
| Admin console | `http://172.16.100.81:18080/` (React UI after V1 deploy) |
| Swagger | `http://172.16.100.81:18080/swagger` |
| Admin header | `X-Admin-Api-Key: lab-admin-key-change-me` |
| Enrollment | `lab-enroll-token-change-me` |
| Agent | `HORIZON_SERVER=http://172.16.100.81:18080` |

See [deploy/PORTAINER.md](../deploy/PORTAINER.md) for Git stack details. Portainer API tokens belong only in local ignored [`docs/api.txt`](api.txt) — never commit them.

### Install agents (supported path)

Elevated PowerShell from the repo:

```powershell
cd "C:\projects\vmware client controller"
dotnet build HorizonFleetControl.sln -c Release
.\installer\Install-HorizonWatchdog.ps1 `
  -ServerUrl "http://172.16.100.81:18080" `
  -EnrollmentToken "lab-enroll-token-change-me"
```

Then start FakeClient for hang tests:

```powershell
dotnet run --project src/HorizonWatchdog.FakeClient -c Release --no-build
```

Uninstall: `.\installer\Uninstall-HorizonWatchdog.ps1`

### Checks

1. Admin UI or `GET /api/v1/admin/endpoints` → this hostname appears.
2. `GET /api/v1/admin/instances` → FakeClient `instanceId` + `jobObjectName`.
3. Heal loop:
   - FakeClient → **Simulate UI hang**, wait for 3 hang checks, **or**
   - Admin UI **Restart** / `POST /api/v1/admin/instances/restart`
4. Confirm `HorizonWatchdog.FakeClient` exits (Job Object terminate).

## Prerequisites

| Item | Notes |
|------|--------|
| .NET 8 SDK | `dotnet --list-sdks` must list `8.0.x` |
| Git | For repo status/commits |
| Admin / elevated PowerShell | Service + installer write `HKLM\SOFTWARE\HorizonWatchdog` |
| Portainer host | Control plane at `172.16.100.81` (or local Mode A/B below) |

Do **not** install Redis, Loki, MinIO, or RustDesk for this lab ([MVP_NON_GOALS.md](MVP_NON_GOALS.md)).

## Mode A — Docker Compose on the Windows PC

```powershell
cd "C:\projects\vmware client controller"
dotnet build HorizonFleetControl.sln -c Release
docker compose -f deploy/docker-compose.yml config
docker compose -f deploy/docker-compose.yml up --build
```

Open `http://localhost:8080/swagger`. Agents: `HORIZON_SERVER=http://127.0.0.1:8080`.

If Docker Desktop cannot start (Hyper-V/WSL), use Portainer (preferred) or Mode B.

## Mode B — Local Postgres + API (fallback)

1. Install PostgreSQL 16 (lab password: `horizon_lab`).
2. Create role/db/extensions via `deploy/lab-init-local.sql`.
3. Start API:

```powershell
$env:ConnectionStrings__Default = "Host=127.0.0.1;Port=5432;Database=horizon_fleet;Username=horizon;Password=horizon_lab"
$env:FleetControl__AdminApiKey = "lab-admin-key-change-me"
$env:FleetControl__LabEnrollmentToken = "lab-enroll-token-change-me"
$env:FleetControl__AllowLabDefaults = "true"
dotnet run --project src/HorizonControl.Api -c Release --no-launch-profile --urls http://127.0.0.1:8080
```

## Dev agent triad (without installer)

```powershell
$env:HORIZON_SERVER = "http://172.16.100.81:18080"
$env:HORIZON_ENROLLMENT_TOKEN = "lab-enroll-token-change-me"
# Service must be elevated
dotnet run --project src/HorizonWatchdog.Service -c Release
dotnet run --project src/HorizonWatchdog.Tray -c Release
dotnet run --project src/HorizonWatchdog.FakeClient -c Release
```

## Verified

- Portainer Git stack `horizon-fleet` on `:18080`; enroll + admin Job Object restart PASS.
- Prefer installer + FakeClient for V1 acceptance (see Phase 5 gate).
