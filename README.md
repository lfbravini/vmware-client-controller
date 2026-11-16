# Horizon Multi-Tenant Fleet Control Plane & Self-Healing Watchdog Agent

> **A complete, self-hosted, multi-tenant End-User Computing (EUC) orchestration, monitoring, and automated remediation platform for VMware / Omnissa Horizon Published Application (RDSH) environments across domain-joined and non-domain (workgroup) Windows endpoints.**

## 1. Executive Summary & Problem Statement
In enterprise **VMware / Omnissa Horizon** environments serving **Published Applications (RDSH)** rather than full VDI desktops, users frequently experience localized application freezes, Blast/PCoIP transport stalls, or endpoint OS hangs caused by network jitter, local resource contention, or remote application deadlocks.

### Gaps Solved by This Platform
1. **Local Client Blindness:** Detects when local `vmware-view.exe` or `horizon-client.exe` UI threads deadlock on the workstation.
2. **Non-Domain / Workgroup Endpoints:** Deploys, groups, and manages workgroup/BYOD Windows PCs via Virtual OUs without requiring Active Directory GPOs.
3. **Per-Instance Process Isolation (Win32 Job Objects):** Isolates multiple concurrent Horizon Client instances on the same PC so restarting a frozen session never kills healthy sibling sessions.
4. **RDSH Seamless Window Detection:** Identifies which specific published app window (`EnumWindows` + `SendMessageTimeout`) froze while other published apps in the same session stay responsive.
5. **Full Lifecycle & Remote Assist:** Includes workstation power/WoL relay, VictoriaMetrics telemetry, Grafana Loki centralized logs, OTA binary updates, and on-demand ephemeral RustDesk remote support.

## 2. Repository Structure
```text
horizon-fleet-control/
 .cursor/rules/                      # Cursor AI rules (*.mdc) — invariants, contracts, stack boundaries
 README.md                           # Full project overview & quickstart (this file)
 docs/
    horizon_multi_tenant_fleet_control_plane_master_specification.md
    ARCHITECTURE_HANDOFF.md         # Condensed schemas and flows (subordinate to master spec)
 src/
    HorizonControl.Shared/          # Shared .NET 8 DTOs, Policy models, SignalR & IPC contracts
    HorizonControl.Api/             # Linux containerized .NET 8 Web API, SignalR Hub, Horizon Poller
    HorizonWatchdog.Service/        # Windows Session-0 Service (Enrollment, IPC, Power, WoL, OTA)
    HorizonWatchdog.Tray/           # Windows User-Session App (Job Objects, HWND Hang Check, Logs)
    HorizonWatchdog.Updater/        # Atomic binary swapper for self-updates
    HorizonControl.Web/             # React + TypeScript Multi-Tenant Admin Web GUI
 installer/
    HorizonWatchdogSetup.wxs        # WiX v4 MSI packaging definition (bundles Service, Tray, RustDesk)
 deploy/
     docker-compose.yml              # Production Linux stack (Caddy, API, Web, PG, Redis, VM, Loki, MinIO, RustDesk)
```

## 3. Quickstart in Cursor AI
1. Open `C:\projects\vmware client controller` in **Cursor**.
2. Open Composer (`Ctrl+I`) and enter:
   > *"Read `@docs/horizon_multi_tenant_fleet_control_plane_master_specification.md` and follow `.cursor/rules`. Initialize the .NET 8 solution and implement `src/HorizonControl.Shared` first with all DTOs, Virtual OU Policy models, SignalR hub interfaces, and Named Pipe IPC contracts, then verify with `dotnet build`."*

## 4. Lab notes (MVP)

- **Edge:** Caddy only for MVP. Agents reach the controller over the Internet and/or VPN (`SERVER=https://...`); they never need routes to Horizon brokers or Postgres. Cloudflare Tunnel is a post-MVP option.
- **TLS:** Prefer lab HTTP on a reachable host, or trust Caddy’s local CA, so Windows agents do not fail enroll on untrusted HTTPS.
- **Rename:** A future folder/repo rename to `horizon-fleet-control` is recommended (spaces in paths hurt CLI/docker). Do not rename during MVP unless requested.
- **Non-goals:** See [`docs/MVP_NON_GOALS.md`](docs/MVP_NON_GOALS.md).
