"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { AdminTable, Panel, td, th } from "@/components/admin/widgets";
import { StatusChip, StatusTimeline } from "@/components/order-status";
import { Button, ErrorNote, Skeleton } from "@/components/ui";
import { useAdminOrder, useSetOrderStatus } from "@/lib/api/hooks";
import { legalTargets } from "@/lib/orders/lifecycle";
import { ApiError, formatUsd, type OrderStatus } from "@/lib/api/types";

/** Status dropdown offers only legal targets. A client mirror of
 *  OrderTransitions.cs. The mock re-checks server-side, so hand-crafted
 *  requests fail exactly like Shop.Api would. */
export default function AdminOrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data: order, isPending, isError } = useAdminOrder(id);
  const setStatus = useSetOrderStatus();
  const [target, setTarget] = useState<OrderStatus | "">("");

  if (isPending) return <Skeleton className="mt-6 h-72" />;
  if (isError || !order)
    return (
      <div className="mt-6 max-w-[520px]">
        <ErrorNote><strong>Order not found.</strong></ErrorNote>
      </div>
    );

  const legal = legalTargets(order.status);
  const error = setStatus.error;

  return (
    <div className="mt-5 space-y-5">
      <Link href="/admin/orders" className="inline-flex items-center gap-1.5 font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft hover:text-ink">
        <ArrowLeft size={12} aria-hidden /> back to the ledger
      </Link>

      <Panel title={`Order ${order.orderNumber}`} aside={<StatusChip status={order.status} />}>
        <div className="grid gap-4 sm:grid-cols-[1fr_auto]">
          <div>
            <dl className="space-y-1 text-[13px]">
              <div className="flex gap-2">
                <dt className="font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">Filed</dt>
                <dd>{new Date(order.createdAt).toLocaleString()}</dd>
              </div>
              <div className="flex gap-2">
                <dt className="font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">Ship to</dt>
                <dd>{order.shippingAddress.fullName} · {order.shippingAddress.phone}</dd>
              </div>
              <div className="flex gap-2">
                <dt className="font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">Address</dt>
                <dd>{order.shippingAddress.address}, {order.shippingAddress.city}</dd>
              </div>
              {order.payment && (
                <div className="flex gap-2">
                  <dt className="font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">Payment</dt>
                  <dd className="font-mono text-[12px]">
                    {order.payment.status} · {formatUsd(order.payment.amount)}
                    {order.payment.transactionId && <span className="text-ink-soft"> · {order.payment.transactionId}</span>}
                  </dd>
                </div>
              )}
            </dl>
          </div>
          <div className="sm:border-l-[1.5px] sm:border-dashed sm:border-line sm:pl-5">
            <StatusTimeline status={order.status} />
          </div>
        </div>

        <div className="mt-4">
          <AdminTable
            head={
              <>
                <th className={th}>Product</th>
                <th className={th}>SKU</th>
                <th className={th}>Qty</th>
                <th className={th}>Unit</th>
                <th className={th}>Line</th>
              </>
            }
          >
            {order.items.map((it) => (
              <tr key={it.productId} className="border-b border-dashed border-line">
                <td className={`${td} font-bold`}>{it.productName}</td>
                <td className={`${td} font-mono text-[11px]`}>{it.sku}</td>
                <td className={`${td} font-mono text-[12px]`}>{it.quantity}</td>
                <td className={`${td} font-mono text-[12px]`}>{formatUsd(it.unitPrice)}</td>
                <td className={`${td} font-mono text-[12px] font-bold`}>{formatUsd(it.subtotal)}</td>
              </tr>
            ))}
          </AdminTable>
          <p className="mt-3 text-right font-mono text-[13px]">
            subtotal {formatUsd(order.subtotal)} · shipping {formatUsd(order.shippingFee)} ·{" "}
            <strong>{formatUsd(order.totalAmount)}</strong>
          </p>
        </div>
      </Panel>

      <Panel title="Fulfillment desk">
        {legal.length === 0 ? (
          <p className="text-[13px] leading-relaxed text-ink-soft">
            <strong>{order.status}</strong> is terminal in the state machine. There is no legal
            next state, so this dropdown would be a lie. The row is closed.
          </p>
        ) : (
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2">
              <span className="font-mono text-[11px] font-bold uppercase tracking-[0.18em] text-ink-soft">
                {order.status} →
              </span>
              <select
                value={target}
                onChange={(e) => setTarget(e.target.value as OrderStatus)}
                className="rounded-[4px] border-[1.5px] border-line bg-paper px-3 py-2 font-mono text-[13px] focus:border-ink"
                aria-label="Next status"
              >
                <option value="">pick a legal target</option>
                {legal.map((s) => (
                  <option key={s} value={s}>{s}</option>
                ))}
              </select>
            </label>
            <Button
              variant="human"
              disabled={!target || setStatus.isPending}
              onClick={() =>
                target &&
                setStatus.mutate(
                  { id: order.id, status: target },
                  { onSuccess: () => setTarget("") }
                )
              }
            >
              {setStatus.isPending ? "Writing…" : "Apply transition"}
            </Button>
            <p className="w-full text-[11px] leading-relaxed text-ink-soft">
              Yellow = a human decision. A Pending order moving through this desk runs the same
              guarded settle as the payment page. Reservations commit or release.
            </p>
          </div>
        )}
        {error && (
          <div className="mt-3">
            <ErrorNote>
              <strong>Transition refused.</strong>{" "}
              {error instanceof ApiError ? error.title : "Unexpected error."}. The server keeps
              its own copy of the state machine; the dropdown is convenience, not permission.
            </ErrorNote>
          </div>
        )}
      </Panel>
    </div>
  );
}
