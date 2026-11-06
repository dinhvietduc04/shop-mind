"use client";

import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { api, idemKey, productsQueryToParams } from "./client";
import type {
  CartDto,
  CategoryDto,
  FacetDto,
  OrderDto,
  PagedResult,
  ProductDetailDto,
  ProductDto,
  ProductQuery,
  QuoteDto,
  ShippingAddressDto,
} from "./types";

/* Keys double as the contract map — one place to grep for every endpoint. */
export const qk = {
  products: (q: ProductQuery) => ["products", q] as const,
  product: (id: string) => ["product", id] as const,
  categories: () => ["categories"] as const,
  facets: (category?: string) => ["facets", category] as const,
  cart: () => ["cart"] as const,
  quote: (token: string) => ["quote", token] as const,
  orders: () => ["orders"] as const,
};

export function useProducts(q: ProductQuery) {
  return useQuery({
    queryKey: qk.products(q),
    queryFn: () =>
      api<PagedResult<ProductDto>>(`/products`, { query: productsQueryToParams(q) }),
    placeholderData: keepPreviousData,
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
  return useQuery({
    queryKey: qk.cart(),
    queryFn: () => api<CartDto>("/cart"),
  });
}

const invalidateCart = (qc: ReturnType<typeof useQueryClient>) =>
  qc.invalidateQueries({ queryKey: qk.cart() });

export function useAddToCart() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (productId: string) =>
      api<CartDto>("/cart/items", {
        method: "POST",
        body: JSON.stringify({ productId, quantity: 1 }),
        idempotencyKey: idemKey(),
      }),
    onSuccess: () => invalidateCart(qc),
  });
}

export function useUpdateCartItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ productId, quantity }: { productId: string; quantity: number }) =>
      api<CartDto>(`/cart/items/${productId}`, {
        method: "PATCH",
        body: JSON.stringify({ quantity }),
      }),
    onSuccess: () => invalidateCart(qc),
  });
}

export function useRemoveCartItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (productId: string) =>
      api<CartDto>(`/cart/items/${productId}`, { method: "DELETE" }),
    onSuccess: () => invalidateCart(qc),
  });
}

/* ---------------------------- quote & payment ----------------------------- */

export const DEMO_ADDRESS: ShippingAddressDto = {
  fullName: "Demo Customer",
  phone: "+1-555-0100",
  address: "123 Catalog Lane",
  city: "Vienna",
};

export function useCreateQuote() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (address: ShippingAddressDto) =>
      api<QuoteDto>("/checkout/quote", {
        method: "POST",
        body: JSON.stringify({ shippingAddress: address }),
        idempotencyKey: idemKey(),
      }),
    onSuccess: () => invalidateCart(qc),
  });
}

export function useQuote(sessionToken: string) {
  return useQuery({
    queryKey: qk.quote(sessionToken),
    queryFn: () => api<QuoteDto>(`/quotes/${sessionToken}`),
    refetchInterval: 10_000,
  });
}

export function useConfirmPayment(sessionToken: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      api<{ ok: boolean; order: OrderDto }>(`/quotes/${sessionToken}/confirm`, {
        method: "POST",
        idempotencyKey: idemKey(),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: qk.quote(sessionToken) });
      invalidateCart(qc);
    },
  });
}

export function useCancelPayment(sessionToken: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      api<{ ok: true }>(`/quotes/${sessionToken}/cancel`, { method: "POST" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.quote(sessionToken) }),
  });
}

/* --------------------------------- orders --------------------------------- */

export function useOrders() {
  return useQuery({
    queryKey: qk.orders(),
    queryFn: () => api<OrderDto[]>("/orders"),
  });
}

export function useCancelOrder() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      api<{ ok: true }>(`/orders/${id}/cancel`, { method: "POST", idempotencyKey: idemKey() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.orders() }),
  });
}
