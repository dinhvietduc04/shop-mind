"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Banknote, Hourglass } from "lucide-react";
import { BandStrip, Button, EmptyState, ErrorNote, PhotoPlate } from "@/components/ui";
import {
  DEMO_ADDRESS,
  useCart,
  useCreateQuote,
} from "@/lib/api/hooks";
import { priceToBands } from "@/lib/bands";
import { ApiError, formatUsd } from "@/lib/api/types";
import type { QuoteDto } from "@/lib/api/types";
import { FREE_SHIPPING_THRESHOLD } from "@/lib/shipping";

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

export default function QuotePage() {
  const { data: cart } = useCart();
  const createQuote = useCreateQuote();
  const [quote, setQuote] = useState<QuoteDto | null>(null);

  const lines = cart?.items ?? [];
  const subtotal = cart?.subtotal ?? 0;
  const shipping = subtotal === 0 || subtotal >= FREE_SHIPPING_THRESHOLD ? 0 : 15;
  const total = subtotal + shipping;

  return (
    <div className="mx-auto max-w-[680px] px-6 py-6">
      <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">
        The bill
      </h1>

      {lines.length === 0 ? (
        <div className="mt-6">
          <EmptyState title="No quote open">
            <p>
              Add units to a bin, or let the assistant build the cart and prepare
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
      ) : quote ? (
        <article className="mt-6 overflow-hidden rounded-label border-[1.5px] border-ink bg-card">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b-[1.5px] border-ink bg-band-yellow/25 px-5 py-3 font-mono text-[10px] uppercase tracking-[0.2em]">
            <span>quote {quote.order.orderNumber} · stock reserved</span>
            <span className="flex items-center gap-1.5">
              <Hourglass size={12} aria-hidden /> expires in <Countdown to={quote.expiresAt} />
            </span>
          </div>
          <div className="px-5 py-4 text-[13px] leading-relaxed">
            <p>
              Reserved for <strong>{quote.order.shippingAddress.fullName}</strong>,{" "}
              {quote.order.shippingAddress.address}, {quote.order.shippingAddress.city}.
              Prices were recomputed by the API at quote time — this bill cannot
              change. If stock sells out before you pay, the reservation releases.
            </p>
            <div className="mt-4 flex items-center justify-between font-mono text-lg font-bold tabular-nums">
              <span className="flex items-center gap-2">
                TOTAL <BandStrip bands={priceToBands(quote.order.totalAmount)} />
              </span>
              {formatUsd(quote.order.totalAmount)}
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-3 border-t-[1.5px] border-ink bg-kraft/60 px-5 py-4">
            <Link
              href={quote.hostedPayUrl}
              className="inline-flex items-center gap-2 rounded-[4px] border-[1.5px] border-[#1a1a1a] bg-band-yellow px-4 py-2 text-[12px] font-extrabold uppercase tracking-[0.14em] text-[#1a1a1a] hover:brightness-110"
            >
              <Banknote size={13} aria-hidden /> Pay on hosted page
            </Link>
            <span className="font-mono text-[10px] uppercase tracking-[0.12em] text-ink-soft">
              human-only · opens the psp session
            </span>
          </div>
          <p className="px-5 py-3 font-mono text-[10px] uppercase tracking-[0.14em] text-ink-soft">
            dev note: the hosted page is the FakePsp provider; Stripe swaps in behind
            the same IPaymentProvider seam (backend plan B5)
          </p>
        </article>
      ) : (
        <article className="mt-6 overflow-hidden rounded-label border-[1.5px] border-ink bg-card">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b-[1.5px] border-ink bg-kraft/60 px-5 py-3 font-mono text-[10px] uppercase tracking-[0.2em] text-ink-soft">
            <span>cart · {lines.length} lines</span>
            <span>priced by the API · free ship ≥ {formatUsd(FREE_SHIPPING_THRESHOLD)}</span>
          </div>
          <ul>
            {lines.map((l) => (
              <li key={l.productId} className="flex items-center gap-3 border-b border-line/50 px-5 py-3 text-[13px]">
                <div className="w-[64px] shrink-0">
                  <PhotoPlate sku={l.sku} band="grey" name={l.productName} image={l.imageUrl || undefined} compact />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="font-bold uppercase tracking-[0.03em]">{l.productName}</div>
                  <div className="mt-0.5 flex items-center gap-3">
                    <span className="font-mono text-[11px] text-ink-soft">
                      {l.sku} · ×{l.quantity}
                    </span>
                    <BandStrip bands={priceToBands(l.unitPrice)} />
                  </div>
                </div>
                <span className="font-mono tabular-nums">{formatUsd(l.subtotal)}</span>
              </li>
            ))}
          </ul>
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
                createQuote.mutate(DEMO_ADDRESS, {
                  onSuccess: (q) => setQuote(q),
                })
              }
            >
              {createQuote.isPending ? "Reserving stock…" : "Create quote — reserve stock"}
            </Button>
            <span className="font-mono text-[10px] uppercase tracking-[0.12em] text-ink-soft">
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
      )}
    </div>
  );
}
