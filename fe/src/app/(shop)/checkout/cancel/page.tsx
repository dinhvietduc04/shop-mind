"use client";

import { Suspense } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { X } from "lucide-react";
import { Button, EmptyState, Skeleton } from "@/components/ui";
import { useCart, useQuote } from "@/lib/api/hooks";
import { formatUsd } from "@/lib/api/types";

function CancelView() {
  const token = useSearchParams().get("session");
  const { data: quote, isPending } = useQuote(token ?? "");
  const { data: cart } = useCart();

  if (!token) {
    return (
      <EmptyState title="No payment session">
        <p>This page only exists as the cancel-return URL from the hosted payment.</p>
        <Link href="/" className="mt-4 inline-block">
          <Button variant="ghost">To the catalog</Button>
        </Link>
      </EmptyState>
    );
  }

  if (isPending) return <Skeleton className="h-64" />;

  const abandoned = quote?.order.status === "Cancelled";
  const paid = quote?.order.status === "Paid";

  return (
    <div>
      <div className="flex items-center gap-3">
        <X size={24} aria-hidden className="text-ink-soft" />
        <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">
          Payment not taken
        </h1>
      </div>
      <p className="mt-3 text-[14px] leading-relaxed">
        {paid
          ? `Order ${quote.order.orderNumber} is actually paid. You may have hit back after returning. Nothing further is due.`
          : abandoned
            ? "You abandoned the quote. The reservation is released back to the bins and no charge exists. Your cart still holds the items. You can create a new quote any time."
            : `Session ${token.slice(0, 8)} is still pending. Nothing was charged; stock stays reserved until ${quote ? new Date(quote.expiresAt).toLocaleTimeString() : "expiry"}.`}
      </p>
      {!paid && (
        <p className="mt-2 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
          cart: {cart?.totalQuantity ?? 0} items · {formatUsd(cart?.subtotal ?? 0)}
        </p>
      )}
      <div className="mt-5 flex flex-wrap gap-2">
        {!paid && (
          <Link href="/quote">
            <Button variant="ghost">Back to the bill</Button>
          </Link>
        )}
        <Link href="/">
          <Button variant="ghost">To the catalog</Button>
        </Link>
      </div>
    </div>
  );
}

export default function CheckoutCancelPage() {
  return (
    <main className="mx-auto max-w-[680px] px-6 py-10">
      <Suspense fallback={<Skeleton className="h-64" />}>
        <CancelView />
      </Suspense>
    </main>
  );
}
