import { FormEvent, useState } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { api, getAdminKey, setAdminKey } from "../api";

export function LoginPage() {
  const navigate = useNavigate();
  const [key, setKey] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (getAdminKey()) {
    return <Navigate to="/fleet" replace />;
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.probeAuth(key.trim());
      setAdminKey(key.trim());
      navigate("/fleet");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Login failed");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mx-auto flex min-h-screen max-w-md flex-col justify-center px-4">
      <h1 className="font-display text-3xl font-semibold tracking-tight text-ink">Horizon Fleet</h1>
      <p className="mt-2 text-slate-600">Sign in with the admin API key.</p>
      <form onSubmit={onSubmit} className="mt-8 space-y-4">
        <label className="block text-sm font-medium text-slate-700">
          Admin API key
          <input
            type="password"
            autoComplete="off"
            className="mt-1 w-full rounded-md border border-slate-300 bg-white px-3 py-2 shadow-sm outline-none ring-accent focus:ring-2"
            value={key}
            onChange={(e) => setKey(e.target.value)}
            required
          />
        </label>
        {error ? <p className="text-sm text-red-600">{error}</p> : null}
        <button
          type="submit"
          disabled={busy}
          className="w-full rounded-md bg-accent px-4 py-2 font-medium text-white hover:bg-teal-700 disabled:opacity-60"
        >
          {busy ? "Checking…" : "Continue"}
        </button>
      </form>
    </div>
  );
}
