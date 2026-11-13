"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { api, idemKey } from "@/infrastructure/api";
import { cartStale, quoteWallsStale, stockWallStale } from "@/infrastructure/query";
import { qk } from "@/infrastructure/query";
import type { OrderDto, QuoteDto, ShippingAddressDto } from "@/infrastructure/api";

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
    refetchInterval: (query) =>
      query.state.data?.order.status === "Pending" ? 10_000 : false,
  });
}

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
      stockWallStale();
    },
  });
}

export function useCancelPayment(sessionToken: string) {
  return useMutation({
    mutationFn: () =>
      api<{ ok: true }>(`/quotes/${sessionToken}/cancel`, {
        method: "POST",
      }),
    onSuccess: () => {
      quoteWallsStale(sessionToken);
      stockWallStale();
    },
  });
}
