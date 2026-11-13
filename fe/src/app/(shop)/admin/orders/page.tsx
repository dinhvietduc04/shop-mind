"use client";

import { useState } from "react";
import Link from "next/link";
import { AdminTable, Panel, td, th } from "@/components/admin/widgets";
import { StatusChip } from "@/components/order-status";
import { Button, Skeleton } from "@/components/ui";
import { useAdminOrders } from "@/features/admin";
import { formatUsd, type OrderStatus } from "@/infrastructure/api";
import { cn } from "@/lib/cn";

const STATUSES: (OrderStatus | "All")[] = [
  "All", "Pending", "Paid", "Processing", "Shipped", "Delivered", "Cancelled", "PaymentFailed",
];

export default function AdminOrdersPage() {
  const [status, setStatus] = useState<OrderStatus | "All">("All");
  const { data, isPending } = useAdminOrders(status === "All" ? "" : status);

  return (
    <div className="mt-5">
      <Panel title={`Order ledger · ${data?.length ?? "…"}`}>
        <div className="mb-4 flex flex-wrap gap-1.5">
          {STATUSES.map((s) => (
            <button
              key={s}
              aria-pressed={status === s}
              onClick={() => setStatus(s)}
              className={cn(
                "rounded-[4px] border-[1.5px] px-2.5 py-1 font-mono text-[11px] font-bold uppercase tracking-[0.12em]",
                status === s ? "border-ink bg-ink text-paper" : "border-line text-ink-soft hover:border-ink hover:text-ink"
              )}
            >
              {s}
            </button>
          ))}
        </div>
        {isPending ? (
          <Skeleton className="h-64" />
        ) : !data?.length ? (
          <p className="text-[13px] text-ink-soft">No orders with status &quot;{status}&quot;.</p>
        ) : (
          <AdminTable
            head={
              <>
                <th className={th}>Order</th>
                <th className={th}>Filed</th>
                <th className={th}>Ship to</th>
                <th className={th}>Lines</th>
                <th className={th}>Total</th>
                <th className={th}>Status</th>
                <th className={th}>Open</th>
              </>
            }
          >
            {data.map((o) => (
              <tr key={o.id} className="border-b border-dashed border-line">
                <td className={`${td} font-mono text-[11px] font-bold`}>{o.orderNumber}</td>
                <td className={`${td} font-mono text-[11px] text-ink-soft`}>
                  {new Date(o.createdAt).toLocaleString()}
                </td>
                <td className={td}>
                  {o.shippingAddress.fullName}
                  <span className="block text-[11px] text-ink-soft">
                    {o.shippingAddress.address}, {o.shippingAddress.city}
                  </span>
                </td>
                <td className={`${td} font-mono text-[12px]`}>{o.items.length}</td>
                <td className={`${td} font-mono text-[12px] font-bold`}>{formatUsd(o.totalAmount)}</td>
                <td className={td}><StatusChip status={o.status} /></td>
                <td className={td}>
                  <Link href={`/admin/orders/${o.id}`}>
                    <Button variant="ghost">Open the ledger card</Button>
                  </Link>
                </td>
              </tr>
            ))}
          </AdminTable>
        )}
      </Panel>
    </div>
  );
}
