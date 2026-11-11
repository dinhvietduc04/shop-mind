"use client";

import Link from "next/link";
import { Kpi, Panel, Pill, statusBand } from "@/components/admin/widgets";
import { Skeleton, StockDots, Button } from "@/components/ui";
import { isRevenueEligible } from "@/lib/orders/lifecycle";
import { useAdminOrders, useAiLedger, useInventory } from "@/lib/api/hooks";
import { formatUsd } from "@/lib/api/types";
import { useSession } from "@/lib/session";

/** Admin dashboard: everything reads the same live mock db the storefront
 *  uses. KPIs move the moment a quote settles in another tab. */
export default function AdminDashboardPage() {
  const user = useSession((s) => s.user);
  const { data: orders, isPending: ordersPending } = useAdminOrders("");
  const { data: inventory, isPending: invPending } = useInventory();
  const { data: ledger, isPending: ledgerPending } = useAiLedger();

  const shipped = orders?.filter((o) => isRevenueEligible(o.status)) ?? [];
  const revenue = shipped.reduce((t, o) => t + o.totalAmount, 0);
  const openQuotes = orders?.filter((o) => o.status === "Pending").length ?? 0;
  const lowStock = inventory?.filter((r) => r.availableQuantity <= 5 && r.availableQuantity > 0) ?? [];
  const outStock = inventory?.filter((r) => r.availableQuantity <= 0) ?? [];
  const today = new Date().toISOString().slice(0, 10);
  const callsToday = ledger?.filter((e) => e.ts.startsWith(today)).length ?? 0;
  const errorsToday = ledger?.filter((e) => e.ts.startsWith(today) && e.status === "error").length ?? 0;

  const loading = ordersPending || invPending || ledgerPending;

  return (
    <div className="mt-5 space-y-5">
      <p className="font-mono text-[11px] uppercase tracking-[0.14em] text-ink-soft">
        signed in as {user?.email}. The console writes to the same catalog the
        customer storefront reads.
      </p>

      {loading ? (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
          {[...Array(6)].map((_, i) => (
            <Skeleton key={i} className="h-[92px]" />
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
          <Kpi label="Orders filed" value={orders?.length ?? 0} sub={`${openQuotes} quote(s) still open`} band="brown" />
          <Kpi label="Revenue" value={formatUsd(revenue)} sub="settled at the hosted page" band="green" />
          <Kpi
            label="Low stock"
            value={lowStock.length + outStock.length}
            sub={`${outStock.length} sold out · restock below`}
            band={outStock.length ? "red" : "yellow"}
          />
          <Kpi label="Open quotes" value={openQuotes} sub="reservations held against stock" band="yellow" />
          <Kpi
            label="AI calls today"
            value={callsToday}
            sub={`${errorsToday} rejected / disabled`}
            band="blue"
          />
          <Kpi
            label="Queue / DLQ"
            value={<span className="text-[13px] uppercase tracking-[0.12em]">not wired</span>}
            sub="B2 background queues land server-side, placeholder per plan"
            band="grey"
          />
        </div>
      )}

      <div className="grid gap-5 lg:grid-cols-2">
        <Panel
          title="Restock bench"
          aside={<Link href="/admin/inventory"><Button variant="ghost">Open inventory</Button></Link>}
        >
          {loading ? (
            <Skeleton className="h-40" />
          ) : lowStock.length + outStock.length === 0 ? (
            <p className="text-[13px] text-ink-soft">Every bin reads healthy. Nothing at or below 5 units.</p>
          ) : (
            <ul className="space-y-2">
              {[...outStock, ...lowStock].slice(0, 6).map((r) => (
                <li key={r.productId} className="flex items-center gap-3 text-[13px]">
                  <StockDots qty={r.availableQuantity} withLabel={false} />
                  <span className="min-w-0 flex-1 truncate">{r.productName}</span>
                  <span className="font-mono text-[11px] text-ink-soft">{r.sku}</span>
                  <span className="font-mono text-[12px] font-bold">{r.availableQuantity}</span>
                </li>
              ))}
            </ul>
          )}
        </Panel>

        <Panel
          title="Latest orders"
          aside={<Link href="/admin/orders"><Button variant="ghost">Open the order ledger</Button></Link>}
        >
          {loading ? (
            <Skeleton className="h-40" />
          ) : !orders?.length ? (
            <p className="text-[13px] text-ink-soft">No orders yet. Quote and pay from the storefront to see this move.</p>
          ) : (
            <ul className="space-y-2">
              {orders.slice(0, 6).map((o) => (
                <li key={o.id} className="flex items-center gap-3 text-[13px]">
                  <span className="font-mono text-[11px]">{o.orderNumber}</span>
                  <span className="min-w-0 flex-1 truncate text-ink-soft">{o.shippingAddress.fullName}</span>
                  <Pill band={statusBand(o.status)} label={o.status} />
                  <span className="font-mono text-[12px] font-bold">{formatUsd(o.totalAmount)}</span>
                  <Link href={`/admin/orders/${o.id}`} className="font-mono text-[11px] underline underline-offset-4">
                    open
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>
    </div>
  );
}


