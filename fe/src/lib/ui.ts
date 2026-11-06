"use client";

import { create } from "zustand";

/** Shared catalog UI state (search text + active category slug) between masthead, bin rail, grid. */
interface ShopUIState {
  query: string;
  setQuery: (q: string) => void;
  category: string | null;
  setCategory: (c: string | null) => void;
}

export const useShopUI = create<ShopUIState>((set) => ({
  query: "",
  setQuery: (query) => set({ query }),
  category: null,
  setCategory: (category) => set({ category }),
}));
