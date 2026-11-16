const KEY_STORAGE = "horizon.adminApiKey";

export function getAdminKey(): string | null {
  return sessionStorage.getItem(KEY_STORAGE);
}

export function setAdminKey(key: string): void {
  sessionStorage.setItem(KEY_STORAGE, key);
}

export function clearAdminKey(): void {
  sessionStorage.removeItem(KEY_STORAGE);
}

async function adminFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const key = getAdminKey();
  if (!key) {
    throw new Error("Not authenticated");
  }

  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-Admin-Api-Key": key,
      ...(init?.headers ?? {})
    }
  });

  if (response.status === 401) {
    clearAdminKey();
    throw new Error("Unauthorized");
  }

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export type EndpointItem = {
  agentUuid: string;
  tenantId: string;
  ouId: string;
  hostname: string;
  isDomainJoined: boolean;
  agentVersion?: string | null;
  lastSeenAtUtc: string;
};

export type InstanceItem = {
  instanceId: string;
  agentUuid: string;
  hostname: string;
  brokerFqdn: string;
  horizonUser?: string | null;
  rootPid: number;
  jobObjectName: string;
  state: number;
  updatedAtUtc: string;
};

export type TenantItem = {
  tenantId: string;
  name: string;
  slug: string;
  status: string;
  createdAtUtc: string;
};

export const api = {
  listEndpoints: () => adminFetch<EndpointItem[]>("/api/v1/admin/endpoints"),
  listInstances: () => adminFetch<InstanceItem[]>("/api/v1/admin/instances"),
  listTenants: () => adminFetch<TenantItem[]>("/api/v1/admin/tenants"),
  restartInstance: (instanceId: string) =>
    adminFetch<{ commandId: string; instanceId: string; agentUuid: string }>(
      "/api/v1/admin/instances/restart",
      {
        method: "POST",
        body: JSON.stringify({ instanceId, ttlSec: 120 })
      }
    ),
  probeAuth: async (key: string) => {
    const response = await fetch("/api/v1/admin/tenants", {
      headers: { "X-Admin-Api-Key": key, Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error("Invalid admin API key");
    }
  }
};

export function formatState(state: number): string {
  switch (state) {
    case 1:
      return "AppHung";
    case 2:
      return "BlastStalled";
    case 3:
      return "Recovering";
    case 4:
      return "Terminated";
    default:
      return "Healthy";
  }
}
