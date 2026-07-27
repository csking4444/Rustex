import { useState, type FormEvent } from "react";
import { MessageSquare, Send } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import { useRustPlusChat, useSendRustPlusChat } from "@/hooks/useRustPlusChat";

export function ChatAssistantTab({ serverId }: { serverId: string }) {
  const { data: messages, isLoading } = useRustPlusChat(serverId);
  const sendChat = useSendRustPlusChat(serverId);
  const [text, setText] = useState("");

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!text.trim()) return;
    sendChat.mutate(text.trim(), { onSuccess: () => setText("") });
  }

  return (
    <Card>
      <CardHeader title="Chat Assistant" subtitle="Live team chat — try !help, !pop, !time, !team, !alerts, !wipe, !pos, !device" />

      {isLoading && <SkeletonList rows={4} />}

      {!isLoading && (!messages || messages.length === 0) && (
        <div className="flex flex-col items-center justify-center gap-2 py-12 text-text-muted">
          <MessageSquare className="h-8 w-8" />
          <p className="text-sm">No team chat yet.</p>
        </div>
      )}

      {!isLoading && messages && messages.length > 0 && (
        <div className="mb-4 flex max-h-96 flex-col gap-2 overflow-y-auto">
          {messages.map((m, i) => (
            <div
              key={i}
              className={`max-w-[80%] rounded-xl px-3 py-2 text-sm ${
                m.isFromAssistant ? "self-end bg-blood/20 text-text-primary" : "self-start bg-base-800/60 text-text-secondary"
              }`}
            >
              <span className="mb-0.5 block text-xs font-semibold text-text-muted">{m.name}</span>
              {m.message}
            </div>
          ))}
        </div>
      )}

      <form onSubmit={handleSubmit} className="flex gap-2">
        <input
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="Message the team..."
          className="flex-1 rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
        />
        <button type="submit" disabled={sendChat.isPending} className="btn-primary">
          <Send className="h-4 w-4" />
        </button>
      </form>
    </Card>
  );
}
