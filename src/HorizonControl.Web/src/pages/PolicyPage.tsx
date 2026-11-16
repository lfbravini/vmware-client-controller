import { FormEvent, useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, OuPolicy } from "../api";

export function PolicyPage() {
  const qc = useQueryClient();
  const ous = useQuery({ queryKey: ["ous"], queryFn: api.listOus });
  const ouId = ous.data?.[0]?.ouId;
  const policyQ = useQuery({
    queryKey: ["policy", ouId],
    queryFn: () => api.getPolicy(ouId!),
    enabled: !!ouId
  });
  const [form, setForm] = useState<OuPolicy | null>(null);
  useEffect(() => {
    if (policyQ.data) setForm(policyQ.data);
  }, [policyQ.data]);

  const save = useMutation({
    mutationFn: (p: OuPolicy) => api.putPolicy(ouId!, p),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["policy", ouId] })
  });

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (form) save.mutate(form);
  }

  if (!form) return <p className="text-slate-500">Loading policy…</p>;

  return (
    <section>
      <h2 className="font-display text-2xl font-semibold text-ink">OU policy</h2>
      <p className="mt-1 text-sm text-slate-500">
        Monitoring + remediation for {ous.data?.[0]?.name ?? "root"}. Pushed live to agents.
      </p>
      <form onSubmit={onSubmit} className="mt-6 max-w-lg space-y-4">
        <label className="block text-sm">
          Check interval (sec)
          <input
            type="number"
            className="mt-1 w-full rounded border border-slate-300 px-3 py-2"
            value={form.monitoring.check_interval_sec}
            onChange={(e) =>
              setForm({
                ...form,
                monitoring: { ...form.monitoring, check_interval_sec: Number(e.target.value) }
              })
            }
          />
        </label>
        <label className="block text-sm">
          Consecutive hang failures
          <input
            type="number"
            className="mt-1 w-full rounded border border-slate-300 px-3 py-2"
            value={form.monitoring.consecutive_failures_required}
            onChange={(e) =>
              setForm({
                ...form,
                monitoring: {
                  ...form.monitoring,
                  consecutive_failures_required: Number(e.target.value)
                }
              })
            }
          />
        </label>
        <label className="block text-sm">
          Max restarts / hour
          <input
            type="number"
            className="mt-1 w-full rounded border border-slate-300 px-3 py-2"
            value={form.remediation.max_restarts_per_hour}
            onChange={(e) =>
              setForm({
                ...form,
                remediation: { ...form.remediation, max_restarts_per_hour: Number(e.target.value) }
              })
            }
          />
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={form.remediation.auto_remediate}
            onChange={(e) =>
              setForm({
                ...form,
                remediation: { ...form.remediation, auto_remediate: e.target.checked }
              })
            }
          />
          Auto-remediate
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={form.remediation.notify_user_on_restart}
            onChange={(e) =>
              setForm({
                ...form,
                remediation: { ...form.remediation, notify_user_on_restart: e.target.checked }
              })
            }
          />
          Notify user on restart
        </label>
        <button type="submit" className="rounded-md bg-ink px-4 py-2 text-sm text-white">
          Save & push
        </button>
        {save.isSuccess ? <p className="text-sm text-teal-700">Saved.</p> : null}
        {save.error ? <p className="text-sm text-red-600">{(save.error as Error).message}</p> : null}
      </form>
    </section>
  );
}
