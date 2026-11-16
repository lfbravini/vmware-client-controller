import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, formatState } from "../api";

export function InstancesPage() {
  const queryClient = useQueryClient();
  const { data, error, isLoading } = useQuery({
    queryKey: ["instances"],
    queryFn: () => api.listInstances(false)
  });
  const commands = useQuery({ queryKey: ["commands"], queryFn: api.listCommands });

  const restart = useMutation({
    mutationFn: (instanceId: string) => api.restartInstance(instanceId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["instances"] });
      void queryClient.invalidateQueries({ queryKey: ["commands"] });
    }
  });

  return (
    <section className="space-y-8">
      <div>
        <h2 className="font-display text-2xl font-semibold text-ink">Instances</h2>
        <p className="mt-1 text-sm text-slate-500">
          Restart terminates via Job Object and attempts relaunch. Ghosts (TERMINATED) stay disabled.
        </p>

        {isLoading ? <p className="mt-6 text-slate-500">Loading…</p> : null}
        {error ? <p className="mt-6 text-red-600">{(error as Error).message}</p> : null}
        {restart.error ? (
          <p className="mt-4 text-red-600">{(restart.error as Error).message}</p>
        ) : null}

        {data ? (
          <div className="mt-6 overflow-x-auto rounded-lg border border-slate-200 bg-white">
            <table className="min-w-full text-left text-sm">
              <thead className="border-b border-slate-200 bg-slate-50 text-slate-600">
                <tr>
                  <th className="px-4 py-3 font-medium">Live</th>
                  <th className="px-4 py-3 font-medium">Hostname</th>
                  <th className="px-4 py-3 font-medium">Broker</th>
                  <th className="px-4 py-3 font-medium">PID</th>
                  <th className="px-4 py-3 font-medium">State</th>
                  <th className="px-4 py-3 font-medium">Action</th>
                </tr>
              </thead>
              <tbody>
                {data.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="px-4 py-6 text-slate-500">
                      No managed instances.
                    </td>
                  </tr>
                ) : (
                  data.map((i) => {
                    const live = i.isLive !== false && i.state !== 4;
                    return (
                      <tr key={i.instanceId} className="border-b border-slate-100 last:border-0">
                        <td className="px-4 py-3 text-xs">{live ? "yes" : "no"}</td>
                        <td className="px-4 py-3 font-medium text-ink">{i.hostname}</td>
                        <td className="px-4 py-3 text-xs">{i.brokerFqdn}</td>
                        <td className="px-4 py-3 font-mono">{i.rootPid}</td>
                        <td className="px-4 py-3">{formatState(i.state)}</td>
                        <td className="px-4 py-3">
                          <button
                            type="button"
                            className="rounded-md bg-ink px-3 py-1.5 text-xs font-medium text-white hover:bg-slate-800 disabled:opacity-40"
                            disabled={restart.isPending || !live}
                            onClick={() => restart.mutate(i.instanceId)}
                          >
                            Restart
                          </button>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        ) : null}
      </div>

      <div>
        <h3 className="text-lg font-semibold text-ink">Recent command acks</h3>
        <ul className="mt-3 space-y-2 text-sm">
          {(commands.data ?? []).slice(0, 10).map((c) => (
            <li key={c.commandId} className="rounded border border-slate-200 bg-white px-3 py-2">
              <span className={c.success ? "text-teal-700" : "text-red-600"}>
                {c.success ? "OK" : "FAIL"}
              </span>{" "}
              <span className="font-mono text-xs">{c.commandId}</span> — {c.message ?? ""}
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}
