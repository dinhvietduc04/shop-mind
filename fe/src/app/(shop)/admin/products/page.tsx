"use client";

import { useState } from "react";
import Link from "next/link";
import { Plus, Trash2 } from "lucide-react";
import { AdminTable, Panel, Pill, statusBand, td, th } from "@/components/admin/widgets";
import { Button, ErrorNote, MagnitudeTick, Skeleton, StockDots } from "@/components/ui";
import { useAdminProducts, useDeleteProduct } from "@/lib/api/hooks";
import { ApiError, formatUsd } from "@/lib/api/types";
import { BAND_BG, bandForCategory } from "@/lib/bands";

/* category band fill by slug, same ladder the storefront rails use */
function bandClassOf(categoryName: string | null) {
  return BAND_BG[bandForCategory(categoryName?.toLowerCase())];
}

export default function AdminProductsPage() {
  const [search, setSearch] = useState("");
  const { data, isPending } = useAdminProducts(search);
  const del = useDeleteProduct();
  const [confirming, setConfirming] = useState<string | null>(null);

  return (
    <div className="mt-5">
      <Panel
        title={`Catalog · ${data?.length ?? "…"}`}
        aside={
          <>
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="search name / sku / brand"
              aria-label="Search products"
              className="w-[150px] min-w-0 shrink rounded-[4px] border-[1.5px] border-line bg-paper px-3 py-1.5 font-mono text-[12px] focus:border-ink sm:w-[220px]"
            />
            <Link href="/admin/products/new">
              <Button variant="add">
                <Plus size={13} aria-hidden /> New product
              </Button>
            </Link>
          </>
        }
      >
        {isPending ? (
          <Skeleton className="h-64" />
        ) : !data?.length ? (
          <p className="text-[13px] text-ink-soft">Nothing matches “{search}”.</p>
        ) : (
          <AdminTable
            head={
              <>
                <th className={th}>Product</th>
                <th className={th}>SKU</th>
                <th className={th}>Bin</th>
                <th className={th}>Price</th>
                <th className={th}>Stock</th>
                <th className={th}>Status</th>
                <th className={th}>Actions</th>
              </>
            }
          >
            {data.map((p) => (
              <tr key={p.id} className="border-b border-dashed border-line">
                <td className={td}>
                  <span className="font-bold">{p.name}</span>
                  <span className="ml-2 font-mono text-[11px] text-ink-soft">{p.brand ?? "—"}</span>
                </td>
                <td className={`${td} font-mono text-[11px]`}>{p.sku}</td>
                <td className={td}>
                  <span className="inline-flex items-center gap-1.5">
                    <span className={`h-3 w-3 rounded-[2px] ${bandClassOf(p.categoryName)}`} aria-hidden />
                    {p.categoryName ?? "—"}
                  </span>
                </td>
                <td className={td}>
                  <span className="flex items-center gap-2">
                    <MagnitudeTick value={p.price} className="h-4" />
                    <span className="font-mono text-[12px] font-bold">{formatUsd(p.price)}</span>
                  </span>
                </td>
                <td className={td}>
                  <span className="flex items-center gap-2">
                    <StockDots qty={p.availableStock} withLabel={false} />
                    <span className="font-mono text-[12px]">
                      {p.availableStock}<span className="text-ink-soft">/{p.quantity}</span>
                    </span>
                  </span>
                </td>
                <td className={td}>
                  <Pill band={statusBand(p.status)} label={p.status} />
                </td>
                <td className={td}>
                  {confirming === p.id ? (
                    <span className="flex items-center gap-2">
                      <Button
                        variant="human"
                        disabled={del.isPending}
                        onClick={() =>
                          del.mutate(p.id, {
                            onSuccess: () => setConfirming(null),
                            onError: () => setConfirming(null),
                          })
                        }
                      >
                        <Trash2 size={12} aria-hidden /> Confirm delete
                      </Button>
                      <Button variant="ghost" onClick={() => setConfirming(null)}>
                        Keep
                      </Button>
                    </span>
                  ) : (
                    <span className="flex items-center gap-2">
                      <Link href={`/product/${p.slug}`}>
                        <Button variant="ghost">View</Button>
                      </Link>
                      <Link href={`/admin/products/${p.id}/edit`}>
                        <Button variant="ghost">Edit</Button>
                      </Link>
                      <Button variant="ghost" onClick={() => setConfirming(p.id)} aria-label={`Delete ${p.name}`}>
                        <Trash2 size={12} aria-hidden /> Delete
                      </Button>
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </AdminTable>
        )}
        {del.error && (
          <div className="mt-3">
            <ErrorNote>
              <strong>Could not delete.</strong>{" "}
              {del.error instanceof ApiError ? del.error.title : "Unexpected error."} — the
              guard mirrors the real FK rules: cart lines and open quotes hold products.
            </ErrorNote>
          </div>
        )}
      </Panel>
    </div>
  );
}

