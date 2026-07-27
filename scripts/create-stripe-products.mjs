#!/usr/bin/env node
/**
 * Creates the Rustex products and prices in YOUR Stripe account, then prints the environment
 * variables to paste into your host.
 *
 * Run it yourself — your secret key stays on your machine and never needs to be shared:
 *
 *   # macOS / Linux / Git Bash
 *   STRIPE_SECRET_KEY=sk_test_... node scripts/create-stripe-products.mjs
 *
 *   # PowerShell
 *   $env:STRIPE_SECRET_KEY="sk_test_..."; node scripts/create-stripe-products.mjs
 *
 * Safe to re-run: products and prices are looked up by a stable key first, so a second run
 * reports what already exists instead of creating duplicates. Stripe prices are immutable, so
 * changing an amount means creating a new price — the script tells you when that has happened
 * rather than silently leaving the old one in use.
 *
 * No dependencies: talks to Stripe's REST API with fetch.
 */

// Amounts in cents, mirroring PlanCatalog.cs. If you change them here, change them there too —
// the catalog drives entitlement limits, Stripe drives what is actually charged, and a mismatch
// means the customer pays Stripe's number while the app grants the catalog's features.
const PLANS = [
  { tier: 'scout',  name: 'Rustex Scout',  monthly: 499,  yearly: 4990,
    description: 'Raid alarms and live status for a couple of servers.' },
  { tier: 'raider', name: 'Rustex Raider', monthly: 999,  yearly: 9990,
    description: 'Everything in Scout plus smart devices and the market tools.' },
  { tier: 'clan',   name: 'Rustex Clan',   monthly: 1999, yearly: 19990,
    description: 'The full toolkit for an organised group, including analytics.' },
];

const KEY = process.env.STRIPE_SECRET_KEY;
if (!KEY) {
  console.error('STRIPE_SECRET_KEY is not set.\n' +
    'Get a test key from https://dashboard.stripe.com/test/apikeys (it starts sk_test_).');
  process.exit(1);
}

const LIVE = KEY.startsWith('sk_live_');
if (LIVE && process.argv[2] !== '--yes-live') {
  // A live key creates products customers can actually be charged against. Requiring an explicit
  // flag makes that a decision rather than an accident from a stale shell variable.
  console.error('That is a LIVE key. Re-run with --yes-live if you really mean to touch live mode.');
  process.exit(1);
}

/** Stripe's API is form-encoded, not JSON. */
function form(obj, prefix = '', out = new URLSearchParams()) {
  for (const [k, v] of Object.entries(obj)) {
    if (v === undefined || v === null) continue;
    const key = prefix ? `${prefix}[${k}]` : k;
    if (typeof v === 'object' && !Array.isArray(v)) form(v, key, out);
    else out.append(key, String(v));
  }
  return out;
}

async function stripe(method, path, body) {
  const res = await fetch(`https://api.stripe.com/v1${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${KEY}`,
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body: body ? form(body) : undefined,
  });
  const json = await res.json();
  if (!res.ok) throw new Error(`${path}: ${json.error?.message ?? res.status}`);
  return json;
}

/** Find a product we created earlier, by the tier stamped into its metadata. */
async function findProduct(tier) {
  const r = await stripe('GET', `/products/search?query=${encodeURIComponent(`metadata['rustex_tier']:'${tier}'`)}&limit=1`);
  return r.data?.[0] ?? null;
}

async function ensureProduct(plan) {
  const existing = await findProduct(plan.tier);
  if (existing) return { product: existing, created: false };
  const product = await stripe('POST', '/products', {
    name: plan.name,
    description: plan.description,
    metadata: { rustex_tier: plan.tier },
  });
  return { product, created: true };
}

async function ensurePrice(product, tier, interval, amount) {
  // lookup_key is unique per account, which makes it a natural idempotency handle.
  const lookupKey = `rustex_${tier}_${interval}`;
  const found = await stripe('GET', `/prices?lookup_keys[]=${lookupKey}&limit=1&active=true`);
  const existing = found.data?.[0];

  if (existing) {
    if (existing.unit_amount === amount) return { price: existing, created: false, changed: false };
    // Prices are immutable in Stripe. Retire the old one and make a new one at the new amount,
    // moving the lookup key across so config keeps pointing at the right thing.
    await stripe('POST', `/prices/${existing.id}`, { active: false, lookup_key: '' });
    const price = await stripe('POST', '/prices', {
      product: product.id, currency: 'usd', unit_amount: amount,
      recurring: { interval }, lookup_key: lookupKey,
      metadata: { rustex_tier: tier },
    });
    return { price, created: true, changed: true, oldAmount: existing.unit_amount };
  }

  const price = await stripe('POST', '/prices', {
    product: product.id, currency: 'usd', unit_amount: amount,
    recurring: { interval }, lookup_key: lookupKey,
    metadata: { rustex_tier: tier },
  });
  return { price, created: true, changed: false };
}

const money = c => `$${(c / 100).toFixed(2)}`;

(async () => {
  console.log(`\nStripe ${LIVE ? 'LIVE' : 'TEST'} mode\n${'='.repeat(58)}`);
  const env = [];

  for (const plan of PLANS) {
    const { product, created } = await ensureProduct(plan);
    console.log(`\n${plan.name}  ${created ? '(created)' : '(already existed)'}`);
    console.log(`  ${product.id}`);

    for (const [interval, amount, suffix] of [
      ['month', plan.monthly, 'Monthly'],
      ['year', plan.yearly, 'Yearly'],
    ]) {
      const r = await ensurePrice(product, plan.tier, interval, amount);
      const note = r.changed ? `(replaced ${money(r.oldAmount)} -> ${money(amount)})`
        : r.created ? '(created)' : '(already existed)';
      console.log(`  ${suffix.padEnd(8)} ${money(amount).padStart(8)} / ${interval}  ${r.price.id}  ${note}`);
      env.push(`Stripe__Prices__${plan.tier}__${suffix}=${r.price.id}`);
    }
  }

  console.log(`\n${'='.repeat(58)}`);
  console.log('Set these on your API host:\n');
  console.log(env.join('\n'));
  console.log(`\nStripe__SecretKey=<the key you just used>`);
  console.log('Stripe__WebhookSecret=<from the webhook endpoint, see below>');
  console.log(`
Then add the webhook endpoint:
  https://dashboard.stripe.com/${LIVE ? '' : 'test/'}webhooks -> Add endpoint
  URL:    https://<your-api-host>/api/billing/webhook
  Events: checkout.session.completed
          customer.subscription.created
          customer.subscription.updated
          customer.subscription.deleted
          invoice.paid
          invoice.payment_failed
          payment_method.attached
  Copy the signing secret (whsec_...) into Stripe__WebhookSecret.
`);
})().catch(err => {
  console.error('\nFailed:', err.message);
  process.exit(1);
});
