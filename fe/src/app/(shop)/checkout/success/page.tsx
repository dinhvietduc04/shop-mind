"use client";

import { Suspense } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { BadgeCheck } from "lucide-react";
import { StatusChip, StatusTimeline } from "@/components/order-status";
import { Button, EmptyState, Skeleton } from "@/components/ui";
import { useQuote } from "@/lib/api/hooks";
import { formatUsd } from "@/lib/api/types";

function SuccessView() {
  const token = useSearchParams().get("session");
  const { data: quote, isPending } = useQuote(token ?? "");
  const order = quote?.order;

  if (!token) {
    return (
      <EmptyState title="No payment session">
        <p>This page only exists as the return URL after a hosted payment.</p>
        <Link href="/" className="mt-4 inline-block">
          <Button variant="ghost">To the catalog</Button>
        </Link>
      </EmptyState>
    );
  }

  if (isPending) return <Skeleton className="h-64" />;

  return (
    <div>
      <div className="flex items-center gap-3">
        <BadgeCheck size={26} aria-hidden className="text-band-green" />
        <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">
          {order?.status === "Paid" ? "Payment received" : "Session check…"}
        </h1>
      </div>

      {order && order.status !== "Pending" && (
        <article className="mt-6 overflow-hidden rounded-label border-[1.5px] border-ink bg-card">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b-[1.5px] border-ink bg-kraft/60 px-5 py-3">
            <span className="font-mono text-[12px] font-bold">{order.orderNumber}</span>
            <StatusChip status={order.status} />
          </div>
          <div className="px-5 py-4">
            <StatusTimeline status={order.status} />
            <p className="mt-4 font-mono text-lg font-bold tabular-nums">
              {formatUsd(order.totalAmount)} paid
              {order.payment?.transactionId && (
                <span className="ml-3 align-middle font-mono text-[11px] font-normal uppercase tracking-[0.14em] text-ink-soft">
                  txn {order.payment.transactionId}
                </span>
              )}
            </p>
            <p className="mt-2 text-[13px] leading-relaxed text-ink-soft">
              Stock committed, cart cleared. In the real build, this page and the
              ledger entry are the confirmation e-mail.
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              <Link href={`/orders/${order.id}`}>
                <Button variant="ghost">View in the order ledger</Button>
              </Link>
              <Link href="/">
                <Button variant="ghost">Keep shopping</Button>
              </Link>
            </div>
          </div>
        </article>
      )}

      {order?.status === "Pending" && (
        <p className="mt-4 text-[13px] text-ink-soft">
          This session has not been paid. The bill still holds.{" "}
          <Link href="/quote" className="underline underline-offset-4">
            Back to the bill
          </Link>
          .
        </p>
      )}
    </div>
  );
}

export default function CheckoutSuccessPage() {
  return (
    <main className="mx-auto max-w-[680px] px-6 py-10">
      <Suspense fallback={<Skeleton className="h-64" />}>
        <SuccessView />
      </Suspense>
    </main>
  );
}
