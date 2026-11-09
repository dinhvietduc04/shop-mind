"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Banknote, Ban, Hourglass, Minus, Plus, X } from "lucide-react";
import { RequireAuth } from "@/components/guard";
import { Button, EmptyState, ErrorNote, MagnitudeTick, PhotoPlate } from "@/components/ui";
import { StatusChip } from "@/components/order-status";
import {
  useAccountAddress,
  useCancelPayment,
  useCreateQuote,
  useCart,
  useRemoveCartItem,
  useUpdateCartItem,
} from "@/lib/api/hooks";
import {
  DEMO_ADDRESS,
  holdOpenQuote,
  quoteOutcome,
  releaseOpenQuote,
  useHeldQuote,
} from "@/lib/quotes/intake";
import { ApiError, formatUsd } from "@/lib/api/types";
import { FREE_SHIPPING_THRESHOLD, shippingFeeFor } from "@/lib/shipping";

/* The session pointer, discovery reconciliation, and outcome decode all live
   in lib/quotes/intake (C1) — this page renders what that module holds. */

function Countdown({ to }: { to: string }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);
  const left = Math.max(0, new Date(to).getTime() - now);
  const m = Math.floor(left / 60000);
  const s = Math.floor((left % 60000) / 1000);
  return (
    <span className="font-mono tabular-nums">
      {left === 0 ? "EXPIRED" : `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`}
    </span>
  );
}

function QuotePageInner() {
  const { data: accountAddr } = useAccountAddress();
  const shipTo = accountAddr?.address ?? DEMO_ADDRESS;
  const { data: cart } = useCart();
  const createQuote = useCreateQuote();
  const update = useUpdateCartItem();
  const remove = useRemoveCartItem();
  /* pointer + discovery + reconciliation in one hook (intake): this tab's
     held session, or the live one the server/assistant points at. */
  const { token, quote, isPending: quotePending } = useHeldQuote();
  const abandon = useCancelPayment(token ?? "");

  const lines = cart?.items ?? [];
  const subtotal = cart?.subtotal ?? 0;
  const shipping = shippingFeeFor(subtotal); // the ONE rule, same fn the mock server prices with
  const total = subtotal + shipping;
  const shipLeft = Math.max(0, FREE_SHIPPING_THRESHOLD - subtotal);

  const cartError =
    (update.error ?? remove.error) instanceof ApiError
      ? ((update.error ?? remove.error) as ApiError).message
      : null;

  /* ---------------------- quote session: server-driven ---------------------- */
  if (token) {
    if (quotePending) {
      return (
        <main className="mx-auto max-w-[680px] px-6 py-6">
          <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">The bill</h1>
          <div className="mt-6 space-y-3">
            <div className="h-8 w-2/3 animate-pulse rounded bg-kraft" />
            <div className="h-40 animate-pulse rounded bg-kraft" />
          </div>
        </main>
      );
    }

    if (!quote) {
      // token in this tab but the server has no such session (reset, expiry GC…)
      return (
        <main className="mx-auto max-w-[680px] px-6 py-6">
          <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">The bill</h1>
          <div className="mt-6">
            <ErrorNote>
              <strong>That session no longer exists on the server.</strong> The demo
              state may have been reset. Nothing was charged.
              <div className="mt-3">
                <Button variant="ghost" onClick={releaseOpenQuote}>
                  <X size={13} aria-hidden /> Discard the dead session
                </Button>
              </div>
            </ErrorNote>
          </div>
        </main>
      );
    }

    const o = quote.order;
    /* the ONE decode (intake) — reads status + payment.outcome, never
       transactionId archaeology */
    const outcome = quoteOutcome(o);

    if (outcome === "paid" || outcome === "fulfilled") {
      return (
        <TerminalQuote title="Paid — thank you" tone="green">
          <p>
            Order <strong>{o.orderNumber}</strong> settled at the hosted page.
            Stock committed, cart cleared — the ledger has the full trail.
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            <Link href={`/orders/${o.id}`}>
              <Button variant="ghost">Order ledger →</Button>
            </Link>
            <Link href="/">
              <Button variant="ghost" onClick={releaseOpenQuote}>
                New shopping session
              </Button>
            </Link>
          </div>
        </TerminalQuote>
      );
    }

    if (outcome === "expired" || outcome === "declined") {
      const expired = outcome === "expired";
      return (
        <TerminalQuote title={expired ? "Quote expired — stock released" : "Card declined"} tone="red">
          <p>
            {expired
              ? "The 30-minute hold ran out and every reserved unit went back to the bins. Nobody was charged. Your cart survived — create a fresh quote and pay faster next time."
              : "The hosted page declined the card. Nothing was charged and the reservation was released."}
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            <Button variant="ghost" onClick={releaseOpenQuote}>
              Back to the cart — re-quote
            </Button>
            <Link href="/orders">
              <Button variant="ghost">See the ledger entry</Button>
            </Link>
          </div>
        </TerminalQuote>
      );
    }

    if (outcome === "abandoned") {
      return (
        <TerminalQuote title="Quote abandoned" tone="grey">
          <p>
            Reservation released, no charge, cart intact.
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            <Button variant="ghost" onClick={releaseOpenQuote}>
              Back to the cart — re-quote
            </Button>
          </div>
        </TerminalQuote>
      );
    }

    // Pending — the live bill
    return (
      <main className="mx-auto max-w-[680px] px-6 py-6">
        <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">
          The bill <StatusChip status={o.status} />
        </h1>
        <article className="mt-6 overflow-hidden rounded-label border-[1.5px] border-ink bg-card">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b-[1.5px] border-ink bg-band-yellow/25 px-5 py-3 font-mono text-[11px] uppercase tracking-[0.2em]">
            <span>quote {o.orderNumber} · stock reserved</span>
            <span className="flex items-center gap-1.5">
              <Hourglass size={12} aria-hidden /> expires in <Countdown to={quote.expiresAt} />
            </span>
          </div>

          <ul className="px-5 py-2">
            {o.items.map((i) => (
              <li key={i.productId} className="flex items-center justify-between border-b border-line/40 py-2 text-[13px] last:border-b-0">
                <span>
                  {i.productName} <span className="font-mono text-[11px] text-ink-soft">×{i.quantity}</span>
                </span>
                <span className="font-mono tabular-nums">{formatUsd(i.subtotal)}</span>
              </li>
            ))}
          </ul>

          <div className="px-5 py-4 text-[13px] leading-relaxed">
            <p>
              Reserved for <strong>{o.shippingAddress.fullName}</strong>,{" "}
              {o.shippingAddress.address}, {o.shippingAddress.city}. Prices were
              recomputed by the API at quote time — this bill cannot change. If
              stock sells out before you pay, the reservation releases.
            </p>
            <div className="mt-4 flex items-center justify-between font-mono text-lg font-bold tabular-nums">
              <span className="flex items-center gap-2">
                TOTAL <MagnitudeTick value={o.totalAmount} />
              </span>
              {formatUsd(o.totalAmount)}
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-3 border-t-[1.5px] border-ink bg-kraft/60 px-5 py-4">
            <Link
              href={quote.hostedPayUrl}
              className="inline-flex items-center gap-2 rounded-[4px] border-[1.5px] border-[#1a1a1a] bg-band-yellow px-4 py-2 text-[12px] font-extrabold uppercase tracking-[0.14em] text-[#1a1a1a] hover:brightness-110"
            >
              <Banknote size={13} aria-hidden /> Pay on hosted page
            </Link>
            <Button
              variant="ghost"
              disabled={abandon.isPending}
              onClick={() =>
                abandon.mutate(undefined, {
                  onSuccess: () => {
                    // go read the released truth from the server, then park the tab
                    setTimeout(releaseOpenQuote, 4000);
                  },
                })
              }
            >
              <Ban size={13} aria-hidden /> {abandon.isPending ? "Releasing…" : "Abandon quote"}
            </Button>
            <span className="font-mono text-[11px] uppercase tracking-[0.12em] text-ink-soft">
              pay is human-only · abandon releases the hold, keeps your cart
            </span>
          </div>
          {abandon.error && (
            <div className="border-t border-line/50 px-5 py-3">
              <ErrorNote>Could not release the hold — the quote stays pending.</ErrorNote>
            </div>
          )}
        </article>
      </main>
    );
  }

  /* ------------------------------ cart is empty ----------------------------- */
  if (lines.length === 0) {
    return (
      <main className="mx-auto max-w-[680px] px-6 py-6">
        <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">The bill</h1>
        <div className="mt-6">
          <EmptyState title="No quote open">
            <p>
              Add units to the cart, or let the assistant build it and prepare
              a quote for you to review.
            </p>
            <Link
              href="/"
              className="mt-4 inline-flex items-center gap-2 text-sm font-bold uppercase tracking-[0.14em] underline underline-offset-4"
            >
              <ArrowLeft size={14} aria-hidden /> Back to the catalog
            </Link>
          </EmptyState>
        </div>
      </main>
    );
  }

  /* ------------------------------- the cart ------------------------------- */
  return (
    <main className="mx-auto max-w-[680px] px-6 py-6">
      <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">The bill</h1>

      <article className="mt-6 overflow-hidden rounded-label border-[1.5px] border-ink bg-card">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b-[1.5px] border-ink bg-kraft/60 px-5 py-3 font-mono text-[11px] uppercase tracking-[0.2em] text-ink-soft">
          <span>cart · {lines.length} lines</span>
          <span>priced by the API · free ship ≥ {formatUsd(FREE_SHIPPING_THRESHOLD)}</span>
        </div>

        {/* free-shipping progress — honest bar, band green only when filled */}
        <div className="px-5 pt-4">
          <div
            role="progressbar"
            aria-valuenow={Math.min(subtotal, FREE_SHIPPING_THRESHOLD)}
            aria-valuemin={0}
            aria-valuemax={FREE_SHIPPING_THRESHOLD}
            aria-label="Free shipping progress"
            className="h-2.5 overflow-hidden rounded-[3px] border-[1.5px] border-ink bg-paper"
          >
            <div
              className={
                subtotal >= FREE_SHIPPING_THRESHOLD
                  ? "h-full bg-band-green transition-[width] duration-300"
                  : "h-full bg-band-yellow transition-[width] duration-300"
              }
              style={{ width: `${Math.min(100, (subtotal / FREE_SHIPPING_THRESHOLD) * 100)}%` }}
            />
          </div>
          <p className="mt-2 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
            {subtotal >= FREE_SHIPPING_THRESHOLD
              ? "free shipping unlocked"
              : `add ${formatUsd(shipLeft)} more for free shipping`}
          </p>
        </div>

        <ul className="mt-2">
          {lines.map((l) => (
            <li key={l.productId} className="flex items-center gap-3 border-b border-line/50 px-5 py-3 text-[13px]">
              <div className="w-[64px] shrink-0">
                <PhotoPlate sku={l.sku} band="grey" name={l.productName} image={l.imageUrl || undefined} compact />
              </div>
              <div className="min-w-0 flex-1">
                <div className="font-bold uppercase tracking-[0.03em]">
                  <Link href={`/product/${l.productId}`} className="hover:underline">
                    {l.productName}
                  </Link>
                </div>
                <div className="mt-0.5 flex items-center gap-3">
                  <span className="font-mono text-[11px] text-ink-soft">{l.sku}</span>
                  <span className="inline-flex items-center gap-1.5">
                    <MagnitudeTick value={l.unitPrice} className="h-3.5" />
                    <span className="font-mono text-[11px] uppercase tracking-[0.12em] text-ink-soft">
                      {formatUsd(l.unitPrice)} ea
                    </span>
                  </span>
                </div>
                <div className="mt-2 flex items-center gap-1.5">
                  <button
                    aria-label={`Decrease ${l.productName}`}
                    disabled={update.isPending || l.quantity <= 1}
                    onClick={() => update.mutate({ productId: l.productId, quantity: l.quantity - 1 })}
                    className="rounded-[3px] border-[1.5px] border-line p-1 hover:border-ink disabled:opacity-40"
                  >
                    <Minus size={11} aria-hidden />
                  </button>
                  <span className="w-7 text-center font-mono text-[12px] tabular-nums">{l.quantity}</span>
                  <button
                    aria-label={`Increase ${l.productName}`}
                    disabled={update.isPending}
                    onClick={() => update.mutate({ productId: l.productId, quantity: l.quantity + 1 })}
                    className="rounded-[3px] border-[1.5px] border-line p-1 hover:border-ink disabled:opacity-40"
                  >
                    <Plus size={11} aria-hidden />
                  </button>
                  <button
                    aria-label={`Remove ${l.productName}`}
                    disabled={remove.isPending}
                    onClick={() => remove.mutate(l.productId)}
                    className="ml-2 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft underline decoration-line underline-offset-4 hover:text-band-red"
                  >
                    <X size={10} aria-hidden className="mr-1 inline" />
                    remove
                  </button>
                </div>
              </div>
              <span className="font-mono tabular-nums">{formatUsd(l.subtotal)}</span>
            </li>
          ))}
        </ul>

        {cartError && (
          <div className="border-t border-line/50 px-5 py-3">
            <ErrorNote>{cartError}</ErrorNote>
          </div>
        )}

        <dl className="px-5 py-3 text-[13px]">
          <div className="flex justify-between py-1"><dt className="text-ink-soft">Subtotal</dt><dd className="font-mono tabular-nums">{formatUsd(subtotal)}</dd></div>
          <div className="flex justify-between py-1"><dt className="text-ink-soft">Shipping</dt><dd className="font-mono tabular-nums">{shipping === 0 ? "free" : formatUsd(shipping)}</dd></div>
          <div className="mt-1 flex justify-between border-t-[1.5px] border-ink py-2 font-extrabold uppercase tracking-[0.14em]">
            <dt>Total</dt><dd className="font-mono text-lg tabular-nums">{formatUsd(total)}</dd>
          </div>
        </dl>

        <div className="flex flex-wrap items-center gap-3 border-t-[1.5px] border-ink px-5 py-4">
          <Button
            variant="add"
            disabled={createQuote.isPending}
            onClick={() =>
              createQuote.mutate(shipTo, {
                // sessionToken arrives as a DTO field — no URL archaeology (C1)
                onSuccess: (q) => holdOpenQuote(q.sessionToken),
              })
            }
          >
            {createQuote.isPending ? "Reserving stock…" : "Create quote — reserve stock"}
          </Button>
          <span className="font-mono text-[11px] uppercase tracking-[0.12em] text-ink-soft">
            assistant may press this · yellow may not
          </span>
        </div>
        {createQuote.error && (
          <div className="px-5 pb-5">
            <ErrorNote>
              {createQuote.error instanceof ApiError
                ? createQuote.error.message
                : "Could not create the quote."}
            </ErrorNote>
          </div>
        )}
      </article>
    </main>
  );
}

function TerminalQuote({
  title,
  tone,
  children,
}: {
  title: string;
  tone: "green" | "red" | "grey";
  children: React.ReactNode;
}) {
  const bar = { green: "bg-band-green", red: "bg-band-red", grey: "bg-band-grey" }[tone];
  return (
    <main className="mx-auto max-w-[680px] px-6 py-6">
      <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">{title}</h1>
      <article className="mt-6 overflow-hidden rounded-label border-[1.5px] border-ink bg-card">
        <div className={`h-1.5 ${bar}`} aria-hidden />
        <div className="px-5 py-4 text-[14px] leading-relaxed">{children}</div>
      </article>
    </main>
  );
}

export default function QuotePage() {
  return (
    <RequireAuth>
      <QuotePageInner />
    </RequireAuth>
  );
}
