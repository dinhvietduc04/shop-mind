"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, Ban, RotateCcw } from "lucide-react";
import { RequireAuth } from "@/components/guard";
import { StatusChip, StatusTimeline } from "@/components/order-status";
import { Button, ErrorNote, MagnitudeTick, Skeleton } from "@/components/ui";
import { useCancelOrder, useOrder } from "@/features/orders";
import { ApiError, formatUsd } from "@/infrastructure/api";

export default function OrderDetailPage() {
  return (
    <RequireAuth>
      <OrderDetailInner />
    </RequireAuth>
  );
}

function OrderDetailInner() {
  const { id } = useParams<{ id: string }>();
  const { data: order, isPending, isError, refetch } = useOrder(id);
  const cancel = useCancelOrder();

  if (isPending) return <Skeleton className="mx-auto mt-6 h-96 max-w-[860px]" />;

  if (isError || !order) {
    return (
      <main className="mx-auto max-w-[720px] px-6 py-10">
        <ErrorNote>
          <strong>No order with that code on your account.</strong> Orders are
          scoped to the session that created them.
          <div className="mt-3 flex gap-2">
            <Button variant="ghost" onClick={() => refetch()}>
              <RotateCcw size={13} aria-hidden /> Re-read ledger
            </Button>
            <Link href="/orders">
              <Button variant="ghost">
                <ArrowLeft size={13} aria-hidden /> All orders
              </Button>
            </Link>
          </div>
        </ErrorNote>
      </main>
    );
  }

  const cancelable = order.status === "Pending";
  const cancelError =
    cancel.error instanceof ApiError ? cancel.error.message : null;

  return (
    <main className="mx-auto max-w-[860px] px-6 py-6">
      <nav className="font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">
        <Link href="/orders" className="hover:text-ink hover:underline">
          Back to the order ledger
        </Link>
      </nav>

      <div className="mt-4 flex flex-wrap items-center gap-4">
        <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">
          Order {order.orderNumber}
        </h1>
        <StatusChip status={order.status} />
      </div>

      <article className="mt-6 overflow-hidden rounded-label border-[1.5px] border-ink bg-card">
        <div className="border-b-[1.5px] border-line px-5 py-4">
          <h2 className="mb-3 font-mono text-[11px] uppercase tracking-[0.22em] text-ink-soft">
            Fulfillment path
          </h2>
          <StatusTimeline status={order.status} />
        </div>

        <ul className="px-5 py-2">
          {order.items.map((i) => (
            <li key={i.productId} className="flex flex-wrap items-center gap-x-4 border-b border-line/40 py-3 text-[13px] last:border-b-0">
              <span className="font-bold uppercase tracking-[0.03em]">{i.productName}</span>
              <span className="font-mono text-[11px] text-ink-soft">{i.sku}</span>
              <span className="font-mono text-[11px] text-ink-soft">
                {formatUsd(i.unitPrice)} ×{i.quantity}
              </span>
              <span className="ml-auto font-mono tabular-nums">{formatUsd(i.subtotal)}</span>
            </li>
          ))}
        </ul>

        <dl className="border-t-[1.5px] border-line px-5 py-3 text-[13px]">
          <div className="flex justify-between py-1"><dt className="text-ink-soft">Subtotal</dt><dd className="font-mono tabular-nums">{formatUsd(order.subtotal)}</dd></div>
          <div className="flex justify-between py-1"><dt className="text-ink-soft">Shipping</dt><dd className="font-mono tabular-nums">{order.shippingFee === 0 ? "free" : formatUsd(order.shippingFee)}</dd></div>
          <div className="mt-1 flex justify-between border-t-[1.5px] border-ink py-2 font-extrabold uppercase tracking-[0.14em]">
            <dt>Total</dt>
            <dd className="flex items-center gap-2 font-mono text-lg tabular-nums">
              <MagnitudeTick value={order.totalAmount} className="h-4" />
              {formatUsd(order.totalAmount)}
            </dd>
          </div>
        </dl>

        <div className="grid gap-0 border-t-[1.5px] border-line sm:grid-cols-2">
          <div className="border-r border-line/50 px-5 py-4 text-[13px]">
            <h3 className="mb-2 font-mono text-[11px] uppercase tracking-[0.22em] text-ink-soft">
              Ships to
            </h3>
            <p className="leading-relaxed">
              {order.shippingAddress.fullName}
              <br />
              {order.shippingAddress.address}, {order.shippingAddress.city}
              <br />
              <span className="font-mono text-[11px]">{order.shippingAddress.phone}</span>
            </p>
          </div>
          <div className="px-5 py-4 text-[13px]">
            <h3 className="mb-2 font-mono text-[11px] uppercase tracking-[0.22em] text-ink-soft">
              Payment
            </h3>
            {order.payment ? (
              <p className="leading-relaxed">
                <strong>{order.payment.status}</strong>, {formatUsd(order.payment.amount)}
                <br />
                <span className="font-mono text-[11px]">
                  {order.payment.transactionId ??
                    (order.payment.outcome === "expired"
                      ? "expired before pay, no transaction"
                      : "no transaction")}
                </span>
              </p>
            ) : (
              <p className="text-ink-soft">
                {order.status === "Pending"
                  ? "Awaiting payment on the hosted page."
                  : "Never charged."}
              </p>
            )}
          </div>
        </div>

        {cancelable && (
          <div className="flex flex-wrap items-center gap-3 border-t-[1.5px] border-ink bg-kraft/60 px-5 py-4">
            <Button
              variant="ghost"
              disabled={cancel.isPending}
              onClick={() => cancel.mutate(order.id)}
            >
              <Ban size={13} aria-hidden />
              {cancel.isPending ? "Cancelling…" : "Cancel order and release stock"}
            </Button>
            <span className="font-mono text-[11px] uppercase tracking-[0.12em] text-ink-soft">
              legal while pending; paid orders route to support
            </span>
          </div>
        )}
        {cancelError && (
          <div className="border-t border-line/50 px-5 py-3">
            <ErrorNote>{cancelError}</ErrorNote>
          </div>
        )}
        {cancel.isSuccess && (
          <p className="border-t border-line/50 px-5 py-3 font-mono text-[11px] uppercase tracking-[0.14em] text-band-green">
            Cancelled. Reservation released back to the bins.
          </p>
        )}
      </article>

      <p className="mt-4 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
        placed {new Date(order.createdAt).toLocaleString()} · mock ledger, no real
        charge. This record exists because a human pressed Pay on the hosted page
      </p>
    </main>
  );
}
