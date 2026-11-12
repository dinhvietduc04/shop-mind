import { useMutation, useQuery } from "@tanstack/react-query";
import { api, idemKey } from "../client";
import { qk } from "../keys";
import {
    orderLedgerStale,
    stockWallStale,
} from "../invalidation"
import type {
    OrderDto,
} from "../types"

export function useOrders() {
    return useQuery({
        queryKey: qk.orders(),
        queryFn: () => api<OrderDto[]>("/orders"),
    })
}

export function useOrder(id: string) {
    return useQuery({
        queryKey: qk.order(id),
        queryFn: () => api<OrderDto>(`/orders/${id}`),
    })
}

export function useCancelOrder() {
    return useMutation({
        mutationFn: (id: string) => api<{ ok: true }>(`/orders/${id}/cancel`, {
            method: "POST",
            idempotencyKey: idemKey()
        }),
        onSuccess: (_data, id) => {
            orderLedgerStale(id);
            stockWallStale();
        },
    })
}