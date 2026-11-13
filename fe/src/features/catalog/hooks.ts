"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api, productsQueryToParams } from "@/infrastructure/api";
import type { ProductQuery } from "@/lib/api/contracts/schemas";
import { qk } from "@/infrastructure/query";
import type {
  CategoryDto,
  FacetDto,
  PagedResult,
  ProductDetailDto,
  ProductDto,
} from "@/infrastructure/api";

export function useProducts(q: ProductQuery, opts?: { enabled?: boolean }) {
  return useQuery({
    queryKey: qk.products(q),
    queryFn: () =>
      api<PagedResult<ProductDto>>("/products", {
        query: productsQueryToParams(q),
      }),
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
    queryFn: () =>
      api<FacetDto[]>("/products/facets", {
        query: { category },
      }),
  });
}
