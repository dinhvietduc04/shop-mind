import { delay, HttpResponse, http } from "msw";
import {
  FIXTURE_CATEGORIES,
  FIXTURE_PRODUCTS,
  MOCK_USERS,
  type FixtureProduct,
} from "./fixtures";
import {
  available,
  cancelOrder,
  cartAdd,
  cartClear,
  cartRemove,
  cartUpdate,
  createQuote,
  getCart,
  getQuote,
  liveProduct,
  ordersFor,
  settleQuote,
  userByToken,
} from "./db";
import type { PagedResult, Specs } from "@/lib/api/types";

/**
 * MSW handlers mirroring Shop.Api routes 1:1 (docs/PLAN.md + backend plan B5
 * quote endpoints, A3 facets). NEXT_PUBLIC_MOCK_MODE: slow | ratelimit |
 * paymentfail demo the failure paths without a backend.
 */

const MODE = process.env.NEXT_PUBLIC_MOCK_MODE ?? "";
const BASE = "/api";

async function shaped(kind: "" | "slow" | "fail") {
  if (kind === "slow" || MODE === "slow") await delay(650);
  if (kind === "fail") await delay(200);
}

function problems(title: string, errors: Record<string, string[]>, status = 400) {
  return HttpResponse.json({ title, status, errors }, { status });
}

function requireAuth(request: Request) {
  return userByToken(request.headers.get("authorization"));
}

let readCount = 0;

function maybeRateLimited() {
  if (MODE === "ratelimit" && ++readCount % 3 === 0) {
    return HttpResponse.json(
      { title: "Too many requests", status: 429 },
      { status: 429, headers: { "Retry-After": "60" } }
    );
  }
  return null;
}

/** spec filter for facets/agent search: q=ram_gb>=16,storage_gb<=512 */
function matchSpecs(specs: Specs | undefined, raw: string | null): boolean {
  if (!raw) return true;
  return raw.split(",").every((clause) => {
    const m = clause.trim().match(/^([a-z0-9_]+)\s*(>=|<=|==|!=)\s*(.+)$/i);
    if (!m) return true;
    const [, key, op, valueRaw] = m;
    const actual = specs?.[key];
    const value: number | string | boolean =
      valueRaw === "true" ? true : valueRaw === "false" ? false : isNaN(Number(valueRaw)) ? valueRaw : Number(valueRaw);
    if (typeof actual !== typeof value) return false;
    switch (op) {
      case ">=": return (actual as number) >= (value as number);
      case "<=": return (actual as number) <= (value as number);
      case "==": return String(actual).toLowerCase() === String(value).toLowerCase();
      case "!=": return String(actual).toLowerCase() !== String(value).toLowerCase();
      default: return true;
    }
  });
}

export const handlers = [
  /* ------------------------------- auth ------------------------------- */
  http.post(`${BASE}/auth/login`, async ({ request }) => {
    await shaped("");
    const body = (await request.json().catch(() => ({}))) as { email?: string; password?: string };
    const rec = body.email ? MOCK_USERS[body.email] : undefined;
    if (!rec || rec.password !== body.password)
      return problems("Invalid email or password.", { "": ["Invalid credentials."] }, 400);
    return HttpResponse.json({ token: `mock.${rec.user.email}`, user: rec.user });
  }),

  http.get(`${BASE}/auth/me`, ({ request }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    return HttpResponse.json(MOCK_USERS[u.email].user);
  }),

  /* ----------------------------- catalog ------------------------------ */
  http.get(`${BASE}/categories`, async () => {
    await shaped("slow");
    return HttpResponse.json(FIXTURE_CATEGORIES, { headers: { "X-Cache": "HIT" } });
  }),

  http.get(`${BASE}/products/facets`, async ({ request }) => {
    await shaped("slow");
    const url = new URL(request.url);
    const categorySlug = url.searchParams.get("category");
    const pool = FIXTURE_PRODUCTS.filter((p) => !categorySlug || p.categoryName?.toLowerCase() === categorySlug.toLowerCase() || p.categoryId === `c-${categorySlug}`);
    const keys = new Map<string, (string | number)[]>();
    for (const p of pool)
      for (const [k, v] of Object.entries(p.specs ?? {})) {
        if (typeof v === "boolean") continue;
        const list = keys.get(k) ?? [];
        if (!list.includes(v as string | number)) list.push(v as string | number);
        keys.set(k, list);
      }
    const facets = [...keys.entries()].map(([key, values]) => {
      const numeric = values.every((v) => typeof v === "number");
      return {
        key,
        label: key.replace(/_/g, " "),
        type: numeric ? ("range" as const) : ("enum" as const),
        values: numeric ? undefined : values,
        min: numeric ? Math.min(...(values as number[])) : undefined,
        max: numeric ? Math.max(...(values as number[])) : undefined,
        count: values.length,
      };
    });
    return HttpResponse.json(facets, { headers: { "X-Cache": "MISS" } });
  }),

  http.get(`${BASE}/products`, async ({ request }) => {
    await shaped("slow");
    const rl = maybeRateLimited();
    if (rl) return rl;
    const url = new URL(request.url);
    const sp = url.searchParams;
    const search = (sp.get("search") ?? "").toLowerCase();
    const category = sp.get("category");
    const brand = sp.get("brand");
    const minPrice = sp.get("minPrice");
    const maxPrice = sp.get("maxPrice");
    const inStockOnly = sp.get("inStockOnly") === "true";
    const specFilter = sp.get("specs");
    const sort = sp.get("sort");
    const page = Math.max(1, Number(sp.get("page") ?? 1));
    const pageSize = Math.min(100, Math.max(1, Number(sp.get("pageSize") ?? 20)));

    const items = FIXTURE_PRODUCTS.map(liveProduct).filter((p) => {
      if (category && p.categoryName?.toLowerCase() !== category.toLowerCase() && p.categoryId !== category && !p.slug.startsWith(`${category}-`)) return false;
      if (brand && p.brand?.toLowerCase() !== brand.toLowerCase()) return false;
      if (minPrice && p.price < Number(minPrice)) return false;
      if (maxPrice && p.price > Number(maxPrice)) return false;
      if (inStockOnly && available(p.id) <= 0) return false;
      if (specFilter && !matchSpecs(p.specs, specFilter)) return false;
      if (search) {
        const hay = `${p.name} ${p.brand ?? ""} ${p.sku} ${p.categoryName ?? ""} ${p.description}`.toLowerCase();
        const tokens = search.split(/\s+/).filter((t) => t.length > 2);
        if (tokens.some((t) => !hay.includes(t))) return false;
      }
      return true;
    });

    switch (sort) {
      case "price_asc": items.sort((a, b) => a.price - b.price); break;
      case "price_desc": items.sort((a, b) => b.price - a.price); break;
      case "name_asc": items.sort((a, b) => a.name.localeCompare(b.name)); break;
      case "name_desc": items.sort((a, b) => b.name.localeCompare(a.name)); break;
      case "newest": default: items.sort((a, b) => b.createdAt.localeCompare(a.createdAt)); break;
    }

    const result: PagedResult<(typeof items)[number]> = {
      items: items.slice((page - 1) * pageSize, page * pageSize),
      page,
      pageSize,
      totalCount: items.length,
      totalPages: Math.ceil(items.length / pageSize),
    };
    return HttpResponse.json(result, { headers: { "X-Cache": "MISS" } });
  }),

  http.get(`${BASE}/products/:id`, async ({ params }) => {
    await shaped("slow");
    const p = FIXTURE_PRODUCTS.find((x) => x.id === params.id || x.slug === params.id);
    if (!p) return problems("Not found.", {}, 404);
    const dto: Partial<FixtureProduct> = { ...liveProduct(p) };
    delete dto.quantity; // internal row state never leaves the API
    return HttpResponse.json(dto, { headers: { "X-Cache": "MISS" } });
  }),

  /* ------------------------------- cart ------------------------------- */
  http.get(`${BASE}/cart`, ({ request }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const items = getCart(u.id);
    const subtotal = +items.reduce((t, i) => t + i.subtotal, 0).toFixed(2);
    return HttpResponse.json({
      id: `cart-${u.id}`, items, subtotal,
      totalQuantity: items.reduce((t, i) => t + i.quantity, 0),
    });
  }),

  http.post(`${BASE}/cart/items`, async ({ request }) => {
    await shaped("");
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const body = (await request.json().catch(() => null)) as { productId?: string; quantity?: number } | null;
    if (!body?.productId || !body.quantity || body.quantity <= 0)
      return problems("Validation failed.", { Quantity: ["Quantity must be greater than zero."] });
    const r = cartAdd(u.id, body.productId, body.quantity);
    if (!r.ok) return problems(r.error, {}, 409);
    const subtotal = +r.items.reduce((t, i) => t + i.subtotal, 0).toFixed(2);
    return HttpResponse.json({ id: `cart-${u.id}`, items: r.items, subtotal, totalQuantity: r.items.reduce((t, i) => t + i.quantity, 0) });
  }),

  http.patch(`${BASE}/cart/items/:productId`, async ({ request, params }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const body = (await request.json().catch(() => null)) as { quantity?: number } | null;
    if (!body?.quantity || body.quantity <= 0)
      return problems("Validation failed.", { Quantity: ["Quantity must be greater than zero."] });
    const r = cartUpdate(u.id, params.productId as string, body.quantity);
    if (!r.ok) return problems(r.error, {}, 409);
    return HttpResponse.json({ id: `cart-${u.id}`, items: r.items, subtotal: +r.items.reduce((t, i) => t + i.subtotal, 0).toFixed(2), totalQuantity: r.items.reduce((t, i) => t + i.quantity, 0) });
  }),

  http.delete(`${BASE}/cart/items/:productId`, ({ request, params }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const items = cartRemove(u.id, params.productId as string);
    return HttpResponse.json({ id: `cart-${u.id}`, items, subtotal: +items.reduce((t, i) => t + i.subtotal, 0).toFixed(2), totalQuantity: items.reduce((t, i) => t + i.quantity, 0) });
  }),

  http.delete(`${BASE}/cart`, ({ request }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    cartClear(u.id);
    return HttpResponse.json({ id: `cart-${u.id}`, items: [], subtotal: 0, totalQuantity: 0 });
  }),

  /* ------------------------------ quote ------------------------------- */
  http.post(`${BASE}/checkout/quote`, async ({ request }) => {
    await shaped("");
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const body = (await request.json().catch(() => null)) as { shippingAddress?: { fullName?: string; phone?: string; address?: string; city?: string } } | null;
    const a = body?.shippingAddress;
    if (!a?.fullName || !a.address || !a.city || !a.phone)
      return problems("Validation failed.", { ShippingAddress: ["Full name, phone, address and city are required."] });
    const r = createQuote(u.id, { fullName: a.fullName, phone: a.phone, address: a.address, city: a.city });
    if (!r.ok) return problems(r.error, {}, 409);
    return HttpResponse.json(r.quote, { status: 201 });
  }),

  http.get(`${BASE}/quotes/:token`, ({ params }) => {
    const q = getQuote(params.token as string);
    if (!q) return problems("Quote not found or expired.", {}, 404);
    return HttpResponse.json({ order: q.order, hostedPayUrl: q.hostedPayUrl, expiresAt: q.expiresAt });
  }),

  http.post(`${BASE}/quotes/:token/confirm`, async ({ params }) => {
    await shaped("");
    const q = getQuote(params.token as string);
    if (!q) return problems("Quote not found or expired.", {}, 410);
    const failed = MODE === "paymentfail";
    const applied = settleQuote(params.token as string, failed ? "PaymentFailed" : "Paid", failed ? "Card declined (mock)." : undefined);
    if (!applied) return HttpResponse.json({ ok: true, replayed: true, order: q.order }); // idempotent replay
    return HttpResponse.json({ ok: !failed, order: q.order });
  }),

  http.post(`${BASE}/quotes/:token/cancel`, ({ params }) => {
    const q = getQuote(params.token as string);
    if (!q) return problems("Quote not found or expired.", {}, 410);
    settleQuote(params.token as string, "Cancelled", "Abandoned by user.");
    return HttpResponse.json({ ok: true, order: q.order });
  }),

  /* ------------------------------ orders ------------------------------ */
  http.get(`${BASE}/orders`, ({ request }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    return HttpResponse.json(ordersFor(u.id));
  }),

  http.post(`${BASE}/orders/:id/cancel`, ({ request, params }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const r = cancelOrder(params.id as string, u.id);
    if (!r.ok) return problems(r.error, {}, 409);
    return HttpResponse.json({ ok: true });
  }),

  /* dev helper: wipe mock server state (localStorage-backed) between demos */
  http.post(`${BASE}/_mock/reset`, async () => {
    const { resetDb } = await import("./db");
    resetDb();
    return HttpResponse.json({ ok: true });
  }),
];
