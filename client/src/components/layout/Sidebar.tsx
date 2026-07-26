import { motion } from "framer-motion";
import {
  LayoutDashboard,
  Server,
  Map,
  Siren,
  CalendarClock,
  Users,
  BarChart3,
  Settings,
  ChevronsLeft,
  ShieldAlert,
} from "lucide-react";
import { NavLink } from "react-router-dom";

const NAV_ITEMS = [
  { to: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
  { to: "/servers", label: "Servers", icon: Server },
  { to: "/maps", label: "Maps", icon: Map },
  { to: "/raid-alerts", label: "Raid Alerts", icon: Siren },
  { to: "/events", label: "Events", icon: CalendarClock },
  { to: "/teams", label: "Teams", icon: Users },
  { to: "/analytics", label: "Analytics", icon: BarChart3 },
  { to: "/settings", label: "Settings", icon: Settings },
];

interface SidebarProps {
  collapsed: boolean;
  onToggle: () => void;
}

export function Sidebar({ collapsed, onToggle }: SidebarProps) {
  return (
    <motion.aside
      animate={{ width: collapsed ? 72 : 232 }}
      transition={{ duration: 0.2, ease: "easeInOut" }}
      className="relative flex h-screen shrink-0 flex-col border-r border-white/5 bg-base-900"
    >
      <div className="flex h-16 items-center gap-2 px-4">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-blood/15 border border-blood/30">
          <ShieldAlert className="h-5 w-5 text-blood-light" />
        </div>
        {!collapsed && <span className="truncate text-sm font-semibold tracking-wide text-text-primary">RUSTEX</span>}
      </div>

      <nav className="mt-2 flex flex-1 flex-col gap-1 px-3">
        {NAV_ITEMS.map(({ to, label, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) =>
              [
                "group flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-all duration-150",
                isActive
                  ? "bg-blood/15 text-white border border-blood/30 shadow-glow-blood"
                  : "text-text-secondary hover:bg-white/5 hover:text-white",
              ].join(" ")
            }
          >
            <Icon className="h-5 w-5 shrink-0" />
            {!collapsed && <span className="truncate">{label}</span>}
          </NavLink>
        ))}
      </nav>

      <button
        onClick={onToggle}
        className="mx-3 mb-4 flex items-center justify-center gap-2 rounded-xl border border-white/5 py-2 text-text-muted transition-colors hover:text-white"
      >
        <motion.span animate={{ rotate: collapsed ? 180 : 0 }} transition={{ duration: 0.2 }}>
          <ChevronsLeft className="h-4 w-4" />
        </motion.span>
        {!collapsed && <span className="text-xs">Collapse</span>}
      </button>
    </motion.aside>
  );
}
