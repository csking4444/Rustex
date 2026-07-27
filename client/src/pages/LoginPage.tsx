import { useEffect, useState, type FormEvent } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { motion } from "framer-motion";
import { ShieldAlert } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { RustexMark } from "@/components/brand/RustexMark";

const OAUTH_ERROR_MESSAGES: Record<string, string> = {
  steam_already_linked: "That Steam account is already linked to a different Rustex account.",
};

export default function LoginPage() {
  const { isAuthenticated, isLoading, loginWithGoogle, loginWithSteam, loginWithPassword, registerWithPassword } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const [mode, setMode] = useState<"login" | "register">("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [username, setUsername] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (!isLoading && isAuthenticated) navigate("/dashboard", { replace: true });
  }, [isLoading, isAuthenticated, navigate]);

  useEffect(() => {
    const oauthError = searchParams.get("error");
    if (oauthError) setError(OAUTH_ERROR_MESSAGES[oauthError] ?? "That sign-in method couldn't be linked.");
  }, [searchParams]);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);

    try {
      if (mode === "login") {
        await loginWithPassword(email, password);
      } else {
        await registerWithPassword(email, password, username);
      }
      navigate("/dashboard", { replace: true });
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: string } })?.response?.data ??
        (mode === "login" ? "Invalid email or password." : "Couldn't create that account.");
      setError(String(message));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="relative flex h-screen w-screen items-center justify-center overflow-hidden bg-base-950">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_50%_20%,rgba(122,0,0,0.18),transparent_60%)]" />

      <a href="/" className="absolute left-6 top-6 flex items-center gap-2">
        <RustexMark size={28} />
        <span className="text-sm font-bold tracking-wide text-text-primary">RUSTEX</span>
      </a>

      <motion.div
        initial={{ opacity: 0, y: 16 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4, ease: "easeOut" }}
        className="glass-panel relative w-full max-w-sm p-8 text-center"
      >
        <div className="mx-auto mb-5 flex h-14 w-14 items-center justify-center rounded-2xl border border-blood/30 bg-blood/15">
          <ShieldAlert className="h-7 w-7 text-blood-light" />
        </div>

        <h1 className="text-xl font-semibold tracking-tight text-text-primary">
          {mode === "login" ? "Welcome back" : "Create your account"}
        </h1>
        <p className="mt-2 text-sm text-text-muted">Real-time raid alarms and server intelligence for Rust.</p>

        <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-3 text-left">
          {mode === "register" && (
            <input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="Username"
              required
              minLength={2}
              className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-blood-light/60"
            />
          )}
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="Email"
            required
            className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-blood-light/60"
          />
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="Password"
            required
            minLength={8}
            className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-blood-light/60"
          />

          {error && <p className="text-xs text-critical">{error}</p>}

          <button type="submit" disabled={isSubmitting} className="btn-primary mt-1 w-full">
            {isSubmitting ? "Please wait..." : mode === "login" ? "Log in" : "Create account"}
          </button>
        </form>

        <button
          onClick={() => {
            setMode(mode === "login" ? "register" : "login");
            setError(null);
          }}
          className="mt-3 text-xs text-text-muted underline-offset-2 hover:text-white hover:underline"
        >
          {mode === "login" ? "Need an account? Sign up" : "Already have an account? Log in"}
        </button>

        <div className="my-5 flex items-center gap-3">
          <div className="h-px flex-1 bg-white/10" />
          <span className="text-[11px] uppercase tracking-wider text-text-muted">or continue with</span>
          <div className="h-px flex-1 bg-white/10" />
        </div>

        <div className="flex flex-col gap-2">
          <button onClick={loginWithGoogle} className="btn-ghost w-full justify-center">
            Google
          </button>
          <button onClick={() => loginWithSteam()} className="btn-ghost w-full justify-center">
            Steam
          </button>
          <button
            onClick={() => loginWithSteam(true)}
            className="text-xs text-text-muted underline-offset-2 hover:text-white hover:underline"
          >
            Use a different Steam account
          </button>
        </div>

        <p className="mt-6 text-xs text-text-muted">
          By continuing you agree this is an independent, unofficial companion tool.
        </p>
      </motion.div>
    </div>
  );
}
