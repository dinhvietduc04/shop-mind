import type { ProductQuery } from "@/lib/api/contracts/schemas";

/** One place to grep for every endpoint. Prefixes exist only for invalidation
 *  recipes; reads use exact keys. */
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

  /* prefixes, for invalidation recipes only; reads still use exact keys */
  productsAll: () => ["products"] as const,
  productAny: () => ["product"] as const,
  facetsAll: () => ["facets"] as const,
  adminAll: () => ["admin"] as const,
  adminOrdersAll: () => ["admin", "orders"] as const,
};
