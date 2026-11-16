import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { clearAdminKey } from "../api";

export function Shell() {
  const navigate = useNavigate();

  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-200/80 bg-white/80 backdrop-blur">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-4">
          <div>
            <p className="font-display text-xl font-semibold tracking-tight text-ink">Horizon Fleet</p>
            <p className="text-sm text-slate-500">Admin console</p>
          </div>
          <nav className="flex items-center gap-4 text-sm font-medium">
            <NavLink
              to="/fleet"
              className={({ isActive }) =>
                isActive ? "text-accent" : "text-slate-600 hover:text-ink"
              }
            >
              Fleet
            </NavLink>
            <NavLink
              to="/instances"
              className={({ isActive }) =>
                isActive ? "text-accent" : "text-slate-600 hover:text-ink"
              }
            >
              Instances
            </NavLink>
            <button
              type="button"
              className="text-slate-500 hover:text-ink"
              onClick={() => {
                clearAdminKey();
                navigate("/login");
              }}
            >
              Sign out
            </button>
          </nav>
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  );
}
