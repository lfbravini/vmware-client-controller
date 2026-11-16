# Deploy control plane via Portainer

## Git vs Cursor (what Portainer needs)

Cursor’s git is **local only** on the Windows VM (`git init` / commits). That is **not** a remote Portainer can clone.

For **Stacks → Add stack → Repository**, Portainer needs a **cloneable URL**, for example:

- `https://github.com/<org>/<repo>.git`
- `https://gitlab.com/<group>/<repo>.git`
- any Gitea / Azure DevOps HTTPS (or SSH) URL Portainer’s host can reach

Optional: a deploy key or username + PAT if the repo is private.

This lab repo currently has **no `git remote`**. Until you add one and push:

1. Build `horizon-control-api:lab` through the Portainer Docker API (tar upload), and
2. Run containers from [`portainer-stack.yml`](portainer-stack.yml) (or create them via the Docker proxy as in the lab).

To enable Git deploys later:

```powershell
git remote add origin https://github.com/<you>/<repo>.git
git push -u origin master
```

Then in Portainer: **Stacks → Add stack → Repository**, point at that URL and `deploy/portainer-stack.yml` (you still need a place that builds/pushes the `horizon-control-api:lab` image, or change the compose to a registry image).

## Lab URL (live)

| What | URL / value |
|------|-------------|
| Edge (Caddy) | `http://172.16.100.81:18080` (9443 left for Portainer UI) |
| Health | `http://172.16.100.81:18080/health` |
| Swagger | `http://172.16.100.81:18080/swagger` |
| Admin header | `X-Admin-Api-Key: lab-admin-key-change-me` |
| Agent | `HORIZON_SERVER=http://172.16.100.81:18080` |
| Enrollment | `FleetControl__LabEnrollmentToken=lab-enroll-token-change-me` |

Containers on endpoint `local` (id=3): `horizon-fleet-caddy`, `horizon-fleet-control-api`, `horizon-fleet-postgres`.

## Host notes

- Portainer CE **2.39.0** at `https://172.16.100.81:9443`.
- Disk was full during first Postgres start (`No space left on device`). Unused images (dotnet SDK, powershell, dangling layers) were pruned so Postgres could initialize. Keep an eye on disk before rebuilding images.
- Do **not** commit Portainer API tokens. Rotate any token pasted into chat.
