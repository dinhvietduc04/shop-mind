import type { ProductQuery } from "./types";

/**
 * Query keys are the contract map — one place to grep for every endpoint.
 * C6: prefixes are FIRST-CLASS members now (productsAll, productAny, …).
 * When callers had to hand-write `["products"]` to invalidate a whole wall,
 * the key map was a shallow naming layer its own callers out-managed.
 */
export const qk = {
  /* exact keys */
  products: (q: ProductQuery) => ["products", q] as const,
  product: (id: string) => ["product", id] as const,
  categories: () => ["categories"] as const,
  facets: (category?: string) => ["facets", category] as const,
  cart: () => ["cart"] as const,
  quote: (sessionToken: string) => ["quote", sessionToken] as const,
  orders: () => ["orders"] as const,
  order: (id: string) => ["order", id] as const,
  activeQuote: () => ["orders", "active-quote"] as const,
  address: () => ["account", "address"] as const,
  adminProducts: (search: string) => ["admin", "products", search] as const,
  adminProduct: (id: string) => ["admin", "product", id] as const,
  adminCategories: () => ["admin", "categories"] as const,
  adminInventory: () => ["admin", "inventory"] as const,
  adminOrders: (status: string) => ["admin", "orders", status] as const,
  adminOrder: (id: string) => ["admin", "order", id] as const,
  aiLedger: () => ["admin", "ai", "ledger"] as const,
  aiConversations: () => ["admin", "ai", "conversations"] as const,
  aiSettings: () => ["admin", "ai", "settings"] as const,

  /* prefixes — for invalidation recipes only; reads still use exact keys */
  productsAll: () => ["products"] as const,
  productAny: () => ["product"] as const,
  facetsAll: () => ["facets"] as const,
  adminAll: () => ["admin"] as const,
  adminOrdersAll: () => ["admin", "orders"] as const,
};
