# MVP Non-Goals

This document prevents scope creep during the MVP remediation build. Do **not** implement these until the core heal loop works (enroll → hang detect → Job Object → `RESTART_CLIENT_INSTANCE`).

## Deferred modules

- Redis (SignalR backplane / presence cache)
- Grafana Loki / VictoriaMetrics / alert webhooks
- MinIO, OTA binary updates, `HorizonWatchdog.Updater`
- RustDesk on-demand remote assist (`hbbs` / `hbbr`)
- Wake-on-LAN relay and maintenance-window power orchestration
- Horizon Connection Server REST poller and local-vs-RDSH correlation
- Full Virtual OU policy inheritance UI and dynamic auto-tagging engine
- React multi-tenant admin console (`HorizonControl.Web`) and real RBAC
- Northbound ITSM OpenAPI (`/api/v1/external/*`)
- WiX MSI packaging
- Cloudflare Tunnel / Access (post-MVP edge option; MVP uses Caddy only)
- Kubernetes, multi-region, plugin SDK, mobile clients

## Still in scope for MVP

- Caddy + API + Postgres
- Shared contracts, agent enroll/auth, SignalR commands
- Watchdog Service + Tray + FakeClient hang harness
- Swagger + admin API key for list/restart
