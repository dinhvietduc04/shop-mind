"use client";

import { useParams } from "next/navigation";
import { BadgeCheck, Hourglass, Lock, ShieldAlert, X } from "lucide-react";
import { Button, ErrorNote, Skeleton } from "@/components/ui";
import { useCancelPayment, useConfirmPayment, useQuote } from "@/lib/api/hooks";
import { formatUsd } from "@/lib/api/types";

/**
 * FakePsp hosted payment page. Intentionally chromeless and outside the shop
 * shell: this is the boundary from the Assist level of the plan — the human
 * arrives here from the bill, reviews, and pays. The assistant never does.
 * A real Stripe Checkout session URL would replace this route; the shop-side
 * contract (hostedPayUrl + webhook) is identical.
 */
export default function PspPage() {
  const { token } = useParams<{ token: string }>();
  const { data: quote, isPending, isError } = useQuote(token);
  const confirm = useConfirmPayment(token);
  const cancel = useCancelPayment(token);

  const status = quote?.order.status;
  const expired = status === "PaymentFailed" && !quote?.order.payment?.transactionId;
  const paid = status === "Paid" || confirm.data?.order.status === "Paid";
  const view = paid ? confirm.data?.order ?? quote?.order : quote?.order;

  return (
    <main className="flex min-h-screen items-center justify-center bg-kraft p-6">
      <div className="w-full max-w-[520px] overflow-hidden rounded-label border-[1.5px] border-ink bg-card">
        <div className="flex items-center justify-between border-b-[1.5px] border-ink bg-ink px-5 py-3 text-paper">
          <span className="flex items-center gap-2 font-mono text-[11px] uppercase tracking-[0.24em]">
            <Lock size={13} aria-hidden /> FakePsp · hosted session
          </span>
          <span className="font-mono text-[10px] uppercase tracking-[0.2em] opacity-70">
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
              30 minutes — start a new one from the bill page.
            </ErrorNote>
          </div>
        ) : (
          <div className="p-5">
            <p className="font-mono text-[10px] uppercase tracking-[0.22em] text-ink-soft">
              merchant · shopmind · order {view.orderNumber}
            </p>
            <h1 className="mt-1 text-xl font-extrabold uppercase">
              {formatUsd(view.totalAmount)} due
            </h1>
            {status === "Pending" && (
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
                  <div className="mt-1 font-mono text-[10px] uppercase tracking-[0.14em] text-ink-soft">
                    txn {view.payment?.transactionId}
                  </div>
                </div>
              </div>
            ) : expired ? (
              <div className="flex items-center gap-3 rounded-[4px] border-[1.5px] border-band-red bg-band-red/10 p-4 text-[13px]">
                <ShieldAlert size={20} aria-hidden className="text-band-red" />
                <div><strong>Reservation released.</strong> The quote expired — stock
                  returned to the bins. Create a new quote from the bill page.</div>
              </div>
            ) : status === "Cancelled" ? (
              <div className="rounded-[4px] border-[1.5px] border-line p-4 text-[13px]">
                <strong>Quote abandoned.</strong> Reservation released. No charge.
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
                <ErrorNote><strong>Card declined.</strong> Nothing was charged; the reservation still holds until expiry — try again or abandon.</ErrorNote>
              </div>
            )}

            <p className="mt-4 font-mono text-[10px] uppercase leading-relaxed tracking-[0.12em] text-ink-soft">
              no card form: this mock confirms instantly (paymentfail mode simulates a
              decline). real stripe checkout collects the card on stripe&apos;s page —
              card data never touches ShopMind (Assist level).
            </p>
          </div>
        )}
      </div>
    </main>
  );
}
