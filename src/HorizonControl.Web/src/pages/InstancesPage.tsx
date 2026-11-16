import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, formatState } from "../api";

export function InstancesPage() {
  const queryClient = useQueryClient();
  const { data, error, isLoading } = useQuery({
    queryKey: ["instances"],
    queryFn: api.listInstances
  });

  const restart = useMutation({
    mutationFn: (instanceId: string) => api.restartInstance(instanceId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["instances"] });
    }
  });

  return (
    <section>
      <h2 className="font-display text-2xl font-semibold text-ink">Instances</h2>
      <p className="mt-1 text-sm text-slate-500">
        Managed Horizon / FakeClient instances. Restart terminates via Job Object.
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
                <th className="px-4 py-3 font-medium">Hostname</th>
                <th className="px-4 py-3 font-medium">PID</th>
                <th className="px-4 py-3 font-medium">State</th>
                <th className="px-4 py-3 font-medium">Job object</th>
                <th className="px-4 py-3 font-medium">Action</th>
              </tr>
            </thead>
            <tbody>
              {data.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-4 py-6 text-slate-500">
                    No managed instances.
                  </td>
                </tr>
              ) : (
                data.map((i) => (
                  <tr key={i.instanceId} className="border-b border-slate-100 last:border-0">
                    <td className="px-4 py-3 font-medium text-ink">{i.hostname}</td>
                    <td className="px-4 py-3 font-mono">{i.rootPid}</td>
                    <td className="px-4 py-3">{formatState(i.state)}</td>
                    <td className="px-4 py-3 font-mono text-xs text-slate-600">{i.jobObjectName}</td>
                    <td className="px-4 py-3">
                      <button
                        type="button"
                        className="rounded-md bg-ink px-3 py-1.5 text-xs font-medium text-white hover:bg-slate-800 disabled:opacity-50"
                        disabled={restart.isPending}
                        onClick={() => restart.mutate(i.instanceId)}
                      >
                        Restart
                      </button>
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
