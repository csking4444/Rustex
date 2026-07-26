import { useState, type FormEvent } from "react";
import { Link2, Plus, Trash2 } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { useCreateWebhook, useDeleteWebhook, useWebhooks } from "@/hooks/useWebhooks";

export function DiscordWebhookPanel({ serverId }: { serverId: string }) {
  const { data: webhooks, isLoading } = useWebhooks(serverId);
  const createWebhook = useCreateWebhook(serverId);
  const deleteWebhook = useDeleteWebhook(serverId);
  const [url, setUrl] = useState("");

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!url.trim()) return;
    createWebhook.mutate(url, { onSuccess: () => setUrl("") });
  }

  return (
    <Card>
      <CardHeader
        title="Discord Webhooks"
        subtitle="Posted here when a raid is detected — also requires Discord Webhooks enabled in Settings"
      />

      <form onSubmit={handleSubmit} className="mb-3 flex gap-3">
        <input
          value={url}
          onChange={(e) => setUrl(e.target.value)}
          placeholder="https://discord.com/api/webhooks/..."
          className="flex-1 rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-blood-light/60"
        />
        <button type="submit" disabled={createWebhook.isPending} className="btn-primary">
          <Plus className="h-4 w-4" />
          Add
        </button>
      </form>

      {createWebhook.isError && <p className="mb-2 text-xs text-critical">Couldn't add that webhook — must be a valid https:// URL.</p>}

      {!isLoading && webhooks?.length === 0 && (
        <p className="text-xs text-text-muted">No webhooks configured for this server yet.</p>
      )}

      {webhooks && webhooks.length > 0 && (
        <ul className="flex flex-col gap-2">
          {webhooks.map((webhook) => (
            <li key={webhook.id} className="flex items-center justify-between rounded-lg border border-white/5 bg-base-800/40 px-3 py-2">
              <div className="flex min-w-0 items-center gap-2">
                <Link2 className="h-3.5 w-3.5 shrink-0 text-text-muted" />
                <span className="truncate text-xs text-text-secondary">{webhook.url}</span>
              </div>
              <button onClick={() => deleteWebhook.mutate(webhook.id)} className="shrink-0 text-text-muted hover:text-critical" aria-label="Remove webhook">
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
