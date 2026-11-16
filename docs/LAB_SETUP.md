# Lab Setup (Mode A preferred, Mode B fallback)

Prepare a Windows PC to deploy and test the Horizon Fleet MVP heal loop.

## Prerequisites

| Item | Notes |
|------|--------|
| .NET 8 SDK | `dotnet --list-sdks` must list `8.0.x` |
| Git | For repo status/commits |
| Docker Desktop + WSL2 | **Mode A** (Caddy + API + Postgres). After first install, **reboot** if `docker info` says the engine cannot start |
| PostgreSQL 16 | **Mode B only** when Docker engine is unavailable |
| Admin / elevated PowerShell | Service writes `HKLM\SOFTWARE\HorizonWatchdog` |
| Free ports | `8080` (edge/API), `5432` (Postgres), `8443` (reserved) |

Do **not** install Redis, Loki, MinIO, or RustDesk for this lab ([MVP_NON_GOALS.md](MVP_NON_GOALS.md)).

## Lab secrets (local only)

| Key | Value |
|-----|--------|
| Admin header | `X-Admin-Api-Key: lab-admin-key-change-me` |
| Enrollment token | `lab-enroll-token-change-me` |
| Agent server | `HORIZON_SERVER=http://127.0.0.1:8080` |

## Mode A — Docker Compose (preferred)

```powershell
cd "C:\projects\vmware client controller"
dotnet build HorizonFleetControl.sln -c Release
docker compose -f deploy/docker-compose.yml config
docker compose -f deploy/docker-compose.yml up --build
```

Open `http://localhost:8080/swagger`.

If Docker Desktop was just installed and the engine fails (`unable to start` / pipe error / WSL has no distros):

1. Enable **Virtual Machine Platform** (and ensure CPU virtualization is on in BIOS/firmware).  
   Elevated: `dism /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart`
2. **Reboot** Windows.
3. `wsl --install -d Ubuntu`, finish first-run user setup.
4. Start Docker Desktop and wait until healthy (`docker info`).
5. Retry `docker compose ... up --build`.

On this lab PC, Hyper-V/virtualization detection was incomplete until features are enabled + reboot — Mode B remains the reliable path until then.

## Mode B — Local Postgres + API (fallback)

Use when Docker cannot run yet.

1. Install PostgreSQL 16 (password for `postgres` used in lab: `horizon_lab`).
2. Create role/db/extensions:

```powershell
$env:PGPASSWORD = "horizon_lab"
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -h 127.0.0.1 -c "CREATE ROLE horizon LOGIN PASSWORD 'horizon_lab';"
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -h 127.0.0.1 -c "CREATE DATABASE horizon_fleet OWNER horizon;"
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -U postgres -h 127.0.0.1 -d horizon_fleet -f deploy/lab-init-local.sql
```

3. Start API on port 8080 (skip launchSettings):

```powershell
$env:ConnectionStrings__Default = "Host=127.0.0.1;Port=5432;Database=horizon_fleet;Username=horizon;Password=horizon_lab"
$env:FleetControl__AdminApiKey = "lab-admin-key-change-me"
$env:FleetControl__LabEnrollmentToken = "lab-enroll-token-change-me"
dotnet run --project src/HorizonControl.Api -c Release --no-launch-profile --urls http://127.0.0.1:8080
```

4. Swagger: `http://127.0.0.1:8080/swagger`

## Agent triad

Three processes (Service **elevated**). Start Service before Tray.

```powershell
$env:HORIZON_SERVER = "http://127.0.0.1:8080"
$env:HORIZON_ENROLLMENT_TOKEN = "lab-enroll-token-change-me"
dotnet run --project src/HorizonWatchdog.Service -c Release
dotnet run --project src/HorizonWatchdog.Tray -c Release
dotnet run --project src/HorizonWatchdog.FakeClient -c Release
```

### Checks

1. `GET /api/v1/admin/endpoints` with admin key → this hostname appears.
2. `GET /api/v1/admin/instances` → FakeClient `instanceId` + `jobObjectName`.
3. Heal loop:
   - FakeClient → **Simulate UI hang**, wait for 3 hang checks, **or**
   - `POST /api/v1/admin/instances/restart` with `{ "instanceId": "<guid>", "ttlSec": 120 }`
4. Confirm `HorizonWatchdog.FakeClient` process exits (Job Object terminate).

## Common failures

| Symptom | Fix |
|---------|-----|
| Docker unable to start | Reboot; complete WSL; start Docker Desktop |
| Enroll 401 | Token must match `FleetControl__LabEnrollmentToken` |
| Service registry errors | Run Service elevated |
| Tray no inventory | Start Service first (Named Pipe) |
| API on 5047 not 8080 | Use `--no-launch-profile --urls http://127.0.0.1:8080` |
| Port in use | `netstat -ano \| findstr ":8080 :5432"` |
| Path with spaces | `cd` into repo; quote paths |

## Verified on this machine (initial lab)

- Release build OK; MVP committed.
- Docker Desktop installed; **engine not yet healthy** without reboot → Mode B used.
- Local Postgres 16 + API on `:8080`.
- Service + Tray + FakeClient enrolled; admin restart terminated FakeClient via Job Object.
