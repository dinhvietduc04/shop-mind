"use client";

import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { api, idemKey } from "@/infrastructure/api";
import { qk } from "@/infrastructure/query";
import { catalogWriteStale, adminLedgerStale, orderLedgerStale, stockWallStale } from "@/infrastructure/query";
import { useSession } from "@/lib/session";
import type {
  AiConversationDto,
  AiLedgerEntryDto,
  AiSettingsDto,
  CategoryDto,
  InventoryDto,
  OrderDto,
  OrderStatus,
  PagedResult,
  ProductAdminDto,
} from "@/infrastructure/api";
import type {
  CreateCategoryRequest,
  UpdateCategoryRequest,
  UpdateInventoryRequest,
  UpdateOrderStatusRequest,
} from "@/lib/api/contracts/schemas";

export function useAdminProducts(search = "") {
  const authed = useSession((s) => s.user?.role === "Admin");

  return useQuery({
    queryKey: qk.adminProducts(search),
    queryFn: async () => (await api<PagedResult<ProductAdminDto>>("/admin/products", { query: { search, pageSize: 20 } })).items,
    enabled: authed,
  });
}

export function useAdminProduct(id: string) {
  return useQuery({
    queryKey: qk.adminProducts(id),
    queryFn: () => api<ProductAdminDto>(`/admin/product/${id}`),
    enabled: Boolean(id),
  });
}

export function useDeleteProduct() {
  return useMutation({
    mutationFn: (id: string) => api<{ ok: true }>(`/admin/product/${id}`, { method: "DELETE" }),
    onSuccess: () => catalogWriteStale(),
  });
}

export function useAdminCategories() {
  return useQuery({
    queryKey: qk.adminCategories(),
    queryFn: () => api<CategoryDto[]>("/admin/categories"),
  });
}

export function useSaveCategory() {
  return useMutation({
    mutationFn: (c: { id?: string } & CreateCategoryRequest) =>
      c.id
        ? api<CategoryDto>(`/admin/categories/${c.id}`, {
            method: "PUT",
            body: JSON.stringify(c satisfies UpdateCategoryRequest),
          })
        : api<CategoryDto>("/admin/categories", {
            method: "POST",
            body: JSON.stringify(c),
          }),
    onSuccess: () => catalogWriteStale(),
  });
}

export function useDeleteCategory() {
  return useMutation({
    mutationFn: (id: string) => api<{ ok: true }>(`/admin/categories/${id}`, { method: "DELETE" }),
    onSuccess: () => catalogWriteStale(),
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

  return useMutation({
    mutationFn: (v: { productId: string; quantity: number }) =>
      api<InventoryDto>(`/admin/inventory/${v.productId}`, {
        method: "PATCH",
        body: JSON.stringify({ quantity: v.quantity } satisfies UpdateInventoryRequest),
      }),
    onSuccess: (data) => {
      catalogWriteStale();
      qc.setQueryData<InventoryDto[]>(qk.adminInventory(), (old) =>
        old ? old.map((r) => (r.productId === data.productId ? data : r)) : old
      );
    },
  });
}

export function useAdminOrders(status = "") {
  return useQuery({
    queryKey: qk.adminOrders(status),
    queryFn: async () => (await api<PagedResult<OrderDto>>("/admin/orders", { query: { status: status || undefined, pageSize: 50 } })).items,
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
        body: JSON.stringify({ status: v.status } satisfies UpdateOrderStatusRequest),
        idempotencyKey: idemKey(),
      }),
    onSuccess: (order) => {
      adminLedgerStale(order.id);
      orderLedgerStale(order.id);
      stockWallStale();
    },
  });
}

export function useAiLedger() {
  return useQuery({
    queryKey: qk.aiLedger(),
    queryFn: () => api<AiLedgerEntryDto[]>("/admin/ai/ledger"),
    refetchInterval: 5000,
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
  return useQuery({
    queryKey: qk.aiSettings(),
    queryFn: () => api<AiSettingsDto>("/admin/ai/settings"),
  });
}

export function useSaveAiSettings() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (patch: Partial<AiSettingsDto>) =>
      api<AiSettingsDto>("/admin/ai/settings", {
        method: "PUT",
        body: JSON.stringify(patch),
      }),
    onSuccess: (next) => qc.setQueryData(qk.aiSettings(), next),
  });
}
