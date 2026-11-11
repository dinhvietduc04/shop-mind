"use client";

import { useParams } from "next/navigation";
import Link from "next/link";
import { BadgeCheck, Hourglass, Lock, ShieldAlert, X } from "lucide-react";
import { Button, ErrorNote, Skeleton } from "@/components/ui";
import { useCancelPayment, useConfirmPayment, useQuote } from "@/lib/api/hooks";
import { quoteOutcome } from "@/lib/quotes/intake";
import { formatUsd } from "@/lib/api/types";

/**
 * FakePsp hosted payment page. Intentionally chromeless and outside the shop
 * shell. This is where the Assist level ends. The human arrives here from the
 * bill, reviews, and pays. The assistant never does.
 * A real Stripe Checkout session URL would replace this route; the shop-side
 * contract (hostedPayUrl + webhook) is identical.
 */
export default function PspPage() {
  const { token } = useParams<{ token: string }>();
  const { data: quote, isPending, isError } = useQuote(token);
  const confirm = useConfirmPayment(token);
  const cancel = useCancelPayment(token);

  /* one decode. Expired-versus-declined comes from payment.outcome */
  const outcome = quote ? quoteOutcome(quote.order) : null;
  const expired = outcome === "expired";
  const paid = outcome === "paid" || outcome === "fulfilled" || (confirm.data ? quoteOutcome(confirm.data.order) === "paid" : false);
  const view = paid ? confirm.data?.order ?? quote?.order : quote?.order;

  return (
    <main className="flex min-h-screen items-center justify-center bg-kraft p-6">
      <div className="w-full max-w-[520px] overflow-hidden rounded-label border-[1.5px] border-ink bg-card">
        <div className="flex items-center justify-between border-b-[1.5px] border-ink bg-ink px-5 py-3 text-paper">
          <span className="flex items-center gap-2 font-mono text-[11px] uppercase tracking-[0.24em]">
            <Lock size={13} aria-hidden /> FakePsp · hosted session
          </span>
          <span className="font-mono text-[11px] uppercase tracking-[0.2em] opacity-70">
            demo provider
          </span>
        </div>

        {isPending ? (
          <div className="space-y-3 p-5">
            <Skeleton className="h-6 w-2/3" />
            <Skeleton className="h-40" />
          </div>
        ) : isError || !view ? (
          <div className="p-5">
            <ErrorNote>
              <strong>Session not found or expired.</strong> Quotes hold stock for
              30 minutes. Start a new one from the bill page.
            </ErrorNote>
          </div>
        ) : (
          <div className="p-5">
            <p className="font-mono text-[11px] uppercase tracking-[0.22em] text-ink-soft">
              merchant · shopmind · order {view.orderNumber}
            </p>
            <h1 className="mt-1 text-xl font-extrabold uppercase">
              {formatUsd(view.totalAmount)} due
            </h1>
            {outcome === "open" && (
              <p className="mt-1 flex items-center gap-1.5 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
                <Hourglass size={12} aria-hidden /> reservation held until{" "}
                {new Date(quote.expiresAt).toLocaleTimeString()}
              </p>
            )}

            <ul className="my-4 border-y border-line/50">
              {view.items.map((i) => (
                <li key={i.productId} className="flex justify-between border-b border-line/40 py-2 text-[13px] last:border-b-0">
                  <span>{i.productName} ×{i.quantity}</span>
                  <span className="font-mono tabular-nums">{formatUsd(i.subtotal)}</span>
                </li>
              ))}
              <li className="flex justify-between py-2 text-[13px]">
                <span className="text-ink-soft">Shipping</span>
                <span className="font-mono tabular-nums">{view.shippingFee === 0 ? "free" : formatUsd(view.shippingFee)}</span>
              </li>
            </ul>

            {paid ? (
              <div className="flex items-center gap-3 rounded-[4px] border-[1.5px] border-band-green bg-band-green/10 p-4">
                <BadgeCheck size={20} aria-hidden className="text-band-green" />
                <div className="text-[13px]">
                  <strong>Payment succeeded.</strong> Stock committed, cart cleared,
                  order {view.orderNumber} is <em>Paid</em>. The webhook consumer in the
                  real build makes this transition once (idempotent).
                  <div className="mt-1 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
                    txn {view.payment?.transactionId}
                  </div>
                  <Link
                    href={`/checkout/success?session=${token}`}
                    className="mt-2 inline-block font-mono text-[11px] font-bold uppercase tracking-[0.14em] underline underline-offset-4"
                  >
                    Return to the shop
                  </Link>
                </div>
              </div>
            ) : expired ? (
              <div className="flex items-center gap-3 rounded-[4px] border-[1.5px] border-band-red bg-band-red/10 p-4 text-[13px]">
                <ShieldAlert size={20} aria-hidden className="text-band-red" />
                <div><strong>Reservation released.</strong> The quote expired. Stock
                  returned to the bins. Create a new quote from the bill page.</div>
              </div>
            ) : outcome === "abandoned" ? (
              <div className="rounded-[4px] border-[1.5px] border-line p-4 text-[13px]">
                <strong>Quote abandoned.</strong> Reservation released. No charge.
                <div className="mt-2">
                  <Link
                    href={`/checkout/cancel?session=${token}`}
                    className="font-mono text-[11px] font-bold uppercase tracking-[0.14em] underline underline-offset-4"
                  >
                    Return to the shop
                  </Link>
                </div>
              </div>
            ) : (
              <div className="flex flex-wrap gap-3">
                <Button
                  variant="human"
                  disabled={confirm.isPending}
                  onClick={() => confirm.mutate()}
                >
                  {confirm.isPending ? "Contacting psp…" : `Pay ${formatUsd(view.totalAmount)}`}
                </Button>
                <Button
                  variant="ghost"
                  disabled={cancel.isPending}
                  onClick={() => cancel.mutate()}
                >
                  <X size={13} aria-hidden /> Abandon quote
                </Button>
              </div>
            )}

            {confirm.error && (
              <div className="mt-4">
                <ErrorNote><strong>Card declined.</strong> Nothing was charged; the reservation still holds until expiry. Try again or abandon.</ErrorNote>
              </div>
            )}

            <p className="mt-4 font-mono text-[11px] uppercase leading-relaxed tracking-[0.12em] text-ink-soft">
              no card form. This mock confirms instantly (paymentfail mode simulates a
              decline). Real stripe checkout collects the card on stripe&apos;s page.
              Card data never touches ShopMind (Assist level).
            </p>
          </div>
        )}
      </div>
    </main>
  );
}
