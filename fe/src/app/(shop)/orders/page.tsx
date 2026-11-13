"use client";

import Link from "next/link";
import { ArrowLeft, Package } from "lucide-react";
import { RequireAuth } from "@/components/guard";
import { StatusChip } from "@/components/order-status";
import { EmptyState, Skeleton } from "@/components/ui";
import { useOrders } from "@/features/orders";
import { formatUsd } from "@/infrastructure/api";

export default function OrdersPage() {
  return (
    <RequireAuth>
      <OrdersInner />
    </RequireAuth>
  );
}

function OrdersInner() {
  const { data: orders, isPending } = useOrders();

  return (
    <main className="mx-auto max-w-[860px] px-6 py-6">
      <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">
        Order ledger
      </h1>
      <p className="mt-2 font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">
        every quote you created lands here: paid, expired or abandoned
      </p>

      {isPending ? (
        <div className="mt-6 space-y-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-20" />
          ))}
        </div>
      ) : !orders?.length ? (
        <div className="mt-6">
          <EmptyState title="No orders yet">
            <p>
              Pay a quote on the hosted page and it appears here with its full
              status timeline.
            </p>
            <Link
              href="/"
              className="mt-4 inline-flex items-center gap-2 text-sm font-bold uppercase tracking-[0.14em] underline underline-offset-4"
            >
              <ArrowLeft size={14} aria-hidden /> Back to the catalog
            </Link>
          </EmptyState>
        </div>
      ) : (
        <ul className="mt-6 divide-y divide-line/60 border-y-[1.5px] border-ink bg-card">
          {orders.map((o) => (
            <li key={o.id}>
              <Link
                href={`/orders/${o.id}`}
                className="flex flex-wrap items-center gap-x-4 gap-y-1 px-5 py-4 hover:bg-kraft/50"
              >
                <Package size={15} aria-hidden className="text-ink-soft" />
                <span className="font-mono text-[13px] font-bold">{o.orderNumber}</span>
                <StatusChip status={o.status} />
                <span className="font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
                  {new Date(o.createdAt).toLocaleString()} · {o.items.length} line
                  {o.items.length === 1 ? "" : "s"}
                </span>
                <span className="ml-auto font-mono text-[15px] font-bold tabular-nums">
                  {formatUsd(o.totalAmount)}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
