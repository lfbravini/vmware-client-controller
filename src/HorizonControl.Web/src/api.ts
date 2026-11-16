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
  isLive?: boolean;
};

export type TenantItem = {
  tenantId: string;
  name: string;
  slug: string;
  status: string;
  createdAtUtc: string;
};

export type OuItem = {
  ouId: string;
  tenantId: string;
  name: string;
  path: string;
};

export type TokenItem = {
  tokenId: string;
  tenantId: string;
  ouId: string;
  expiresAtUtc?: string | null;
  maxUses?: number | null;
  useCount: number;
};

export type CommandItem = {
  commandId: string;
  agentUuid: string;
  instanceId?: string | null;
  success: boolean;
  message?: string | null;
  createdAtUtc: string;
};

export type BrokerItem = {
  brokerId: string;
  tenantId: string;
  name: string;
  baseUrl: string;
  publicFqdn: string;
  pollIntervalSec: number;
  lastPollAtUtc?: string | null;
  lastPollError?: string | null;
};

export type BrokerSessionItem = {
  snapshotId: string;
  brokerId: string;
  sessionId: string;
  userName?: string | null;
  dnsName?: string | null;
  brokerFqdn?: string | null;
  observedAtUtc: string;
  correlatedInstanceId?: string | null;
  correlatedHostname?: string | null;
};

export type OuPolicy = {
  policy_version: number;
  monitoring: {
    check_interval_sec: number;
    hang_threshold_sec: number;
    consecutive_failures_required: number;
  };
  remediation: {
    auto_remediate: boolean;
    max_restarts_per_hour: number;
    flush_cache_after_failed_restarts: number;
    notify_user_on_restart: boolean;
  };
};

export const api = {
  listEndpoints: () => adminFetch<EndpointItem[]>("/api/v1/admin/endpoints"),
  listInstances: (liveOnly = false) =>
    adminFetch<InstanceItem[]>(`/api/v1/admin/instances${liveOnly ? "?liveOnly=true" : ""}`),
  listTenants: () => adminFetch<TenantItem[]>("/api/v1/admin/tenants"),
  listOus: () => adminFetch<OuItem[]>("/api/v1/admin/ous"),
  listTokens: () => adminFetch<TokenItem[]>("/api/v1/admin/enrollment-tokens"),
  createToken: (body: { tenantId: string; ouId: string; maxUses?: number }) =>
    adminFetch<{ plainToken: string; tokenId: string }>("/api/v1/admin/enrollment-tokens", {
      method: "POST",
      body: JSON.stringify(body)
    }),
  getPolicy: (ouId: string) => adminFetch<OuPolicy>(`/api/v1/admin/ous/${ouId}/policy`),
  putPolicy: (ouId: string, policy: OuPolicy) =>
    adminFetch<OuPolicy>(`/api/v1/admin/ous/${ouId}/policy`, {
      method: "PUT",
      body: JSON.stringify(policy)
    }),
  listCommands: () => adminFetch<CommandItem[]>("/api/v1/admin/commands?take=30"),
  listBrokers: () => adminFetch<BrokerItem[]>("/api/v1/admin/brokers"),
  upsertBroker: (body: {
    tenantId: string;
    name: string;
    baseUrl: string;
    publicFqdn: string;
    pollIntervalSec?: number;
  }) =>
    adminFetch<BrokerItem>("/api/v1/admin/brokers", {
      method: "POST",
      body: JSON.stringify(body)
    }),
  listBrokerSessions: () => adminFetch<BrokerSessionItem[]>("/api/v1/admin/broker-sessions"),
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

export function isOnline(lastSeenAtUtc: string, skewSec = 45): boolean {
  const t = Date.parse(lastSeenAtUtc);
  if (Number.isNaN(t)) return false;
  return Date.now() - t < skewSec * 1000;
}
