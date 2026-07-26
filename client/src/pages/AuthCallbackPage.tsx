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

    const params = new URLSearchParams(window.location.hash.replace(/^#/, ""));
    const accessToken = params.get("access_token");
    const refreshToken = params.get("refresh_token");

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
