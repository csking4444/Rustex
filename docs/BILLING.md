# Billing, live sync, and API security

Three subsystems added together, documented here because they interlock: entitlement decides who
gets live data, and both rely on the same authorization rules.

---

## 1. Subscriptions

### What is real

Payments run through **Stripe Billing**. Checkout, proration, renewals, invoices and dunning are
all genuinely Stripe's — we do not generate invoice numbers, compute renewal dates, or decide
whether a card succeeded. In test mode (`sk_test_…`) the entire flow works end to end without
moving real money.

If `Stripe:SecretKey` is unset the API still runs. `GET /api/billing/plans` then reports
`purchasable: false` and the UI says checkout is not connected, rather than showing a button that
would fail.

### Plans

Tiers live in code, in [`PlanCatalog`](../server/src/Rustex.Domain/Billing/PlanCatalog.cs) — not in
a database table. Entitlement rules are logic, and a table would let production drift from what
this repo says is enforced.

| Tier | Monthly | Yearly | Servers | Members | Adds |
|---|---|---|---|---|---|
| Scout | $4.99 | $49.90 | 2 | 5 | Raid alarms, live server status |
| Raider | $9.99 | $99.90 | 10 | 20 | Team tracking, smart devices, vending search, shop alerts |
| Clan | $19.99 | $199.90 | 25 | 100 | Chat assistant, analytics, phone escalation |

The catalog is the source of truth for **limits and features**. Stripe is the source of truth for
**what is actually charged**. If the two disagree, the customer is charged Stripe's number — so
the Price objects must be kept in step with the table above.

### The one rule that keeps this honest

> The provider is the source of truth for money, and **the webhook is the only path that writes
> paid state**.

Request handlers ask Stripe to make a change and then re-read it. They never assume the change
took, and they never take the client's word for a plan. `GET /api/billing/entitlement` resolves
server-side on every call rather than reading a claim out of the JWT — a token minted before
someone cancelled would otherwise keep working until it expired.

### Upgrades vs downgrades

- **Upgrade** → `ProrationBehavior = always_invoice`. The difference is charged immediately.
- **Downgrade** → `ProrationBehavior = none`. The cheaper tier starts next period; we do not
  refund time the customer has already been served.

Monthly → yearly on the same tier counts as an upgrade.

### Cancellation

Cancelling defaults to **end of period**: access continues until `CurrentPeriodEnd`, which is what
was already paid for. `POST /api/billing/resume` undoes a pending cancellation while the period is
still running.

`PastDue` still entitles. Stripe is retrying the card, and locking someone out mid-retry loses
customers who would have paid. `Unpaid` and `Canceled` do not entitle.

### Complimentary access

A granted plan is `Source = Complimentary`. It has no billing period, no card, and no invoices,
and those fields are deliberately left null so nothing downstream can render a renewal date or a
charge that will never happen. The API refuses to cancel it, change its plan, or open a billing
portal for it.

This replaces the old `COMPED_ACCOUNTS` environment-variable allowlist on the static site with a
real row that can be listed, reasoned about and revoked.

### Card data

**No endpoint in this API accepts a card number, CVC, or bank detail**, and there is no shape in
`IPaymentProvider` that could carry one. Card entry happens on Stripe's hosted Checkout and
Billing Portal pages. We store only `brand`, `last4` and expiry for display, and hold an opaque
payment-method id. That is what keeps this system out of PCI scope, and any future provider
implementation must preserve it.

### Webhook

`POST /api/billing/webhook` is the only endpoint that is both unauthenticated and state-changing,
because Stripe has no session with us. Its security is entirely the signature check:

1. **Verify before parsing.** The raw body is HMAC'd with the signing secret; the timestamp
   tolerance also rejects a captured-and-replayed delivery. Model binding would change the bytes
   and break verification, so the body is read raw.
2. **Claim the event id first.** A unique index on `ProcessedWebhookEvents.ProviderEventId` is the
   idempotency mechanism — a duplicate delivery loses the insert race and is skipped. Deciding
   this with a prior read would leave a window where two concurrent deliveries both proceeded.
3. **Status codes mean retry.** Non-2xx makes Stripe retry, so real handling failures return 500
   while events we intentionally ignore return 200.

Out-of-order delivery is handled by `ProviderUpdatedAt`: an event older than what we have already
applied is skipped rather than rolling state backwards.

The webhook path is exempt from IP rate limiting (`IpRateLimiting:EndpointWhitelist`) — Stripe
delivers from a small address range and would otherwise be throttled. That is safe precisely
because the signature, not the source IP, is what authenticates it.

### Local setup

```bash
stripe login
stripe listen --forward-to localhost:8080/api/billing/webhook   # prints whsec_…
```

Put the printed secret in `STRIPE_WEBHOOK_SECRET`, create three Products with monthly and yearly
Prices, and set the six `STRIPE_PRICE_*` variables. See `.env.example`.

---

## 2. Live synchronisation

### Shape

```
Rust+ / A2S  ->  background workers  ->  ILiveSyncPublisher
                                              |
                                    +---------+---------+
                                    |                   |
                              ILiveStateStore     ILiveBroadcaster
                              (Redis snapshot)     (SignalR group)
                                    |                   |
                                    +---------+---------+
                                              |
                                    reconnecting client        connected client
                                    reads the snapshot         receives LiveUpdate
```

### Why a snapshot as well as a push

The broadcast only reaches clients connected at that instant. The snapshot is what a client
reconnecting *between* pushes reads, so it is immediately correct instead of showing stale data
until the next tick — which for the 30s team poll could be half a minute of wrong information on
screen.

State is stored as a Redis **hash**, one field per section (`status`, `team`, `devices`, …),
because producers are independent: the status poller and the team tracker write concurrently, and
read-modify-write on a shared blob would lose whichever landed second.

### Versions and gap detection

`HashIncrement` gives each scope a strictly increasing version. Clients track the last version
they saw per scope; anything that is not exactly one ahead means a message was missed, so the
client re-fetches the whole snapshot rather than rendering state it cannot trust.

### Reconnect

`SubscribeScope` returns the current snapshot **in the same round trip** as the subscribe, so
resuming is one call. SignalR's own `withAutomaticReconnect` handles the transport; on
`onreconnected` the client clears its version map (server versions have moved on) and re-subscribes.

### Retry

Failed publishes go to a bounded, drop-oldest channel drained by `SyncRetryWorker` with 1/2/4/8/16s
backoff, five attempts, then dropped with an error log. Two deliberate choices:

- **Bounded and drop-oldest.** Live state is replaced by the next tick anyway; under sustained
  failure it is better to lose the stalest update than to grow without limit.
- **Store failure and broadcast failure are treated differently.** The store write is what a
  reconnecting client reads, so it must eventually land. The broadcast is an optimisation for
  clients already connected — if it fails but the store succeeded, clients still converge on their
  next reconnect.

Everything degrades to the pre-existing polling intervals, so the dashboard is never blank because
a WebSocket could not be established.

---

## 3. API security

### Authenticated by default

`Program.cs` sets an authorization **fallback policy** requiring an authenticated user. Previously
every controller had to remember its own `[Authorize]`, so a new one that forgot was silently
public. Now anything reachable without a login must say so with `[AllowAnonymous]`.

`EndpointAuthorizationTests` pins the exact anonymous set, so widening it has to be a deliberate
edit to that list.

It also catches a specific trap: a controller with class-level `[AllowAnonymous]` plus an
action-level `[Authorize]`. The authorization middleware finds the `IAllowAnonymous` in the
endpoint metadata and skips the policy entirely, so the action is **public despite looking
protected**. `AuthController` mixes both kinds of endpoint and therefore marks each anonymous
action individually.

### Live scope authorization

SignalR adds a connection to whatever group name it is handed. Before this work, `Subscribe(serverId)`
had no ownership check — any signed-in user could pass another account's server id and receive
their live player positions, team roster and raid alerts.

`ILiveScopeAuthorizer` now gates every join, matching how `ServersController` scopes its reads.
Unknown scope kinds are refused rather than ignored, and a denied subscribe returns the same
message whether the scope does not exist or belongs to someone else — telling them apart would let
a caller enumerate real server ids.

### Entitlement gating

`[RequiresSubscription]` and `[RequiresFeature(...)]` re-resolve entitlement server-side per
request. Status codes are distinct so the UI can respond correctly:

| Code | Meaning | UI response |
|---|---|---|
| 401 | Not signed in | Send to login |
| 402 | Signed in, no active plan | Offer checkout |
| 403 | Subscribed, but tier too low | Offer upgrade |

The plan's server allowance is enforced in `ServersController.Create`, not only in the UI — hiding
the button does nothing to stop a direct POST.

### Secrets

- Rust+ credentials and Rust+ player tokens are stored **AES-GCM encrypted** at rest.
  `Encryption:FieldKey` is now required at startup: the previous conditional registration turned a
  missing key into an obscure DI failure at the moment a user tried to pair.
- No endpoint returns a credential, token hash, password hash, or provider secret. Stripe customer
  and subscription ids are deliberately absent from the API responses too — a client gains nothing
  legitimate from them and they are useful to someone probing our Stripe account.
- The Stripe **publishable** key is not needed anywhere, because we use hosted Checkout rather
  than initialising Stripe.js in the browser.

### Error handling

`ExceptionHandlingMiddleware` returns a consistent JSON envelope. A small set of exception types
carry messages written deliberately for end users and pass through verbatim
(`PaymentProviderException` → 400, `SubscriptionStateException` → 409). Everything else is logged
in full and replaced with a generic sentence, because an arbitrary exception message can contain a
connection string, a file path, or a key.
