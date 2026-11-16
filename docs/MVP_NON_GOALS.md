# Scope notes (post heal-loop gate)

The original MVP gate — enroll → hang detect → Job Object → `RESTART_CLIENT_INSTANCE` — **passed** on the Portainer lab. Items below that were blocked only by that gate are now on the **active path** (see project plan Phases 1–3).

## Active path (build next)

- React admin console depth (`HorizonControl.Web`): tokens, policy editor, presence, command ack
- Agent packaging (release zip + AtLogon Tray); WiX MSI after zip is proven
- Horizon Connection Server REST poller + local↔broker session correlation
- Heal completeness: stable instance identity, relaunch after kill, OuPolicy enforcement
- Versioned control-plane images + scripted Portainer lab loop (`scripts/lab-*.ps1`)

## Still deferred (after Connection Server correlation)

- Redis (SignalR backplane / presence cache)
- Grafana Loki / VictoriaMetrics / alert webhooks
- MinIO, OTA binary updates, `HorizonWatchdog.Updater`
- RustDesk on-demand remote assist (`hbbs` / `hbbr`)
- Wake-on-LAN relay and maintenance-window power orchestration
- Full OIDC / real multi-tenant RBAC (lab still uses admin API key)
- Northbound ITSM OpenAPI (`/api/v1/external/*`) beyond thin stubs
- Cloudflare Tunnel / Access
- Kubernetes, multi-region, plugin SDK, mobile clients

## Lab always in scope

- Caddy + API + Postgres + Web on Portainer
- Shared contracts, agent enroll/auth, SignalR commands
- Watchdog Service + Tray + FakeClient hang harness
- `scripts/lab-up.ps1` acceptance (UC1–UC5+)
