import { useState, type FormEvent } from "react";
import { Eye, MessageSquareText, Plus, Save, Trash2 } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { SkeletonList } from "@/components/ui/Skeleton";
import {
  useChatTemplateMetadata,
  useCreateMessageTemplate,
  useDeleteMessageTemplate,
  useMessageTemplates,
  usePreviewTemplate,
  useUpdateMessageTemplate,
} from "@/hooks/useMessageTemplates";
import type { MessageTemplate } from "@/types";

export function MessageTemplatesPanel({ teamId }: { teamId: string }) {
  const { data: templates, isLoading } = useMessageTemplates(teamId);
  const { data: metadata } = useChatTemplateMetadata();
  const createTemplate = useCreateMessageTemplate(teamId);

  const [eventType, setEventType] = useState("");
  const [templateText, setTemplateText] = useState("");

  const usedEventTypes = new Set(templates?.map((t) => t.eventType) ?? []);
  const availableEventTypes = (metadata?.eventTypes ?? []).filter((e) => !usedEventTypes.has(e));

  function handleCreate(e: FormEvent) {
    e.preventDefault();
    if (!eventType || !templateText.trim()) return;
    createTemplate.mutate(
      { eventType, templateText, isEnabled: true, cooldownSeconds: 30, serverId: null },
      { onSuccess: () => setTemplateText("") },
    );
  }

  return (
    <Card>
      <CardHeader
        title="Team Chat Templates"
        subtitle="Auto-post into Rust team chat when these events fire — placeholders: {server} {grid} {time} {event} {player} {count} {team} {weapon}"
      />

      <form onSubmit={handleCreate} className="mb-4 flex flex-col gap-3 rounded-xl border border-white/5 bg-base-800/40 p-4">
        <div className="flex gap-3">
          <select
            value={eventType}
            onChange={(e) => setEventType(e.target.value)}
            className="rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
          >
            <option value="">Add template for event...</option>
            {availableEventTypes.map((et) => (
              <option key={et} value={et}>
                {et}
              </option>
            ))}
          </select>
          <button type="submit" disabled={!eventType || createTemplate.isPending} className="btn-primary">
            <Plus className="h-4 w-4" />
            Add
          </button>
        </div>
        {eventType && (
          <textarea
            value={templateText}
            onChange={(e) => setTemplateText(e.target.value)}
            placeholder={`e.g. Raid detected at {grid}! {count} explosions on {server}`}
            rows={2}
            className="w-full rounded-xl border border-white/10 bg-base-800/60 px-3 py-2 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-blood-light/60"
          />
        )}
      </form>

      {isLoading && <SkeletonList rows={3} />}

      {!isLoading && templates?.length === 0 && (
        <div className="flex flex-col items-center justify-center gap-2 py-8 text-text-muted">
          <MessageSquareText className="h-8 w-8" />
          <p className="text-sm">No templates configured yet.</p>
        </div>
      )}

      {!isLoading && templates && templates.length > 0 && (
        <ul className="flex flex-col gap-3">
          {templates.map((template) => (
            <TemplateRow key={template.id} teamId={teamId} template={template} />
          ))}
        </ul>
      )}
    </Card>
  );
}

function TemplateRow({ teamId, template }: { teamId: string; template: MessageTemplate }) {
  const [text, setText] = useState(template.templateText);
  const [isEnabled, setIsEnabled] = useState(template.isEnabled);
  const [cooldown, setCooldown] = useState(template.cooldownSeconds);
  const [preview, setPreview] = useState<string | null>(null);

  const updateTemplate = useUpdateMessageTemplate(teamId);
  const deleteTemplate = useDeleteMessageTemplate(teamId);
  const previewTemplate = usePreviewTemplate();

  const dirty = text !== template.templateText || isEnabled !== template.isEnabled || cooldown !== template.cooldownSeconds;

  return (
    <li className="rounded-xl border border-white/5 bg-base-800/40 p-4">
      <div className="mb-2 flex items-center justify-between">
        <span className="text-sm font-medium text-text-primary">{template.eventType}</span>
        <div className="flex items-center gap-3">
          <label className="flex items-center gap-1.5 text-xs text-text-muted">
            <input
              type="checkbox"
              checked={isEnabled}
              onChange={(e) => setIsEnabled(e.target.checked)}
              className="h-3.5 w-3.5 rounded border-white/20 bg-base-800 accent-blood"
            />
            Enabled
          </label>
          <button
            onClick={() => deleteTemplate.mutate(template.id)}
            className="text-text-muted transition-colors hover:text-critical"
            aria-label="Delete template"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      <textarea
        value={text}
        onChange={(e) => setText(e.target.value)}
        rows={2}
        className="w-full rounded-xl border border-white/10 bg-base-900/60 px-3 py-2 text-sm text-text-primary focus:outline-none focus:ring-2 focus:ring-blood-light/60"
      />

      <div className="mt-2 flex items-center justify-between">
        <label className="flex items-center gap-2 text-xs text-text-muted">
          Cooldown
          <input
            type="number"
            min={0}
            value={cooldown}
            onChange={(e) => setCooldown(Number(e.target.value))}
            className="w-20 rounded-lg border border-white/10 bg-base-900/60 px-2 py-1 text-xs text-text-primary focus:outline-none"
          />
          seconds
        </label>

        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => previewTemplate.mutate({ templateText: text, eventType: template.eventType }, { onSuccess: (d) => setPreview(d.rendered) })}
            className="btn-ghost px-3 py-1.5 text-xs"
          >
            <Eye className="h-3.5 w-3.5" />
            Preview
          </button>
          {dirty && (
            <button
              type="button"
              onClick={() => updateTemplate.mutate({ id: template.id, templateText: text, isEnabled, cooldownSeconds: cooldown })}
              disabled={updateTemplate.isPending}
              className="btn-primary px-3 py-1.5 text-xs"
            >
              <Save className="h-3.5 w-3.5" />
              Save
            </button>
          )}
        </div>
      </div>

      {preview && (
        <p className="mt-2 rounded-lg border border-white/5 bg-base-950/60 px-3 py-2 text-xs text-text-secondary">
          <span className="text-text-muted">Preview: </span>
          {preview}
        </p>
      )}
    </li>
  );
}
