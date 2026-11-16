# MVP Deploy (Caddy + API + Postgres)

## Stack

- **Caddy** on host ports `8080` (HTTP) / `8443` reserved
- **control-api** (.NET 8)
- **Postgres 16** with `ltree` + `uuid-ossp`

Redis, Loki, VictoriaMetrics, MinIO, and RustDesk are **out of MVP** (see `docs/MVP_NON_GOALS.md`).

## Agent reachability

Agents talk to the **controller over the Internet and/or VPN**. They never need routes to Horizon brokers or Postgres. Set MSI/`SERVER=` to the public or VPN hostname that fronts Caddy (e.g. `http://ctrl.example.com:8080` in lab).

## Lab TLS

- Default compose uses **HTTP** on port 8080 to avoid untrusted-cert enroll failures on Windows agents.
- For HTTPS: enable Caddy `auto_https`, install/trust the Caddy local CA on agent PCs, or terminate TLS elsewhere (Cloudflare Tunnel is a post-MVP option).

## Rate limits

The API rate-limits enroll (and related agent routes). Stock `caddy:2` images do not include the `rate_limit` module; production can add Cloudflare WAF or a custom Caddy build.

## Admin

Swagger: `http://localhost:8080/swagger`  
Header: `X-Admin-Api-Key: lab-admin-key-change-me` (override via `FleetControl__AdminApiKey`).

## Validate

```bash
docker compose -f deploy/docker-compose.yml config
docker compose -f deploy/docker-compose.yml up --build
```
