"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { api, idemKey } from "@/infrastructure/api";
import { cartStale } from "@/infrastructure/query";
import { qk } from "@/infrastructure/query";
import { useSession } from "@/lib/session";
import type { CartDto } from "@/infrastructure/api";

export function useCart() {
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
      api<CartDto>(`/cart/items/${productId}`, {
        method: "DELETE",
      }),
    onSuccess: () => cartStale(),
  });
}
