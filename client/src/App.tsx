import { Navigate, Route, Routes } from "react-router-dom";
import { AppLayout } from "@/components/layout/AppLayout";
import { ProtectedRoute } from "@/routes/ProtectedRoute";
import LandingPage from "@/pages/LandingPage";
import LoginPage from "@/pages/LoginPage";
import AuthCallbackPage from "@/pages/AuthCallbackPage";
import DashboardPage from "@/pages/DashboardPage";
import ServersPage from "@/pages/ServersPage";
import MapsPage from "@/pages/MapsPage";
import RaidAlertsPage from "@/pages/RaidAlertsPage";
import RustPlusPage from "@/pages/RustPlusPage";
import EventsPage from "@/pages/EventsPage";
import TeamsPage from "@/pages/TeamsPage";
import AnalyticsPage from "@/pages/AnalyticsPage";
import SettingsPage from "@/pages/SettingsPage";
import AcceptInvitePage from "@/pages/AcceptInvitePage";

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<LandingPage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/auth/callback" element={<AuthCallbackPage />} />

      <Route element={<ProtectedRoute />}>
        <Route path="/teams/invite/:token" element={<AcceptInvitePage />} />

        <Route element={<AppLayout />}>
          <Route path="/settings" element={<SettingsPage />} />

            <Route path="/dashboard" element={<DashboardPage />} />
            <Route path="/servers" element={<ServersPage />} />
            <Route path="/maps" element={<MapsPage />} />
            <Route path="/raid-alerts" element={<RaidAlertsPage />} />
            <Route path="/rust-plus" element={<RustPlusPage />} />
            <Route path="/events" element={<EventsPage />} />
            <Route path="/teams" element={<TeamsPage />} />
            <Route path="/analytics" element={<AnalyticsPage />} />
        </Route>
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
