"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { api, idemKey } from "@/infrastructure/api";
import { qk } from "@/infrastructure/query";
import { orderLedgerStale, stockWallStale } from "@/infrastructure/query";
import type { OrderDto } from "@/infrastructure/api";

export function useOrders() {
  return useQuery({
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
      api<{ ok: true }>(`/orders/${id}/cancel`, {
        method: "POST",
        idempotencyKey: idemKey(),
      }),
    onSuccess: (_data, id) => {
      orderLedgerStale(id);
      stockWallStale();
    },
  });
}
