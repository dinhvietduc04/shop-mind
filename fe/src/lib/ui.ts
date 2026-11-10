"use client";

import { create } from "zustand";
import type { ProductQuery } from "@/lib/api/types";

export type SortKey = NonNullable<ProductQuery["sort"]>;

/** Shared catalog UI state: search text, active bin, sort, stock toggle, spec
 *  chips and page. Every filter mutation resets page — stale pagination on a
 *  narrowed result set is how servers get accidentally paged off a cliff. */
interface ShopUIState {
  query: string;
  category: string | null;
  sort: SortKey;
  inStockOnly: boolean;
  specs: Record<string, string[]>;
  page: number;
  setQuery: (q: string) => void;
  setCategory: (c: string | null) => void;
  setSort: (s: SortKey) => void;
  setInStockOnly: (v: boolean) => void;
  toggleSpec: (key: string, value: string) => void;
  setPage: (p: number) => void;
  clearFilters: () => void;
}

export const useShopUI = create<ShopUIState>((set) => ({
  query: "",
  category: null,
  sort: "newest",
  inStockOnly: false,
  specs: {},
  page: 1,
  setQuery: (query) => set({ query, page: 1 }),
  // Changing the bin invalidates the previous bin's spec keys entirely.
  setCategory: (category) => set({ category, specs: {}, page: 1 }),
  setSort: (sort) => set({ sort, page: 1 }),
  setInStockOnly: (inStockOnly) => set({ inStockOnly, page: 1 }),
  toggleSpec: (key, value) =>
    set((s) => {
      const cur = s.specs[key] ?? [];
      const next = cur.includes(value)
        ? cur.filter((v) => v !== value)
        : [...cur, value];
      const specs = { ...s.specs };
      if (next.length) specs[key] = next;
      else delete specs[key];
      return { specs, page: 1 };
    }),
  setPage: (page) => set({ page }),
  clearFilters: () =>
    set({ query: "", category: null, sort: "newest", inStockOnly: false, specs: {}, page: 1 }),
}));

/** Serialize spec chips to the wire DSL — one clause per key through the
 *  shared codec (C8): >1 selected value becomes `in`, one becomes `eq`. */
export { encodeChipState as specsToString } from "@/lib/attributes/codec";
