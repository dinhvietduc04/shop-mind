import { ApiError } from "./types";
import type { ProductQuery } from "@/lib/api/contracts/schemas";
import { whenReady } from "./readyGate";

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

export const hasToken = () => token() !== null;

export function authHeaders(base?: Record<string, string>): Record<string, string> {
  const t = token();
  return { ...(base ?? {}), ...(t ? { Authorization: `Bearer ${t}` } : {}) };
}

export async function api<T>(
  path: string,
  init: RequestInit & { idempotencyKey?: string; query?: Record<string, string | number | boolean | undefined> } = {}
): Promise<T> {
  await whenReady();
  const { idempotencyKey, query, ...rest } = init;
  const qs = query
    ? Object.entries(query)
        .filter(([, v]) => v !== undefined && v !== "")
        .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
    : [];
  const url = `${BASE}${path}${qs.length ? `?${qs.join("&")}` : ""}`;
  const headers = new Headers(rest.headers);
  for (const [name, value] of Object.entries(authHeaders())) headers.set(name, value);
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
    search: q.search,
    category: q.category,
    brand: q.brand,
    minPrice: q.minPrice,
    maxPrice: q.maxPrice,
    inStockOnly: q.inStockOnly,
    specs: q.specs,
    sort: q.sort,
    page: q.page,
    pageSize: q.pageSize,
  };
}

export const idemKey = () => (crypto.randomUUID?.() ?? `k-${Date.now()}-${Math.random().toString(36).slice(2)}`);
