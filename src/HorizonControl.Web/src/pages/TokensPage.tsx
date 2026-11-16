import { FormEvent, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../api";

export function TokensPage() {
  const qc = useQueryClient();
  const tokens = useQuery({ queryKey: ["tokens"], queryFn: api.listTokens });
  const tenants = useQuery({ queryKey: ["tenants"], queryFn: api.listTenants });
  const ous = useQuery({ queryKey: ["ous"], queryFn: api.listOus });
  const [plain, setPlain] = useState<string | null>(null);
  const create = useMutation({
    mutationFn: api.createToken,
    onSuccess: (r) => {
      setPlain(r.plainToken);
      void qc.invalidateQueries({ queryKey: ["tokens"] });
    }
  });

  function onCreate(e: FormEvent) {
    e.preventDefault();
    const tenant = tenants.data?.[0];
    const ou = ous.data?.[0];
    if (!tenant || !ou) return;
    create.mutate({ tenantId: tenant.tenantId, ouId: ou.ouId, maxUses: 100 });
  }

  return (
    <section>
      <h2 className="font-display text-2xl font-semibold text-ink">Enrollment tokens</h2>
      <p className="mt-1 text-sm text-slate-500">Mint once; plaintext shown only at creation.</p>
      <form onSubmit={onCreate} className="mt-4">
        <button
          type="submit"
          className="rounded-md bg-accent px-3 py-2 text-sm font-medium text-white"
          disabled={create.isPending || !tenants.data?.length}
        >
          Create token
        </button>
      </form>
      {plain ? (
        <p className="mt-3 rounded-md border border-teal-200 bg-teal-50 px-3 py-2 font-mono text-sm">
          {plain}
        </p>
      ) : null}
      {tokens.data ? (
        <div className="mt-6 overflow-x-auto rounded-lg border border-slate-200 bg-white">
          <table className="min-w-full text-left text-sm">
            <thead className="border-b bg-slate-50 text-slate-600">
              <tr>
                <th className="px-4 py-3">Token id</th>
                <th className="px-4 py-3">Uses</th>
                <th className="px-4 py-3">Max</th>
              </tr>
            </thead>
            <tbody>
              {tokens.data.map((t) => (
                <tr key={t.tokenId} className="border-b border-slate-100">
                  <td className="px-4 py-3 font-mono text-xs">{t.tokenId}</td>
                  <td className="px-4 py-3">{t.useCount}</td>
                  <td className="px-4 py-3">{t.maxUses ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
