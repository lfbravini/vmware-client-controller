import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../api";

export function BrokersPage() {
  const qc = useQueryClient();
  const tenants = useQuery({ queryKey: ["tenants"], queryFn: api.listTenants });
  const brokers = useQuery({ queryKey: ["brokers"], queryFn: api.listBrokers });
  const sessions = useQuery({ queryKey: ["broker-sessions"], queryFn: api.listBrokerSessions });
  const [name, setName] = useState("Lab mock broker");
  const [baseUrl, setBaseUrl] = useState("http://control-api:8080/lab/mock-horizon");
  const [fqdn, setFqdn] = useState("lab.fakeclient");

  const upsert = useMutation({
    mutationFn: api.upsertBroker,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["brokers"] });
      void qc.invalidateQueries({ queryKey: ["broker-sessions"] });
    }
  });

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    const tenant = tenants.data?.[0];
    if (!tenant) return;
    upsert.mutate({
      tenantId: tenant.tenantId,
      name,
      baseUrl,
      publicFqdn: fqdn,
      pollIntervalSec: 30
    });
  }

  return (
    <section className="space-y-8">
      <div>
        <h2 className="font-display text-2xl font-semibold text-ink">Horizon brokers</h2>
        <p className="mt-1 text-sm text-slate-500">
          Control plane polls Connection Server (or lab mock). Agents never talk to brokers.
        </p>
        <form onSubmit={onSubmit} className="mt-4 max-w-xl space-y-3">
          <input
            className="w-full rounded border border-slate-300 px-3 py-2 text-sm"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Name"
          />
          <input
            className="w-full rounded border border-slate-300 px-3 py-2 text-sm"
            value={baseUrl}
            onChange={(e) => setBaseUrl(e.target.value)}
            placeholder="Base URL"
          />
          <input
            className="w-full rounded border border-slate-300 px-3 py-2 text-sm"
            value={fqdn}
            onChange={(e) => setFqdn(e.target.value)}
            placeholder="Public FQDN (for correlation)"
          />
          <button type="submit" className="rounded-md bg-accent px-3 py-2 text-sm text-white">
            Save broker
          </button>
        </form>
        {brokers.data ? (
          <ul className="mt-4 space-y-2 text-sm">
            {brokers.data.map((b) => (
              <li key={b.brokerId} className="rounded border border-slate-200 bg-white px-3 py-2">
                <span className="font-medium">{b.name}</span> — {b.baseUrl}
                <div className="text-xs text-slate-500">
                  last poll: {b.lastPollAtUtc ?? "—"} {b.lastPollError ? `err=${b.lastPollError}` : "ok"}
                </div>
              </li>
            ))}
          </ul>
        ) : null}
      </div>
      <div>
        <h3 className="text-lg font-semibold text-ink">Broker sessions (correlated)</h3>
        <div className="mt-3 overflow-x-auto rounded-lg border border-slate-200 bg-white">
          <table className="min-w-full text-left text-sm">
            <thead className="border-b bg-slate-50 text-slate-600">
              <tr>
                <th className="px-4 py-3">Session</th>
                <th className="px-4 py-3">User</th>
                <th className="px-4 py-3">Broker</th>
                <th className="px-4 py-3">Local instance</th>
              </tr>
            </thead>
            <tbody>
              {(sessions.data ?? []).map((s) => (
                <tr key={s.snapshotId} className="border-b border-slate-100">
                  <td className="px-4 py-3 font-mono text-xs">{s.sessionId}</td>
                  <td className="px-4 py-3">{s.userName ?? "—"}</td>
                  <td className="px-4 py-3">{s.brokerFqdn ?? "—"}</td>
                  <td className="px-4 py-3">
                    {s.correlatedHostname
                      ? `${s.correlatedHostname} (${s.correlatedInstanceId})`
                      : "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </section>
  );
}
