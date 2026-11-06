import { ApiError, type ProductQuery } from "./types";

const BASE = "/api";

function token(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem("shopmind.token");
}

export function setToken(t: string | null) {
  if (typeof window === "undefined") return;
  if (t) localStorage.setItem("shopmind.token", t);
  else localStorage.removeItem("shopmind.token");
}

/**
 * F8 swap note: everything the UI knows about the network lives here and in
 * types.ts. Point NEXT_PUBLIC_API_URL at Shop.Api, drop MSW, done.
 */
export async function api<T>(
  path: string,
  init: RequestInit & { idempotencyKey?: string; query?: Record<string, string | number | boolean | undefined> } = {}
): Promise<T> {
  const { idempotencyKey, query, ...rest } = init;
  const qs = query
    ? Object.entries(query)
        .filter(([, v]) => v !== undefined && v !== "")
        .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
    : [];
  const url = `${BASE}${path}${qs.length ? `?${qs.join("&")}` : ""}`;
  const headers = new Headers(rest.headers);
  const t = token();
  if (t) headers.set("Authorization", `Bearer ${t}`);
  if (rest.body) headers.set("Content-Type", "application/json");
  if (idempotencyKey) headers.set("Idempotency-Key", idempotencyKey);

  const res = await fetch(url, { ...rest, headers });
  if (res.status === 204) return undefined as T;
  const body = await res.json().catch(() => null);
  if (!res.ok) {
    const retryAfter = Number(res.headers.get("Retry-After") ?? 0) || undefined;
    throw new ApiError(res.status, body?.title ?? "Request failed.", body?.errors, retryAfter);
  }
  return body as T;
}

export function productsQueryToParams(q: ProductQuery): Record<string, string | number | boolean | undefined> {
  return {
    search: q.search, category: q.category, brand: q.brand,
    minPrice: q.minPrice, maxPrice: q.maxPrice, inStockOnly: q.inStockOnly,
    sort: q.sort, page: q.page, pageSize: q.pageSize,
  };
}

/** UUID for Idempotency-Key (backend plan B2: writes carry a key). */
export const idemKey = () =>
  (crypto.randomUUID?.() ?? `k-${Date.now()}-${Math.random().toString(36).slice(2)}`);
