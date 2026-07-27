import { useState } from "react";
import { Copy, Terminal, Wifi, WifiOff } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { useCreateRustPlusLinkCode, useRustPlusCredentialStatus } from "@/hooks/useRustPlusAccount";

/** Account-level Rust+ auto-pairing status — separate from per-server manual pairing below it on
 * the page. Generates a one-time setup code for the `rustex-pair` local helper (see
 * docs/RUSTPLUS.md); once credentials are Active, pressing "Pair With Server" in-game registers
 * new servers automatically without ever needing this card again. */
export function RustPlusAccountSetup() {
  const { data: status, isLoading } = useRustPlusCredentialStatus();
  const createLinkCode = useCreateRustPlusLinkCode();
  const [copied, setCopied] = useState(false);

  if (isLoading) return null;

  if (status?.hasCredentials && status.status === "Active") {
    return (
      <Card className="border-success/20">
        <div className="flex items-center gap-3">
          <Wifi className="h-5 w-5 text-success" />
          <div>
            <p className="text-sm font-medium text-text-primary">Auto-pairing is active</p>
            <p className="text-xs text-text-muted">
              Press ESC in game, open Rust+, and tap "Pair With Server" — it'll show up here automatically.
            </p>
          </div>
        </div>
      </Card>
    );
  }

  const needsReauth = status?.hasCredentials && status.status === "NeedsReauth";

  return (
    <Card className={needsReauth ? "border-warning/30" : undefined}>
      <CardHeader
        title={needsReauth ? "Rust+ auto-pairing needs reconnecting" : "Set up Rust+ auto-pairing"}
        subtitle="Optional — manual pairing per server below always works without this."
      />

      <div className="flex flex-col gap-4">
        <div className="flex items-start gap-3 text-sm text-text-secondary">
          <WifiOff className="mt-0.5 h-4 w-4 shrink-0 text-text-muted" />
          <p>
            Run the <code className="rounded bg-base-800 px-1.5 py-0.5 text-xs">rustex-pair</code> helper on your own
            PC once — your Steam login happens in your own browser and never touches Rustex's servers. After that,
            every server you pair in-game appears here automatically.
          </p>
        </div>

        {createLinkCode.data ? (
          <div className="flex flex-col gap-2 rounded-xl border border-white/10 bg-base-800/60 p-4">
            <span className="text-xs text-text-muted">Setup code (expires {new Date(createLinkCode.data.expiresAt).toLocaleTimeString()})</span>
            <div className="flex items-center gap-2">
              <code className="flex-1 rounded-lg bg-base-900 px-3 py-2 font-mono text-base tracking-wider text-text-primary">
                {createLinkCode.data.code}
              </code>
              <button
                type="button"
                onClick={() => {
                  void navigator.clipboard.writeText(createLinkCode.data!.code);
                  setCopied(true);
                  setTimeout(() => setCopied(false), 1500);
                }}
                className="btn-ghost"
              >
                <Copy className="h-4 w-4" />
                {copied ? "Copied" : "Copy"}
              </button>
            </div>
            <code className="mt-2 block rounded-lg bg-base-900 px-3 py-2 font-mono text-xs text-text-muted">
              rustex-pair --code {createLinkCode.data.code}
            </code>
          </div>
        ) : (
          <button
            type="button"
            onClick={() => createLinkCode.mutate()}
            disabled={createLinkCode.isPending}
            className="btn-primary self-start"
          >
            <Terminal className="h-4 w-4" />
            {createLinkCode.isPending ? "Generating..." : "Generate Setup Code"}
          </button>
        )}
      </div>
    </Card>
  );
}
