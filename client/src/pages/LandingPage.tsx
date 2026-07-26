import { motion } from "framer-motion";
import { Link } from "react-router-dom";
import { Activity, BarChart3, MapPin, MessageSquareText, Server, Siren, Users } from "lucide-react";
import { RustexMark } from "@/components/brand/RustexMark";

const FEATURES = [
  { icon: Siren, title: "Tier-Based Raid Alarms", description: "Cluster explosions by time + location, classified into Tier 1/2/3 by count — not a guess, a threshold you control per server." },
  { icon: Server, title: "Live Server Status", description: "Real ping, population, and map data pulled directly from your server's query port." },
  { icon: MapPin, title: "Interactive Map", description: "Pin raids, teammates, and monuments on a pannable, zoomable map." },
  { icon: Users, title: "Team Management", description: "Invites, roles, and shared alerts so your whole squad sees the same picture." },
  { icon: MessageSquareText, title: "Chat Automation", description: "Template-driven alerts with placeholders for grid, event, player, and more." },
  { icon: BarChart3, title: "Analytics", description: "Raid frequency, peak hours, and server performance trends, computed live." },
];

export default function LandingPage() {
  return (
    <div className="min-h-screen w-full overflow-x-hidden bg-base-950 text-text-primary">
      <div className="pointer-events-none fixed inset-0 bg-[radial-gradient(circle_at_50%_0%,rgba(122,0,0,0.16),transparent_55%)]" />

      <header className="relative z-10 flex items-center justify-between px-6 py-5 sm:px-10">
        <div className="flex items-center gap-2.5">
          <RustexMark size={30} />
          <span className="text-lg font-bold tracking-wide">RUSTEX</span>
        </div>
        <Link to="/login" className="btn-primary">
          Sign In
        </Link>
      </header>

      <main className="relative z-10">
        <section className="mx-auto flex max-w-4xl flex-col items-center px-6 py-20 text-center sm:py-28">
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.5, ease: "easeOut" }}
          >
            <span className="badge-critical mb-6 inline-flex">Independent · Unofficial · Original</span>
            <h1 className="text-4xl font-bold tracking-tight sm:text-6xl">
              Know the second your base is <span className="text-blood-light">under attack.</span>
            </h1>
            <p className="mx-auto mt-6 max-w-xl text-base text-text-secondary sm:text-lg">
              Rustex is a premium companion app for Rust server communities — real-time raid alarms, live server
              intelligence, team coordination, and analytics, built from scratch with an original tactical interface.
            </p>
            <div className="mt-8 flex items-center justify-center gap-3">
              <Link to="/login" className="btn-primary px-6 py-3 text-base">
                Get Started
              </Link>
              <a href="#features" className="btn-ghost px-6 py-3 text-base">
                See Features
              </a>
            </div>
          </motion.div>
        </section>

        <section id="features" className="mx-auto max-w-6xl px-6 pb-24 sm:px-10">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map(({ icon: Icon, title, description }, i) => (
              <motion.div
                key={title}
                initial={{ opacity: 0, y: 16 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true, margin: "-40px" }}
                transition={{ duration: 0.35, delay: i * 0.05 }}
                className="glass-panel glass-panel-hover p-6"
              >
                <div className="mb-4 flex h-11 w-11 items-center justify-center rounded-xl border border-blood/30 bg-blood/15">
                  <Icon className="h-5 w-5 text-blood-light" />
                </div>
                <h3 className="text-sm font-semibold text-text-primary">{title}</h3>
                <p className="mt-1.5 text-sm text-text-muted">{description}</p>
              </motion.div>
            ))}
          </div>
        </section>

        <section className="mx-auto max-w-3xl px-6 pb-24 text-center sm:px-10">
          <div className="glass-panel flex flex-col items-center gap-4 p-10">
            <Activity className="h-8 w-8 text-blood-light" />
            <h2 className="text-2xl font-semibold">Ready to stop tabbing back to check your base?</h2>
            <p className="max-w-md text-sm text-text-muted">
              Sign in with Discord, Steam, or an email — your dashboard is ready in seconds.
            </p>
            <Link to="/login" className="btn-primary mt-2 px-6 py-3 text-base">
              Get Started Free
            </Link>
          </div>
        </section>
      </main>

      <footer className="relative z-10 border-t border-white/5 px-6 py-8 text-center text-xs text-text-muted sm:px-10">
        <p>
          Rustex is an independent, unofficial companion tool. Not affiliated with Facepunch Studios. All code, UI,
          and branding are original.
        </p>
      </footer>
    </div>
  );
}
