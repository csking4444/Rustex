import { Bell, LogOut, Search } from "lucide-react";
import { useAuth } from "@/context/AuthContext";

interface TopbarProps {
  onOpenNotifications: () => void;
  notificationCount: number;
}

export function Topbar({ onOpenNotifications, notificationCount }: TopbarProps) {
  const { user, logout } = useAuth();

  return (
    <header className="flex h-16 shrink-0 items-center justify-between border-b border-white/5 bg-base-900/80 px-6 backdrop-blur">
      <div className="flex w-full max-w-md items-center gap-2 rounded-xl border border-white/5 bg-base-800/60 px-3 py-2">
        <Search className="h-4 w-4 text-text-muted" />
        <input
          type="text"
          placeholder="Search servers, players, grids..."
          className="w-full bg-transparent text-sm text-text-primary placeholder:text-text-muted focus:outline-none"
        />
      </div>

      <div className="flex items-center gap-4">
        <button
          onClick={onOpenNotifications}
          className="relative flex h-10 w-10 items-center justify-center rounded-xl border border-white/5 text-text-secondary transition-colors hover:text-white"
        >
          <Bell className="h-4 w-4" />
          {notificationCount > 0 && (
            <span className="absolute -right-1 -top-1 flex h-4.5 min-w-[1.125rem] items-center justify-center rounded-full bg-critical px-1 text-[10px] font-semibold text-white animate-pulse-glow">
              {notificationCount}
            </span>
          )}
        </button>

        <div className="flex items-center gap-3 border-l border-white/5 pl-4">
          {user?.discordAvatar ? (
            <img
              src={`https://cdn.discordapp.com/avatars/${user.id}/${user.discordAvatar}.png`}
              alt={user.discordUsername}
              className="h-8 w-8 rounded-full border border-white/10"
            />
          ) : (
            <div className="flex h-8 w-8 items-center justify-center rounded-full bg-base-700 text-xs font-semibold text-text-secondary">
              {user?.discordUsername?.slice(0, 2).toUpperCase()}
            </div>
          )}
          <span className="hidden text-sm font-medium text-text-secondary sm:block">
            {user?.displayName ?? user?.discordUsername}
          </span>
          <button onClick={() => void logout()} className="text-text-muted transition-colors hover:text-critical">
            <LogOut className="h-4 w-4" />
          </button>
        </div>
      </div>
    </header>
  );
}
