"use client";

import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { api, idemKey, productsQueryToParams } from "./client";
import { qk } from "./keys";
import {
  adminLedgerStale,
  cartStale,
  catalogWriteStale,
  orderLedgerStale,
  quoteWallsStale,
  stockWallStale,
} from "./invalidation";
import { useSession } from "@/lib/session";
import type {
  AiConversationDto,
  AiLedgerEntryDto,
  AiSettingsDto,
  CartDto,
  CategoryDto,
  CreateCategoryRequest,
  FacetDto,
  InventoryDto,
  OrderDto,
  OrderStatus,
  PagedResult,
  ProductAdminDto,
  ProductDetailDto,
  ProductDto,
  ProductQuery,
  QuoteDto,
  ShippingAddressDto,
  UpdateCategoryRequest,
} from "./types";

/* The contract map lives in ./keys (with first-class prefixes for the
   invalidation recipes); re-exported so existing imports keep working. */
export { qk };

export function useProducts(q: ProductQuery, opts?: { enabled?: boolean }) {
  return useQuery({
    queryKey: qk.products(q),
    queryFn: () =>
      api<PagedResult<ProductDto>>(`/products`, { query: productsQueryToParams(q) }),
    placeholderData: keepPreviousData,
    enabled: opts?.enabled ?? true,
  });
}

export function useProduct(idOrSlug: string) {
  return useQuery({
    queryKey: qk.product(idOrSlug),
    queryFn: () => api<ProductDetailDto>(`/products/${idOrSlug}`),
  });
}

export function useCategories() {
  return useQuery({
    queryKey: qk.categories(),
    queryFn: () => api<CategoryDto[]>("/categories"),
  });
}

export function useFacets(category?: string) {
  return useQuery({
    queryKey: qk.facets(category),
    queryFn: () => api<FacetDto[]>(`/products/facets`, { query: { category } }),
    enabled: true,
  });
}

/* ---------------------------------- cart ---------------------------------- */

export function useCart() {
  /* Guests have no bucket to read — don't fire the 401, wait for a session. */
  const authed = useSession((s) => s.user !== null);
  return useQuery({
    queryKey: qk.cart(),
    queryFn: () => api<CartDto>("/cart"),
    enabled: authed,
  });
}

export function useAddToCart() {
  return useMutation({
    mutationFn: ({ productId, quantity = 1 }: { productId: string; quantity?: number }) =>
      api<CartDto>("/cart/items", {
        method: "POST",
        body: JSON.stringify({ productId, quantity }),
        idempotencyKey: idemKey(),
      }),
    onSuccess: () => cartStale(),
  });
}

export function useUpdateCartItem() {
  return useMutation({
    mutationFn: ({ productId, quantity }: { productId: string; quantity: number }) =>
      api<CartDto>(`/cart/items/${productId}`, {
        method: "PATCH",
        body: JSON.stringify({ quantity }),
      }),
    onSuccess: () => cartStale(),
  });
}

export function useRemoveCartItem() {
  return useMutation({
    mutationFn: (productId: string) =>
      api<CartDto>(`/cart/items/${productId}`, { method: "DELETE" }),
    onSuccess: () => cartStale(),
  });
}

/* ---------------------------- quote & payment -----------------------------
 * The session pointer + outcome live in lib/quotes/intake (C1); these hooks
 * are only the HTTP verbs. QuoteDto carries sessionToken (§B5+) — callers
 * never decompose hostedPayUrl. */

export function useCreateQuote() {
  return useMutation({
    mutationFn: (address: ShippingAddressDto) =>
      api<QuoteDto>("/checkout/quote", {
        method: "POST",
        body: JSON.stringify({ shippingAddress: address }),
        idempotencyKey: idemKey(),
      }),
    onSuccess: () => cartStale(),
  });
}

export function useQuote(sessionToken: string) {
  return useQuery({
    queryKey: qk.quote(sessionToken),
    queryFn: () => api<QuoteDto>(`/quotes/${sessionToken}`),
    enabled: !!sessionToken,
    // live expiry: poll while pending; a settled session is final
    refetchInterval: (query) =>
      query.state.data?.order.status === "Pending" ? 10_000 : false,
  });
}

/** Bill-page discovery of an open quote from ANY origin (agent-created included). */
export function useActiveQuote() {
  return useQuery({
    queryKey: qk.activeQuote(),
    queryFn: () => api<QuoteDto>("/checkout/quote"),
    refetchInterval: (query) =>
      query.state.data?.order?.status === "Pending" ? 15_000 : false,
  });
}

export function useConfirmPayment(sessionToken: string) {
  return useMutation({
    mutationFn: () =>
      api<{ ok: boolean; order: OrderDto }>(`/quotes/${sessionToken}/confirm`, {
        method: "POST",
        idempotencyKey: idemKey(),
      }),
    onSuccess: () => {
      quoteWallsStale(sessionToken);
      cartStale();
      // reservations committed — every stock tile on the wall is now stale
      stockWallStale();
    },
  });
}

export function useCancelPayment(sessionToken: string) {
  return useMutation({
    mutationFn: () =>
      api<{ ok: true }>(`/quotes/${sessionToken}/cancel`, { method: "POST" }),
    onSuccess: () => {
      quoteWallsStale(sessionToken);
      // released reservations put stock back on the wall
      stockWallStale();
    },
  });
}

/* --------------------------------- orders --------------------------------- */

export function useOrders() {  return useQuery({
    queryKey: qk.orders(),
    queryFn: () => api<OrderDto[]>("/orders"),
  });
}

export function useOrder(id: string) {
  return useQuery({
    queryKey: qk.order(id),
    queryFn: () => api<OrderDto>(`/orders/${id}`),
  });
}

export function useCancelOrder() {
  return useMutation({
    mutationFn: (id: string) =>
      api<{ ok: true }>(`/orders/${id}/cancel`, { method: "POST", idempotencyKey: idemKey() }),
    onSuccess: (_data, id) => {
      orderLedgerStale(id);
      stockWallStale();
    },
  });
}

/* --------------------------------- account -------------------------------- */

/** §5.2 shipping preset — the bill creates quotes against this address. */
export function useAccountAddress() {
  return useQuery({
    queryKey: qk.address(),
    queryFn: () => api<{ address: ShippingAddressDto | null }>("/account/address"),
  });
}

export function useSaveAddress() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (address: ShippingAddressDto) =>
      api<{ address: ShippingAddressDto }>("/account/address", {
        method: "PUT",
        body: JSON.stringify(address),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.address() }),
  });
}

/* -------------------------------- admin F6 --------------------------------
 * Catalog writes here must invalidate the PUBLIC keys too — the admin console
 * and the storefront are the same database, and the demo's whole point is
 * "edit in one tab, correct in the other". */

function useCatalogInvalidation() {
  return () => catalogWriteStale();
}

export function useAdminProducts(search = "") {
  const authed = useSession((s) => s.user?.role === "Admin");
  return useQuery({
    queryKey: qk.adminProducts(search),
    queryFn: async () =>
      (await api<PagedResult<ProductAdminDto>>(`/admin/products`, { query: { search, pageSize: 100 } })).items,
    enabled: authed,
  });
}

export function useAdminProduct(id: string) {
  return useQuery({
    queryKey: qk.adminProduct(id),
    queryFn: () => api<ProductAdminDto>(`/admin/products/${id}`),
    enabled: Boolean(id),
  });
}

export function useDeleteProduct() {
  const done = useCatalogInvalidation();
  return useMutation({
    mutationFn: (id: string) => api<{ ok: true }>(`/admin/products/${id}`, { method: "DELETE" }),
    onSuccess: done,
  });
}

export function useAdminCategories() {
  return useQuery({ queryKey: qk.adminCategories(), queryFn: () => api<CategoryDto[]>("/admin/categories") });
}

export function useSaveCategory() {
  const done = useCatalogInvalidation();
  return useMutation({
    mutationFn: (c: { id?: string } & CreateCategoryRequest) =>
      c.id
        ? api<CategoryDto>(`/admin/categories/${c.id}`, { method: "PUT", body: JSON.stringify(c satisfies UpdateCategoryRequest) })
        : api<CategoryDto>("/admin/categories", { method: "POST", body: JSON.stringify(c) }),
    onSuccess: done,
  });
}

export function useDeleteCategory() {
  const done = useCatalogInvalidation();
  return useMutation({
    mutationFn: (id: string) => api<{ ok: true }>(`/admin/categories/${id}`, { method: "DELETE" }),
    onSuccess: done,
  });
}

export function useInventory() {
  return useQuery({
    queryKey: qk.adminInventory(),
    queryFn: async () => (await api<PagedResult<InventoryDto>>("/admin/inventory")).items,
  });
}

export function useSetStock() {
  const qc = useQueryClient();
  const done = useCatalogInvalidation();
  return useMutation({
    mutationFn: (v: { productId: string; quantity: number }) =>
      api<InventoryDto>(`/admin/inventory/${v.productId}`, {
        method: "PATCH",
        body: JSON.stringify({ quantity: v.quantity } satisfies { quantity: number }),
      }),
    onSuccess: (data) => {
      done();
      qc.setQueryData<InventoryDto[]>(qk.adminInventory(), (old) =>
        old ? old.map((r) => (r.productId === data.productId ? data : r)) : old
      );
    },
  });
}

export function useAdminOrders(status = "") {
  return useQuery({
    queryKey: qk.adminOrders(status),
    queryFn: async () =>
      (await api<PagedResult<OrderDto>>(`/admin/orders`, { query: { status: status || undefined, pageSize: 100 } })).items,
  });
}

export function useAdminOrder(id: string) {
  return useQuery({
    queryKey: qk.adminOrder(id),
    queryFn: () => api<OrderDto>(`/admin/orders/${id}`),
    enabled: Boolean(id),
  });
}

export function useSetOrderStatus() {
  return useMutation({
    mutationFn: (v: { id: string; status: OrderStatus }) =>
      api<OrderDto>(`/admin/orders/${v.id}/status`, {
        method: "PATCH",
        body: JSON.stringify({ status: v.status } satisfies { status: string }),
        idempotencyKey: idemKey(),
      }),
    onSuccess: (order) => {
      adminLedgerStale(order.id);
      orderLedgerStale(order.id); // customer ledger sees it too
      stockWallStale(); // a Pending settle moves stock
    },
  });
}

/* ------------------------------ admin · AI audit ------------------------------ */

export function useAiLedger() {
  return useQuery({
    queryKey: qk.aiLedger(),
    queryFn: () => api<AiLedgerEntryDto[]>("/admin/ai/ledger"),
    refetchInterval: 5000, // the ledger fills while someone else chats — live-ish view
  });
}

export function useAiConversations() {
  return useQuery({
    queryKey: qk.aiConversations(),
    queryFn: () => api<AiConversationDto[]>("/admin/ai/conversations"),
    refetchInterval: 5000,
  });
}

export function useAiSettings() {
  return useQuery({ queryKey: qk.aiSettings(), queryFn: () => api<AiSettingsDto>("/admin/ai/settings") });
}

export function useSaveAiSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (patch: Partial<AiSettingsDto>) =>
      api<AiSettingsDto>("/admin/ai/settings", { method: "PUT", body: JSON.stringify(patch) }),
    onSuccess: (next) => qc.setQueryData(qk.aiSettings(), next),
  });
}
