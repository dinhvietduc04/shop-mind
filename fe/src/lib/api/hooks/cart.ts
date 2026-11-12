import { useMutation, useQuery } from "@tanstack/react-query";
import { api, idemKey } from "../client"
import { qk } from "../keys"
import { cartStale } from "../invalidation";
import { useSession } from "@/lib/session";
import type { CartDto } from "../types";

export function useCart() {
    const authed = useSession((s) => s.user !== null)

    return useQuery({
        queryKey: qk.cart(),
        queryFn: () => api<CartDto>("/cart"),
        enabled: authed,
    })
}

export function useAddToCart() {
    return useMutation({
        mutationFn: ({
            productId,
            quantity = 1,
        }: {
            productId: string;
            quantity?: number;
        }) =>
            api<CartDto>("/cart/items", {
                method: "POST",
                body: JSON.stringify({ productId, quantity }),
                idempotencyKey: idemKey(),
            }),
        onSuccess: () => cartStale(),
    })
}

export function useUpdateCartItem() {
    return useMutation({
        mutationFn: ({
            productId,
            quantity,
        } : {
            productId: string;
            quantity: number;
        }) =>
            api<CartDto>(`/cart/items/${productId}`,{
                method: "PATCH",
                body: JSON.stringify({ quantity })
            }),
            onSuccess: () => cartStale(),
    })
}

export function useRemoveCartItem() {
    return useMutation({
        mutationFn: (
            productId: string,
        ) => 
            api<CartDto>(`/cart/items/${productId}`, {
                method: "DELETE",
            }),
            onSuccess: () => cartStale(),
    })
}