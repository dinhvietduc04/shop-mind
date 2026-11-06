"use client";

import { useDeferredValue } from "react";
import { Filter, RotateCcw } from "lucide-react";
import { ProductCard } from "@/components/product-card";
import { AssistantPanel } from "@/components/assistant-panel";
import { BinRail } from "@/components/shell";
import { Button, EmptyState, ErrorNote, Skeleton } from "@/components/ui";
import { useShopUI } from "@/lib/ui";
import { useProducts } from "@/lib/api/hooks";

export default function CatalogPage() {
  const { query, setQuery, category, setCategory } = useShopUI();
  const deferredQuery = useDeferredValue(query);

  const { data, isPending, isError, refetch, isFetching } = useProducts({
    search: deferredQuery.trim() || undefined,
    category: category ?? undefined,
    pageSize: 100,
  });

  const filtered = data?.items ?? [];
  const activeLabel = category
    ? filtered[0]?.categoryName ?? category
    : "every bin";

  return (
    <div className="flex min-h-[calc(100vh-57px)] flex-col lg:flex-row">
      {/* The bin rail belongs to the catalog only — it is the filter surface
          for this exact query. Bill/assistant/order pages render without it. */}
      <BinRail />
      <main className="min-w-0 flex-1 px-6 py-6">
        <div className="flex items-start gap-6">
          <div className="min-w-0 flex-1">
        <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">
          Bin wall — {activeLabel}
          <span className="ml-3 font-mono text-sm font-normal normal-case tracking-normal text-ink-soft">
            {isFetching ? "querying bins…" : `${data?.totalCount ?? 0} units`}
            <span className="ml-2">· X-Cache MISS = fresh from the API</span>
          </span>
        </h1>

        {isError ? (
          <div className="mt-6">
            <ErrorNote>
              <strong>The catalog service is not answering.</strong> Reads may be
              rate-limited (429) or the mock is still booting — nothing was lost.
              <div className="mt-3">
                <Button variant="ghost" onClick={() => refetch()}>
                  <RotateCcw size={13} aria-hidden /> Retry
                </Button>
              </div>
            </ErrorNote>
          </div>
        ) : isPending ? (
          <div className="mt-6 grid grid-cols-1 gap-6 md:grid-cols-2 2xl:grid-cols-3">
            {[0, 1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-[420px]" />
            ))}
          </div>
        ) : (
          <>
            <div className="mt-6 grid grid-cols-1 gap-6 md:grid-cols-2 2xl:grid-cols-3">
              {filtered.map((p) => (
                <ProductCard key={p.id} product={p} />
              ))}
            </div>

            {filtered.length === 0 && (
              <div className="mt-6">
                <EmptyState title="No units match this filter">
                  <p>
                    The bins hold nothing for “{query || activeLabel}”. Loosen the
                    band or drop a filter — server-side search tokenizes words of
                    3+ characters against name, brand, SKU and description.
                  </p>
                  <div className="mt-4 flex gap-2">
                    <Button variant="ghost" onClick={() => setQuery("")}>
                      <Filter size={13} aria-hidden /> Clear search
                    </Button>
                    {category && (
                      <Button variant="ghost" onClick={() => setCategory(null)}>
                        Leave the bin
                      </Button>
                    )}
                  </div>
                </EmptyState>
              </div>
            )}
          </>
        )}

        <div className="mt-10 xl:hidden">
          <AssistantPanel />
        </div>
      </div>

      {/* Locked right column at xl+ — the drawer stays inside the first
          viewport without ever covering a card's controls. */}
      <div className="sticky top-[92px] hidden w-[400px] shrink-0 xl:block">
        <AssistantPanel />
      </div>
        </div>
      </main>
    </div>
  );
}
