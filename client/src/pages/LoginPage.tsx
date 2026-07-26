import { motion } from "framer-motion";
import { ShieldAlert } from "lucide-react";
import { useAuth } from "@/context/AuthContext";

export default function LoginPage() {
  const { loginWithDiscord } = useAuth();

  return (
    <div className="relative flex h-screen w-screen items-center justify-center overflow-hidden bg-base-950">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_50%_20%,rgba(122,0,0,0.18),transparent_60%)]" />

      <motion.div
        initial={{ opacity: 0, y: 16 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4, ease: "easeOut" }}
        className="glass-panel relative w-full max-w-sm p-8 text-center"
      >
        <div className="mx-auto mb-5 flex h-14 w-14 items-center justify-center rounded-2xl bg-blood/15 border border-blood/30">
          <ShieldAlert className="h-7 w-7 text-blood-light" />
        </div>

        <h1 className="text-xl font-semibold tracking-tight text-text-primary">Rustex</h1>
        <p className="mt-2 text-sm text-text-muted">
          Real-time raid alarms, team coordination, and server intelligence for Rust.
        </p>

        <button onClick={loginWithDiscord} className="btn-primary mt-8 w-full">
          Continue with Discord
        </button>

        <p className="mt-6 text-xs text-text-muted">
          By continuing you agree this is an independent, unofficial companion tool.
        </p>
      </motion.div>
    </div>
  );
}
