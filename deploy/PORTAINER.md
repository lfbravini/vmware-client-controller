# Deploy control plane via Portainer (V1)

## Git remote (live)

- Repo: `https://github.com/lfbravini/vmware-client-controller.git`
- Stack name: `horizon-fleet`
- Compose path: `deploy/portainer-stack.yml`
- Reference: `refs/heads/master`

Portainer CE **2.39.0** UI: `https://172.16.100.81:9443` (do not use 9443 for the fleet edge).

## Lab URLs

| What | URL / value |
|------|-------------|
| Edge (Caddy) | `http://172.16.100.81:18080` |
| Admin console | `http://172.16.100.81:18080/` |
| Health | `http://172.16.100.81:18080/health` |
| Swagger | `http://172.16.100.81:18080/swagger` |
| Admin header | `X-Admin-Api-Key: lab-admin-key-change-me` |
| Agent | `HORIZON_SERVER=http://172.16.100.81:18080` |
| Enrollment | `lab-enroll-token-change-me` |

## Images

Git only supplies compose. Build images on the Portainer Docker host before (re)deploy:

1. `horizon-control-api:lab` — [`Dockerfile.api`](Dockerfile.api) (tar upload via Portainer Docker build API, or `docker build` on the host).
2. `horizon-control-web:lab` — [`Dockerfile.web`](Dockerfile.web) after `npm run build` in `src/HorizonControl.Web` (or multi-stage Dockerfile build).

Stack env sets `FleetControl__AllowLabDefaults=true` so Production can keep documented lab keys. For non-lab, remove that flag and set strong `FleetControl__AdminApiKey` / `FleetControl__LabEnrollmentToken`.

## Secrets

- **Never** commit Portainer API tokens. Store locally in ignored [`docs/api.txt`](../docs/api.txt).
- Rotate any token that was pasted into chat or committed by mistake.

## Admin path hardening

Lab leaves `/`, `/swagger*`, and `/api/v1/admin*` open on `:18080`. For LAN-only production, see [`Caddyfile.production-snippet`](Caddyfile.production-snippet) (CIDR allowlist). Keep `/api/v1/agent/*` and `/hubs/agent*` reachable for endpoints.

## TLS

V1 lab uses HTTP on `:18080`. Enable Caddy TLS/`8443` later; trust the CA on agents when you do.
