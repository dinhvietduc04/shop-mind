import { useMutation, useQuery } from "@tanstack/react-query";
import { api } from "../../client";
import { qk } from "../../keys";
import { catalogWriteStale } from "../../invalidation";
import { useSession } from "@/lib/session";
import type { 
    CategoryDto,
    CreateCategoryRequest,
    PagedResult,
    ProductAdminDto,
    UpdateCategoryRequest,
} from "../../types";

export function useAdminProducts(search = "" ) {
    const authed = useSession((s) => s.user?.role === "Admin");

    return useQuery({
        queryKey: qk.adminProducts(search),
        queryFn: async () => (
            await api<PagedResult<ProductAdminDto>>("/admin/products", {
                query: { search, pageSize: 20}, 
            })
        ).items,
        enabled: authed,
    })
}

export function useAdminProduct(id: string ) {
    return useQuery({
        queryKey: qk.adminProducts(id),
        queryFn: () => 
             api<ProductAdminDto>(`/admin/product/${id}`),
        enabled: Boolean(id),
    })
}

export function useDeleteProduct() {
    return useMutation({
        mutationFn: (id: string) => 
            api<{ ok: true }>(`/admin/product/${id}`, {
                method: "DELETE"
            }),
            onSuccess: () => catalogWriteStale(),
    })
}

export function useAdminCategories() {
    return useQuery({
        queryKey: qk.adminCategories(),
        queryFn: () => api<CategoryDto[]>("/admin/categories")
    })
}

export function useSaveCategory() {
  return useMutation({
    mutationFn: (c: { id?: string } & CreateCategoryRequest) =>
      c.id
        ? api<CategoryDto>(`/admin/categories/${c.id}`, {
            method: "PUT",
            body: JSON.stringify(
              c satisfies UpdateCategoryRequest
            ),
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
    mutationFn: (id: string) =>
      api<{ ok: true }>(`/admin/categories/${id}`, {
        method: "DELETE",
      }),
    onSuccess: () => catalogWriteStale(),
  });
}