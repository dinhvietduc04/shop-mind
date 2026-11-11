"use client";

import { useEffect, useRef, useDeferredValue } from "react";
import Link from "next/link";
import { ArrowLeft, ArrowRight, Filter, RotateCcw } from "lucide-react";
import { ProductCard } from "@/components/product-card";
import { AssistantPanel } from "@/components/assistant-panel";
import { BinRail } from "@/components/shell";
import { Button, EmptyState, ErrorNote, Skeleton } from "@/components/ui";
import { useCategories, useFacets, useProducts } from "@/lib/api/hooks";
import { encodeChipState } from "@/lib/attributes/codec";
import { useShopUI, type SortKey } from "@/lib/ui";
import { bandForCategory, bandClass } from "@/lib/bands";
import { cn } from "@/lib/cn";

const PAGE_SIZE = 6;

const SORTS: { value: SortKey; label: string }[] = [
  { value: "newest", label: "newest first" },
  { value: "price_asc", label: "price ↑" },
  { value: "price_desc", label: "price ↓" },
  { value: "name_asc", label: "name A–Z" },
  { value: "name_desc", label: "name Z–A" },
];

/**
 * The home bin wall and every category landing share this catalog engine.
 * The server handles all filtering, sorting, and paging. This component only
 * builds a ProductQuery and renders what the (mock) API returns.
 *
 * `lockedCategory` (a slug) pins the query to one bin: the BinRail disappears,
 * the facet chips scope to that bin, and leaving means going somewhere else.
 */
export function CatalogView({
  lockedCategory,
  bins = false,
  assistant = false,
  headerExtra,
}: {
  lockedCategory?: string;
  bins?: boolean;
  assistant?: boolean;
  headerExtra?: React.ReactNode;
}) {
  const ui = useShopUI();
  const deferredQuery = useDeferredValue(ui.query);
  const locked = !!lockedCategory;
  const activeCategory = lockedCategory ?? ui.category ?? undefined;

  /* Spec chips are bin-scoped: arriving in a different bin must not carry
     stale keys from the last one. */
  const seenCategory = useRef(activeCategory);
  useEffect(() => {
    if (seenCategory.current !== activeCategory && Object.keys(useShopUI.getState().specs).length) {
      useShopUI.setState({ specs: {}, page: 1 });
      seenCategory.current = activeCategory;
    }
  }, [activeCategory]);

  const { data, isPending, isError, refetch, isFetching } = useProducts({
    search: deferredQuery.trim() || undefined,
    category: activeCategory,
    sort: ui.sort,
    inStockOnly: ui.inStockOnly || undefined,
    specs: encodeChipState(ui.specs) || undefined,
    page: ui.page,
    pageSize: PAGE_SIZE,
  });
  const { data: facets } = useFacets(activeCategory);

  const items = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;
  const label = activeCategory ?? "every bin";
  const enumFacets = (facets ?? []).filter((f) => f.type === "enum").slice(0, 3);
  const hasFilters =
    !!ui.query || !!activeCategory || ui.inStockOnly || Object.keys(ui.specs).length > 0 || ui.sort !== "newest";

  return (
    <div className="flex min-h-[calc(100vh-57px)] flex-col lg:flex-row">
      {bins && <BinRail />}
      <main className="min-w-0 flex-1 px-6 py-6">
        <div className="flex items-start gap-6">
          <div className="min-w-0 flex-1">
            <h1 className="text-2xl font-extrabold uppercase tracking-[0.04em]">
              Bin wall, {label}
              {locked && (
                <Link
                  href="/"
                  className="ml-3 align-middle font-mono text-[11px] font-normal normal-case tracking-[0.1em] text-ink-soft underline decoration-line hover:text-ink"
                >
                  leave this bin
                </Link>
              )}
              <span className="ml-3 font-mono text-sm font-normal normal-case tracking-normal text-ink-soft">
                {isFetching ? "querying bins…" : `${data?.totalCount ?? 0} units`}
              </span>
            </h1>

            {headerExtra}

            {/* --------------------- server-driven filter bar -------------------- */}
            <div className="mt-5 flex flex-wrap items-center gap-x-5 gap-y-3 rounded-label border-[1.5px] border-line bg-card px-4 py-3">
              <label className="flex items-center gap-2 text-[11px] font-bold uppercase tracking-[0.16em]">
                Sort
                <select
                  value={ui.sort}
                  onChange={(e) => ui.setSort(e.target.value as SortKey)}
                  className="rounded-[4px] border-[1.5px] border-line bg-paper px-2 py-1 font-mono text-[12px] normal-case tracking-normal focus:border-ink"
                >
                  {SORTS.map((s) => (
                    <option key={s.value} value={s.value}>
                      {s.label}
                    </option>
                  ))}
                </select>
              </label>

              <label className="flex cursor-pointer items-center gap-2 text-[11px] font-bold uppercase tracking-[0.16em]">
                <input
                  type="checkbox"
                  checked={ui.inStockOnly}
                  onChange={(e) => ui.setInStockOnly(e.target.checked)}
                  className="h-4 w-4 accent-[#1a1a1a]"
                />
                In stock only
              </label>

              {hasFilters && (
                <Button variant="ghost" className="ml-auto" onClick={ui.clearFilters}>
                  <RotateCcw size={12} aria-hidden /> Clear filters
                </Button>
              )}
            </div>

            {/* spec facet chips, only meaningful with a bin in scope */}
            {!isPending && activeCategory && enumFacets.length > 0 && (
              <div className="mt-4 space-y-2">
                {enumFacets.map((f) => (
                  <div key={f.key} className="flex flex-wrap items-center gap-2">
                    <span className="mr-1 font-mono text-[11px] uppercase tracking-[0.18em] text-ink-soft">
                      {f.label}
                    </span>
                    {(f.values ?? []).slice(0, 6).map((v) => {
                      const on = (ui.specs[f.key] ?? []).includes(String(v));
                      return (
                        <button
                          key={String(v)}
                          onClick={() => ui.toggleSpec(f.key, String(v))}
                          aria-pressed={on}
                          className={cn(
                            "rounded-[4px] border-[1.5px] px-2 py-0.5 font-mono text-[11px] tracking-[0.06em]",
                            on
                              ? "border-ink bg-ink text-paper"
                              : "border-line bg-card hover:border-ink"
                          )}
                        >
                          {String(v)}
                        </button>
                      );
                    })}
                  </div>
                ))}
              </div>
            )}

            {isError ? (
              <div className="mt-6">
                <ErrorNote>
                  <strong>The catalog service is not answering.</strong> Reads may be
                  rate-limited (429) or the mock is still booting. Nothing was lost.
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
                  {items.map((p) => (
                    <ProductCard key={p.id} product={p} />
                  ))}
                </div>

                {items.length === 0 && (
                  <div className="mt-6">
                    <EmptyState title="No units match this filter">
                      <p>
                        The bins hold nothing for &quot;{ui.query || label}&quot;. Loosen a
                        chip or drop a filter. Server-side search tokenizes words
                        of 3+ characters against name, brand, SKU and description.
                      </p>
                      <div className="mt-4 flex gap-2">
                        <Button variant="ghost" onClick={ui.clearFilters}>
                          <Filter size={13} aria-hidden /> Clear filters
                        </Button>
                      </div>
                    </EmptyState>
                  </div>
                )}

                {totalPages > 1 && (
                  <nav
                    aria-label="Pagination"
                    className="mt-8 flex items-center justify-center gap-4"
                  >
                    <Button
                      variant="ghost"
                      disabled={ui.page <= 1}
                      onClick={() => ui.setPage(ui.page - 1)}
                    >
                      <ArrowLeft size={13} aria-hidden /> Prev
                    </Button>
                    <span className="font-mono text-[12px] uppercase tracking-[0.18em]">
                      page {data?.page} · of {totalPages}
                    </span>
                    <Button
                      variant="ghost"
                      disabled={ui.page >= totalPages}
                      onClick={() => ui.setPage(ui.page + 1)}
                    >
                      Next <ArrowRight size={13} aria-hidden />
                    </Button>
                  </nav>
                )}
              </>
            )}

            {assistant && (
              <div className="mt-10 xl:hidden">
                <AssistantPanel />
              </div>
            )}
          </div>

          {/* Locked right column at xl+. The drawer stays inside the first
              viewport without ever covering a card's controls. */}
          {assistant && (
            <div className="sticky top-[92px] hidden w-[400px] shrink-0 xl:block">
              <AssistantPanel />
            </div>
          )}
        </div>
      </main>
    </div>
  );
}

/** Horizontal sibling-bin chips used on category landings. */
export function BinChips({ activeSlug }: { activeSlug: string }) {
  const { data: categories } = useCategories();
  return (
    <div className="mt-4 flex gap-2 overflow-x-auto pb-1">
      {(categories ?? []).map((c) => (
        <Link
          key={c.id}
          href={c.slug === activeSlug ? "/" : `/category/${c.slug}`}
          className={cn(
            "flex shrink-0 items-center gap-2 rounded-[6px] border-[1.5px] px-3 py-1.5 text-[11px] font-bold uppercase tracking-[0.12em]",
            c.slug === activeSlug
              ? "border-ink bg-ink text-paper"
              : "border-line bg-card hover:border-ink"
          )}
        >
          <span
            aria-hidden
            className={cn(
              "h-3 w-2.5 rounded-[2px] border border-[#1a1a1a] dark:border-[#8a8068]",
              bandClass(bandForCategory(c.slug))
            )}
          />
          {c.name}
        </Link>
      ))}
    </div>
  );
}
