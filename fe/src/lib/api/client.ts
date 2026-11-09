import { ApiError, type ProductQuery } from "./types";
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

/** Has a session token at all — restore() probes with this instead of
 *  touching storage itself (this module is the ONLY reader/writer). */
export const hasToken = () => token() !== null;

/**
 * The ONLY builder of the Authorization header. `api()` uses it for REST;
 * raw-fetch callers (the chat transport needs the Response body stream) spread
 * this record instead of hand-assembling `Bearer ${localStorage...}` — that
 * duplication is how a token-store change becomes a three-module hunt.
 */
export function authHeaders(base?: Record<string, string>): Record<string, string> {
  const t = token();
  return { ...(base ?? {}), ...(t ? { Authorization: `Bearer ${t}` } : {}) };
}

/**
 * F8 swap note: everything the UI knows about the network lives here and in
 * types.ts. Point NEXT_PUBLIC_API_URL at Shop.Api, drop MSW, done.
 */
export async function api<T>(
  path: string,
  init: RequestInit & { idempotencyKey?: string; query?: Record<string, string | number | boolean | undefined> } = {}
): Promise<T> {
  /* Mock boot decoupling: render does not wait for MSW — requests do.
     Resolved immediately outside mock mode (zero cost against Shop.Api). */
  await whenReady();
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
    specs: q.specs, sort: q.sort, page: q.page, pageSize: q.pageSize,
  };
}

/** UUID for Idempotency-Key (backend plan B2: writes carry a key). */
export const idemKey = () =>
  (crypto.randomUUID?.() ?? `k-${Date.now()}-${Math.random().toString(36).slice(2)}`);
