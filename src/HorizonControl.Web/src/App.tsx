import { Navigate, Route, Routes } from "react-router-dom";
import { getAdminKey } from "./api";
import { LoginPage } from "./pages/LoginPage";
import { FleetPage } from "./pages/FleetPage";
import { InstancesPage } from "./pages/InstancesPage";
import { TokensPage } from "./pages/TokensPage";
import { PolicyPage } from "./pages/PolicyPage";
import { BrokersPage } from "./pages/BrokersPage";
import { Shell } from "./components/Shell";

function RequireAuth({ children }: { children: React.ReactNode }) {
  if (!getAdminKey()) {
    return <Navigate to="/login" replace />;
  }
  return <>{children}</>;
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/"
        element={
          <RequireAuth>
            <Shell />
          </RequireAuth>
        }
      >
        <Route index element={<Navigate to="/fleet" replace />} />
        <Route path="fleet" element={<FleetPage />} />
        <Route path="instances" element={<InstancesPage />} />
        <Route path="tokens" element={<TokensPage />} />
        <Route path="policy" element={<PolicyPage />} />
        <Route path="brokers" element={<BrokersPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
