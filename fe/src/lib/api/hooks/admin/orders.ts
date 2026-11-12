import { useMutation, useQuery } from "@tanstack/react-query";
import { api, idemKey } from "../../client";
import { qk } from "../../keys";
import {
    adminLedgerStale,
    orderLedgerStale,
    stockWallStale
} from "../../invalidation"
import type {
    OrderDto,
    OrderStatus,
    PagedResult,
    UpdateOrderStatusRequest
} from "../../types"

export function useAdminOrders(status = "") {
    return useQuery({
        queryKey: qk.adminOrders(status),
        queryFn: async () => (
            await api<PagedResult<OrderDto>>("/admin/orders", {
                query: {
                    status: status || undefined,
                    pageSize: 50,
                },  
            })
        ).items,
    })
}

export function useAdminOrder(id: string) {
    return useQuery({
        queryKey: qk.adminOrder(id),
        queryFn: () => api<OrderDto>(`/admin/orders/${id}`),
        enabled: Boolean(id)
    })
}

export function useSetOrderStatus() {
    return useMutation({
        mutationFn: (v: {
            id: string;
            status: OrderStatus;
        }) => 
            api<OrderDto>(`/admin/orders/${v}/status`, {
                method: "PATCH",
                body: JSON.stringify({
                    status: v.status,
                } satisfies UpdateOrderStatusRequest),
                idempotencyKey: idemKey(),
            }),
            onSuccess: (order) => {
                adminLedgerStale(order.id);
                orderLedgerStale(order.id);
                stockWallStale();
            }
    })
}