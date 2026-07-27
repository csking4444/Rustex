import { useState } from "react";
import { AlertTriangle, Check, CreditCard, ExternalLink, Gift, Loader2, RefreshCw } from "lucide-react";
import { Card, CardHeader } from "@/components/ui/Card";
import { Skeleton, SkeletonList } from "@/components/ui/Skeleton";
import {
  useCancelSubscription,
  useChangePlan,
  useInvoices,
  usePaymentMethod,
  usePlans,
  useResumeSubscription,
  useStartCheckout,
  useSubscription,
  useUpdatePaymentMethod,
} from "@/hooks/useBilling";
import type { BillingInterval, Plan, Subscription } from "@/types";

const FEATURE_LABELS: Record<string, string> = {
  raid_alarms: "Raid alarms",
  server_status: "Live server status",
  team_tracking: "Team tracking",
  smart_devices: "Smart devices",
  vending_search: "Vending search",
  shop_alerts: "Shop alerts",
  chat_assistant: "Chat assistant",
  analytics: "Analytics",
  phone_escalation: "Phone escalation",
};

function money(cents: number, currency = "usd") {
  return new Intl.NumberFormat(undefined, { style: "currency", currency }).format(cents / 100);
}

function date(value: string | null) {
  return value ? new Date(value).toLocaleDateString(undefined, { dateStyle: "medium" }) : "—";
}

/** Turns an API error into something a person can act on, without leaking internals. */
function errorMessage(error: unknown): string {
  const anyErr = error as { response?: { data?: { message?: string } } };
  return anyErr?.response?.data?.message ?? "Something went wrong. Please try again.";
}

export default function BillingPage() {
  const [interval, setInterval] = useState<BillingInterval>("Monthly");

  const plans = usePlans();
  const subscription = useSubscription();
  const invoices = useInvoices();
  const paymentMethod = usePaymentMethod();

  const checkout = useStartCheckout();
  const changePlan = useChangePlan();
  const cancel = useCancelSubscription();
  const resume = useResumeSubscription();
  const updateCard = useUpdatePaymentMethod();

  const sub = subscription.data;
  const busy = checkout.isPending || changePlan.isPending || cancel.isPending || resume.isPending;

  const actionError =
    checkout.error ?? changePlan.error ?? cancel.error ?? resume.error ?? updateCard.error;

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-xl font-semibold text-text-primary">Subscription &amp; Billing</h1>
        <p className="mt-1 text-sm text-text-muted">
          Manage your plan, payment method and invoices.
        </p>
      </div>

      {actionError && (
        <div className="flex items-start gap-3 rounded-xl border border-critical/30 bg-critical/10 p-4">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-critical" />
          <p className="text-sm text-text-secondary">{errorMessage(actionError)}</p>
        </div>
      )}

      <CurrentPlanCard
        subscription={sub}
        isLoading={subscription.isLoading}
        busy={busy}
        onCancel={() => cancel.mutate(false)}
        onResume={() => resume.mutate()}
      />

      <PaymentMethodCard
        isComplimentary={sub?.isComplimentary ?? false}
        hasSubscription={sub?.hasSubscription ?? false}
        isLoading={paymentMethod.isLoading}
        card={paymentMethod.data ?? null}
        onUpdate={() => updateCard.mutate()}
        updating={updateCard.isPending}
      />

      <section>
        <div className="mb-4 flex items-center justify-between gap-3">
          <h2 className="text-sm font-semibold text-text-primary">Plans</h2>
          <IntervalToggle value={interval} onChange={setInterval} />
        </div>

        {plans.isLoading ? (
          <div className="grid gap-4 md:grid-cols-3">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-72 w-full" />
            ))}
          </div>
        ) : (
          <div className="grid gap-4 md:grid-cols-3">
            {plans.data?.map((plan) => (
              <PlanCard
                key={plan.tier}
                plan={plan}
                interval={interval}
                subscription={sub}
                busy={busy}
                onSelect={() => {
                  if (sub?.hasSubscription && sub.isEntitled && !sub.isComplimentary) {
                    changePlan.mutate({ tier: plan.tier, interval });
                  } else {
                    checkout.mutate({ tier: plan.tier, interval });
                  }
                }}
              />
            ))}
          </div>
        )}
      </section>

      <InvoicesCard
        isLoading={invoices.isLoading}
        invoices={invoices.data ?? []}
        isComplimentary={sub?.isComplimentary ?? false}
      />
    </div>
  );
}

function IntervalToggle({
  value,
  onChange,
}: {
  value: BillingInterval;
  onChange: (next: BillingInterval) => void;
}) {
  return (
    <div className="inline-flex rounded-xl border border-white/10 p-0.5">
      {(["Monthly", "Yearly"] as const).map((option) => (
        <button
          key={option}
          type="button"
          onClick={() => onChange(option)}
          className={`rounded-lg px-3 py-1 text-xs font-medium transition-colors ${
            value === option ? "bg-blood text-white" : "text-text-muted hover:text-text-secondary"
          }`}
        >
          {option}
          {option === "Yearly" && <span className="ml-1 text-[10px] opacity-80">2 months free</span>}
        </button>
      ))}
    </div>
  );
}

function CurrentPlanCard({
  subscription,
  isLoading,
  busy,
  onCancel,
  onResume,
}: {
  subscription: Subscription | undefined;
  isLoading: boolean;
  busy: boolean;
  onCancel: () => void;
  onResume: () => void;
}) {
  if (isLoading) {
    return (
      <Card>
        <Skeleton className="mb-3 h-5 w-40" />
        <SkeletonList rows={2} />
      </Card>
    );
  }

  if (!subscription?.hasSubscription) {
    return (
      <Card>
        <CardHeader title="No active plan" subtitle="Pick a plan below to unlock the dashboard." />
        <p className="text-sm text-text-muted">
          You are signed in, but no plan is attached to this account yet.
        </p>
      </Card>
    );
  }

  const comp = subscription.isComplimentary;

  return (
    <Card>
      <CardHeader
        title={subscription.planName ?? "Plan"}
        subtitle={comp ? "Complimentary access" : `Billed ${subscription.interval?.toLowerCase() ?? "monthly"}`}
        action={<StatusBadge subscription={subscription} />}
      />

      {comp ? (
        // A granted plan is not a purchase. Showing a renewal date or a cancel button here would
        // be inventing a billing relationship that does not exist.
        <div className="flex items-start gap-3 rounded-xl border border-info/30 bg-info/10 p-4">
          <Gift className="mt-0.5 h-4 w-4 shrink-0 text-info" />
          <div className="text-sm text-text-secondary">
            <p>This access was granted directly, not bought — there is nothing to bill, renew or cancel.</p>
            {subscription.compReason && (
              <p className="mt-1 text-xs text-text-muted">Reason: {subscription.compReason}</p>
            )}
          </div>
        </div>
      ) : (
        <>
          <dl className="grid gap-4 sm:grid-cols-3">
            <Field label={subscription.cancelAtPeriodEnd ? "Access ends" : "Renews"} value={date(subscription.currentPeriodEnd)} />
            <Field label="Servers included" value={String(subscription.serverLimit)} />
            <Field label="Team members" value={String(subscription.teamMemberLimit)} />
          </dl>

          {subscription.cancelAtPeriodEnd ? (
            <div className="mt-4 flex flex-wrap items-center gap-3 rounded-xl border border-warning/30 bg-warning/10 p-4">
              <p className="flex-1 text-sm text-text-secondary">
                Scheduled to cancel on {date(subscription.currentPeriodEnd)}. You keep full access until then.
              </p>
              <button type="button" className="btn-primary" disabled={busy} onClick={onResume}>
                {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
                Keep my plan
              </button>
            </div>
          ) : (
            <div className="mt-4 flex justify-end">
              <button type="button" className="btn-ghost" disabled={busy} onClick={onCancel}>
                Cancel subscription
              </button>
            </div>
          )}
        </>
      )}
    </Card>
  );
}

function StatusBadge({ subscription }: { subscription: Subscription }) {
  if (subscription.isComplimentary) return <span className="badge-info">Complimentary</span>;

  switch (subscription.status) {
    case "Active":
      return <span className="badge-success">Active</span>;
    case "Trialing":
      return <span className="badge-info">Trial</span>;
    case "PastDue":
      // Still entitled — the provider is retrying the card. Say so rather than implying lockout.
      return <span className="badge-warning">Payment retrying</span>;
    case "Canceled":
      return <span className="badge-critical">Cancelled</span>;
    case "Unpaid":
      return <span className="badge-critical">Unpaid</span>;
    default:
      return <span className="badge-warning">{subscription.status ?? "Pending"}</span>;
  }
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-text-muted">{label}</dt>
      <dd className="mt-0.5 text-sm text-text-primary">{value}</dd>
    </div>
  );
}

function PlanCard({
  plan,
  interval,
  subscription,
  busy,
  onSelect,
}: {
  plan: Plan;
  interval: BillingInterval;
  subscription: Subscription | undefined;
  busy: boolean;
  onSelect: () => void;
}) {
  const price = interval === "Yearly" ? plan.yearlyCents : plan.monthlyCents;
  const isCurrent = subscription?.tier === plan.tier && subscription.interval === interval;
  const comp = subscription?.isComplimentary ?? false;

  return (
    <Card className={isCurrent ? "border-blood/50" : "glass-panel-hover"}>
      <div className="flex items-baseline justify-between">
        <h3 className="text-sm font-semibold text-text-primary">{plan.name}</h3>
        {isCurrent && <span className="badge-success">Current</span>}
      </div>

      <p className="mt-1 text-xs text-text-muted">{plan.description}</p>

      <p className="mt-4">
        <span className="text-2xl font-semibold text-text-primary">{money(price)}</span>
        <span className="text-xs text-text-muted">/{interval === "Yearly" ? "yr" : "mo"}</span>
      </p>

      <ul className="mt-4 flex flex-col gap-1.5">
        <li className="flex items-center gap-2 text-xs text-text-secondary">
          <Check className="h-3.5 w-3.5 text-success" />
          {plan.serverLimit} paired server{plan.serverLimit === 1 ? "" : "s"}
        </li>
        {plan.features.map((feature) => (
          <li key={feature} className="flex items-center gap-2 text-xs text-text-secondary">
            <Check className="h-3.5 w-3.5 text-success" />
            {FEATURE_LABELS[feature] ?? feature}
          </li>
        ))}
      </ul>

      <button
        type="button"
        className="btn-primary mt-5 w-full"
        disabled={busy || isCurrent || comp || !plan.purchasable}
        onClick={onSelect}
      >
        {busy && <Loader2 className="h-4 w-4 animate-spin" />}
        {isCurrent ? "Current plan" : subscription?.isEntitled && !comp ? "Switch to this plan" : "Choose plan"}
      </button>

      {!plan.purchasable && (
        // Honest rather than a button that silently fails: checkout genuinely is not connected
        // on this deployment until the provider keys are configured.
        <p className="mt-2 text-center text-[11px] text-text-muted">Checkout is not connected yet.</p>
      )}
      {comp && plan.purchasable && (
        <p className="mt-2 text-center text-[11px] text-text-muted">
          You have complimentary access.
        </p>
      )}
    </Card>
  );
}

function PaymentMethodCard({
  isComplimentary,
  hasSubscription,
  isLoading,
  card,
  onUpdate,
  updating,
}: {
  isComplimentary: boolean;
  hasSubscription: boolean;
  isLoading: boolean;
  card: { brand: string | null; last4: string | null; expMonth: number | null; expYear: number | null } | null;
  onUpdate: () => void;
  updating: boolean;
}) {
  if (isComplimentary || !hasSubscription) return null;

  return (
    <Card>
      <CardHeader
        title="Payment method"
        subtitle="Card details are handled entirely by our payment provider and never reach our servers."
        action={
          <button type="button" className="btn-ghost" onClick={onUpdate} disabled={updating}>
            {updating ? <Loader2 className="h-4 w-4 animate-spin" /> : <ExternalLink className="h-4 w-4" />}
            Update
          </button>
        }
      />

      {isLoading ? (
        <Skeleton className="h-6 w-48" />
      ) : card?.last4 ? (
        <p className="flex items-center gap-2 text-sm text-text-secondary">
          <CreditCard className="h-4 w-4 text-text-muted" />
          <span className="capitalize">{card.brand}</span> ending {card.last4}
          {card.expMonth && card.expYear && (
            <span className="text-text-muted">
              · expires {String(card.expMonth).padStart(2, "0")}/{card.expYear}
            </span>
          )}
        </p>
      ) : (
        <p className="text-sm text-text-muted">No card on file yet.</p>
      )}
    </Card>
  );
}

function InvoicesCard({
  isLoading,
  invoices,
  isComplimentary,
}: {
  isLoading: boolean;
  invoices: import("@/types").Invoice[];
  isComplimentary: boolean;
}) {
  return (
    <Card>
      <CardHeader title="Billing history" subtitle={`${invoices.length} invoice${invoices.length === 1 ? "" : "s"}`} />

      {isLoading ? (
        <SkeletonList rows={3} />
      ) : invoices.length === 0 ? (
        <p className="text-sm text-text-muted">
          {isComplimentary
            ? "Complimentary access is never charged, so there are no invoices."
            : "No invoices yet. They will appear here after your first payment."}
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-white/5 text-xs text-text-muted">
                <th className="pb-2 font-medium">Date</th>
                <th className="pb-2 font-medium">Invoice</th>
                <th className="pb-2 font-medium">Amount</th>
                <th className="pb-2 font-medium">Status</th>
                <th className="pb-2" />
              </tr>
            </thead>
            <tbody>
              {invoices.map((invoice) => (
                <tr key={invoice.id} className="border-b border-white/5 last:border-0">
                  <td className="py-2.5 text-text-secondary">{date(invoice.issuedAt)}</td>
                  <td className="py-2.5 font-mono text-xs text-text-muted">{invoice.number ?? "—"}</td>
                  <td className="py-2.5 text-text-primary">
                    {money(invoice.amountPaidCents || invoice.amountDueCents, invoice.currency)}
                  </td>
                  <td className="py-2.5">
                    {invoice.status === "Paid" ? (
                      <span className="badge-success">Paid</span>
                    ) : invoice.status === "Open" ? (
                      <span className="badge-warning">Due</span>
                    ) : (
                      <span className="badge-info">{invoice.status}</span>
                    )}
                  </td>
                  <td className="py-2.5 text-right">
                    {invoice.hostedInvoiceUrl && (
                      <a
                        href={invoice.hostedInvoiceUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="inline-flex items-center gap-1 text-xs text-info hover:underline"
                      >
                        View <ExternalLink className="h-3 w-3" />
                      </a>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}
