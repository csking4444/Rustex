import { useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { tokenStorage } from "@/lib/tokenStorage";
import { useAuth } from "@/context/AuthContext";

export default function AuthCallbackPage() {
  const navigate = useNavigate();
  const { refetchUser } = useAuth();
  const hasRun = useRef(false);

  useEffect(() => {
    if (hasRun.current) return;
    hasRun.current = true;

    // A linking failure (e.g. that Steam account is already linked elsewhere) lands here as a
    // plain query param, not a token — surface it on the login page instead of trying to parse it
    // as a session.
    const searchParams = new URLSearchParams(window.location.search);
    const error = searchParams.get("error");
    if (error) {
      navigate(`/login?error=${encodeURIComponent(error)}`, { replace: true });
      return;
    }

    const params = new URLSearchParams(window.location.hash.replace(/^#/, ""));
    const accessToken = params.get("access_token");
    const refreshToken = params.get("refresh_token");

    // Tokens arrive in the URL fragment so they never hit server logs, but the browser still
    // keeps the fragment in history — scrub it immediately so it doesn't sit in the address bar
    // or come back on the back button.
    window.history.replaceState(null, "", window.location.pathname);

    if (accessToken && refreshToken) {
      tokenStorage.setTokens(accessToken, refreshToken);
      void refetchUser().then(() => navigate("/dashboard", { replace: true }));
    } else {
      navigate("/login", { replace: true });
    }
  }, [navigate, refetchUser]);

  return (
    <div className="flex h-screen w-screen items-center justify-center bg-base-950">
      <div className="h-10 w-10 animate-spin rounded-full border-2 border-blood border-t-transparent" />
    </div>
  );
}
