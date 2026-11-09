"use client";

import { useState } from "react";
import { AdminTable, Panel, td, th } from "@/components/admin/widgets";
import { Button, ErrorNote, Skeleton, StockDots } from "@/components/ui";
import { useInventory, useSetStock } from "@/lib/api/hooks";
import { ApiError } from "@/lib/api/types";

export default function AdminInventoryPage() {
  const [search, setSearch] = useState("");
  const { data, isPending } = useInventory();
  const setStock = useSetStock();
  const rows = data?.filter((r) =>
    !search || `${r.productName} ${r.sku}`.toLowerCase().includes(search.toLowerCase())
  );

  return (
    <div className="mt-5">
      <Panel
        title="Inventory"
        aside={
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="filter name / sku"
            aria-label="Search inventory"
            className="w-[220px] rounded-[4px] border-[1.5px] border-line bg-paper px-3 py-1.5 font-mono text-[12px] focus:border-ink"
          />
        }
      >
        <p className="mb-3 text-[12px] leading-relaxed text-ink-soft">
          You set <strong>total stock</strong>. <span className="font-mono">Reserved</span> is
          held by open quotes and protected — a write below it is refused, because that would
          oversell stock a human already committed to at the bill.
        </p>
        {isPending ? (
          <Skeleton className="h-64" />
        ) : !rows?.length ? (
          <p className="text-[13px] text-ink-soft">Nothing matches “{search}”.</p>
        ) : (
          <AdminTable
            head={
              <>
                <th className={th}>Product</th>
                <th className={th}>SKU</th>
                <th className={th}>Total</th>
                <th className={th}>Reserved</th>
                <th className={th}>Available</th>
                <th className={th}>Adjust</th>
              </>
            }
          >
            {rows?.map((r) => (
              // key includes the server quantity: a successful write remounts
              // the row so its local input can't go stale against the db
              <StockRow key={r.productId + ":" + r.quantity} row={r} busy={setStock.isPending} onSet={(q) => setStock.mutate({ productId: r.productId, quantity: q })} />
            ))}
          </AdminTable>
        )}
        {setStock.error && (
          <div className="mt-3">
            <ErrorNote>
              <strong>Write refused.</strong>{" "}
              {setStock.error instanceof ApiError ? setStock.error.title : "Unexpected error."}
            </ErrorNote>
          </div>
        )}
      </Panel>
    </div>
  );
}

function StockRow({
  row,
  busy,
  onSet,
}: {
  row: { productId: string; productName: string; sku: string; quantity: number; reservedQuantity: number; availableQuantity: number };
  busy: boolean;
  onSet: (quantity: number) => void;
}) {
  const [value, setValue] = useState(String(row.quantity));
  const num = Number(value);
  const dirty = Number.isInteger(num) && num !== row.quantity && num >= row.reservedQuantity;

  return (
    <tr className="border-b border-dashed border-line">
      <td className={`${td} font-bold`}>{row.productName}</td>
      <td className={`${td} font-mono text-[11px]`}>{row.sku}</td>
      <td className={td}>
        <span className="flex items-center gap-2">
          <input
            value={value}
            onChange={(e) => setValue(e.target.value.replace(/[^\d-]/g, ""))}
            inputMode="numeric"
            aria-label={`Total stock for ${row.productName}`}
            className="w-[72px] rounded-[4px] border-[1.5px] border-line bg-paper px-2 py-1 font-mono text-[12px] focus:border-ink"
          />
          <Button variant="human" disabled={!dirty || busy} onClick={() => onSet(num)}>
            Set
          </Button>
        </span>
      </td>
      <td className={`${td} font-mono text-[12px]`}>
        {row.reservedQuantity > 0 ? (
          <span className="text-band-yellow">● {row.reservedQuantity}</span>
        ) : (
          <span className="text-ink-soft">0</span>
        )}
      </td>
      <td className={td}>
        <span className="flex items-center gap-2">
          <StockDots qty={row.availableQuantity} withLabel={false} />
          <span className="font-mono text-[12px] font-bold">{row.availableQuantity}</span>
        </span>
      </td>
      <td className={td}>
        <span className="flex gap-1">
          {[5, 25].map((d) => (
            <Button key={d} variant="ghost" disabled={busy} onClick={() => onSet(Math.max(row.reservedQuantity, row.quantity + d))}>
              +{d}
            </Button>
          ))}
          <Button variant="ghost" disabled={busy || row.quantity - 5 < row.reservedQuantity} onClick={() => onSet(row.quantity - 5)}>
            −5
          </Button>
        </span>
      </td>
    </tr>
  );
}
