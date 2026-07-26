import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { CheckCircle2, XCircle } from "lucide-react";
import { apiClient } from "@/lib/apiClient";

export default function AcceptInvitePage() {
  const { token } = useParams<{ token: string }>();
  const navigate = useNavigate();
  const [status, setStatus] = useState<"pending" | "success" | "error">("pending");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) return;

    apiClient
      .post(`/team-invites/${token}/accept`)
      .then(() => {
        setStatus("success");
        setTimeout(() => navigate("/teams", { replace: true }), 1500);
      })
      .catch((err) => {
        setStatus("error");
        setError(err?.response?.data ?? "This invite could not be accepted.");
      });
  }, [token, navigate]);

  return (
    <div className="flex h-screen w-screen flex-col items-center justify-center gap-4 bg-base-950 text-center">
      {status === "pending" && <div className="h-10 w-10 animate-spin rounded-full border-2 border-blood border-t-transparent" />}
      {status === "success" && (
        <>
          <CheckCircle2 className="h-10 w-10 text-success" />
          <p className="text-sm text-text-secondary">Joined the team — redirecting...</p>
        </>
      )}
      {status === "error" && (
        <>
          <XCircle className="h-10 w-10 text-critical" />
          <p className="text-sm text-text-secondary">{String(error)}</p>
        </>
      )}
    </div>
  );
}
