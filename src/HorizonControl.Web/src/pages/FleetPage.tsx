import { useQuery } from "@tanstack/react-query";
import { api } from "../api";

export function FleetPage() {
  const { data, error, isLoading } = useQuery({
    queryKey: ["endpoints"],
    queryFn: api.listEndpoints
  });

  return (
    <section>
      <h2 className="font-display text-2xl font-semibold text-ink">Fleet</h2>
      <p className="mt-1 text-sm text-slate-500">Enrolled endpoints (auto-refresh).</p>

      {isLoading ? <p className="mt-6 text-slate-500">Loading…</p> : null}
      {error ? <p className="mt-6 text-red-600">{(error as Error).message}</p> : null}

      {data ? (
        <div className="mt-6 overflow-x-auto rounded-lg border border-slate-200 bg-white">
          <table className="min-w-full text-left text-sm">
            <thead className="border-b border-slate-200 bg-slate-50 text-slate-600">
              <tr>
                <th className="px-4 py-3 font-medium">Hostname</th>
                <th className="px-4 py-3 font-medium">Agent</th>
                <th className="px-4 py-3 font-medium">Version</th>
                <th className="px-4 py-3 font-medium">Domain</th>
                <th className="px-4 py-3 font-medium">Last seen (UTC)</th>
              </tr>
            </thead>
            <tbody>
              {data.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-4 py-6 text-slate-500">
                    No endpoints enrolled yet.
                  </td>
                </tr>
              ) : (
                data.map((e) => (
                  <tr key={e.agentUuid} className="border-b border-slate-100 last:border-0">
                    <td className="px-4 py-3 font-medium text-ink">{e.hostname}</td>
                    <td className="px-4 py-3 font-mono text-xs text-slate-600">{e.agentUuid}</td>
                    <td className="px-4 py-3">{e.agentVersion ?? "—"}</td>
                    <td className="px-4 py-3">{e.isDomainJoined ? "Yes" : "No"}</td>
                    <td className="px-4 py-3 text-slate-600">
                      {new Date(e.lastSeenAtUtc).toISOString().replace("T", " ").slice(0, 19)}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
