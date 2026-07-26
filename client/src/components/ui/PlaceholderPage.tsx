import type { LucideIcon } from "lucide-react";

interface PlaceholderPageProps {
  icon: LucideIcon;
  title: string;
  description: string;
  phase: string;
}

export function PlaceholderPage({ icon: Icon, title, description, phase }: PlaceholderPageProps) {
  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-semibold text-text-primary">{title}</h1>
        <p className="mt-1 text-sm text-text-muted">{description}</p>
      </div>

      <div className="glass-panel flex flex-col items-center justify-center gap-3 py-24 text-center">
        <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-blood/15 border border-blood/30">
          <Icon className="h-7 w-7 text-blood-light" />
        </div>
        <p className="text-sm font-medium text-text-secondary">Coming in {phase}</p>
        <p className="max-w-sm text-xs text-text-muted">
          This section is designed in docs/ROADMAP.md but not yet implemented — Phase 1 covers auth, servers, and the dashboard shell.
        </p>
      </div>
    </div>
  );
}
