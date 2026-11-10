import { delay, HttpResponse, http } from "msw";
import { LLM_TURN, ledgerWeight } from "@/lib/tools/registry";
import { ORDER_STATUSES } from "@/lib/orders/lifecycle";
import { decodeSpecs, matchesSpecs, type SpecClause } from "@/lib/attributes/codec";
import { facetPool, specLabel } from "@/lib/attributes/registry";
import { type FixtureProduct } from "./fixtures";
import {
  activeProducts,
  adminCreateCategory,
  adminCreateProduct,
  adminDeleteCategory,
  adminDeleteProduct,
  adminListOrders,
  adminSetOrderStatus,
  adminSetStock,
  adminUpdateCategory,
  adminUpdateProduct,
  aiConversationsList,
  aiKillSwitchOn,
  aiLedgerList,
  activeQuoteFor,
  allCategories,
  allProducts,
  available,
  cancelOrder,
  cartAdd,
  cartClear,
  cartHolds,
  cartRemove,
  cartUpdate,
  createQuote,
  expirePendingQuotes,
  findProduct,
  findUserRecord,
  getAddress,
  getAiSettings,
  getCart,
  getQuote,
  inventoryFor,
  liveProduct,
  openQuoteHolds,
  orderFor,
  ordersFor,
  persist,
  recordAiCalls,
  registerUser,
  setAiSettings,
  setAddress,
  settleQuote,
  userByToken,
} from "./db";
import type {
  AgentTurnResult,
  ChatSseFrame,
  CreateProductRequest,
  InventoryDto,
  OrderDto,
  OrderStatus,
  PagedResult,
  ShippingAddressDto,
  Specs,
  UpdateProductRequest,
} from "@/lib/api/types";
import { runAgentTurn } from "./agent";

type ShippingAddressBody = Partial<ShippingAddressDto> | null;

/** Requests only count toward the chat bucket after auth+mode checks pass. */
let chatCount = 0;

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

/** [Authorize(Roles = "Admin")] mirror: signed-in AND admin, else 403 — the
 *  mock's admin surface must fail exactly like Shop.Api so F8 swaps clean. */
function requireAdmin(request: Request) {
  const u = requireAuth(request);
  return u?.admin ? u : null;
}

function adminOnly(request: Request) {
  return requireAdmin(request) ? null : problems("Admin role required.", {}, 403);
}

/* §A7 audit stand-in: every chat turn leaves one llm_turn row plus one row per
   tool in the trail. Durations are deterministic weights from the tool
   REGISTRY (a mock that rolls random latencies teaches nothing); tokens ≈
   chars/4 over the turn's prose. */
function ledgerRows(message: string, result: AgentTurnResult, wallMs: number) {
  const ts = new Date().toISOString();
  const limited = Boolean(result.limitReached);
  const rows: Parameters<typeof recordAiCalls>[1] = [
    {
      ts, tool: LLM_TURN,
      status: limited ? "limited" : "ok",
      durationMs: wallMs + (result.text.length % 3) * 5,
      tokens: Math.ceil((message.length + result.text.length) / 4),
    },
  ];
  for (const t of result.toolTrail) {
    const failed = /rejected|disabled/i.test(t.detail ?? "");
    rows.push({
      ts, tool: t.tool,
      status: failed ? "error" : limited ? "limited" : "ok",
      durationMs: ledgerWeight(t.tool),
      tokens: 0,
    });
  }
  return rows;
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

export const handlers = [
  /* ------------------------------- auth ------------------------------- */
  http.post(`${BASE}/auth/login`, async ({ request }) => {
    await shaped("");
    const body = (await request.json().catch(() => ({}))) as { email?: string; password?: string };
    const rec = body.email ? findUserRecord(body.email.toLowerCase().trim()) : undefined;
    if (!rec || rec.password !== body.password)
      return problems("Invalid email or password.", { "": ["Invalid credentials."] }, 400);
    return HttpResponse.json({ token: `mock.${rec.user.email}`, user: rec.user });
  }),

  /* RegisterRequest mirror of the real endpoint: email/password/name/phone?,
     FluentValidation-shaped error map, 409 on duplicate. */
  http.post(`${BASE}/auth/register`, async ({ request }) => {
    await shaped("");
    const body = (await request.json().catch(() => ({}))) as {
      email?: string; password?: string; firstName?: string; lastName?: string; phoneNumber?: string | null;
    };
    const errs: Record<string, string[]> = {};
    if (!body.email || !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(body.email)) errs.Email = ["Enter a valid email address."];
    if (!body.password || body.password.length < 8) errs.Password = ["Password must be at least 8 characters."];
    if (!body.firstName?.trim()) errs.FirstName = ["First name is required."];
    if (!body.lastName?.trim()) errs.LastName = ["Last name is required."];
    if (Object.keys(errs).length > 0) return problems("Validation failed.", errs);
    const r = registerUser({
      email: body.email!, password: body.password!,
      firstName: body.firstName!, lastName: body.lastName!, phoneNumber: body.phoneNumber ?? null,
    });
    if (!r.ok) return problems(r.error, { Email: [r.error] }, 409);
    persist();
    return HttpResponse.json({ token: `mock.${r.rec.user.email}`, user: r.rec.user });
  }),

  http.get(`${BASE}/auth/me`, ({ request }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    return HttpResponse.json(findUserRecord(u.email)!.user);
  }),

  /* ------------------------- account (§5.2, mock-only) ------------------------- */
  http.get(`${BASE}/account/address`, ({ request }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    return HttpResponse.json({ address: getAddress(u.id) });
  }),

  http.put(`${BASE}/account/address`, async ({ request }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const body = (await request.json().catch(() => null)) as ShippingAddressBody | null;
    const errs: Record<string, string[]> = {};
    if (!body?.fullName?.trim()) errs.FullName = ["Required."];
    if (!body?.phone?.trim()) errs.Phone = ["Required."];
    if (!body?.address?.trim()) errs.Address = ["Required."];
    if (!body?.city?.trim()) errs.City = ["Required."];
    if (Object.keys(errs).length > 0 || !body) return problems("Validation failed.", errs);
    const saved: ShippingAddressDto = {
      fullName: body.fullName!.trim(), phone: body.phone!.trim(),
      address: body.address!.trim(), city: body.city!.trim(),
    };
    setAddress(u.id, saved);
    persist();
    return HttpResponse.json({ address: saved });
  }),

  /* ----------------------------- catalog ------------------------------ */
  http.get(`${BASE}/categories`, async () => {
    await shaped("slow");
    return HttpResponse.json(allCategories().filter((c) => c.isActive), { headers: { "X-Cache": "HIT" } });
  }),

  http.get(`${BASE}/products/facets`, async ({ request }) => {
    await shaped("slow");
    const url = new URL(request.url);
    const categorySlug = url.searchParams.get("category");
    const pool = activeProducts().filter((p) => !categorySlug || p.categoryName?.toLowerCase() === categorySlug.toLowerCase() || p.categoryId === `c-${categorySlug}`);
    /* registry ∩ populated data — the ONE scan (lib/attributes), shared with
       the assistant's Elicitation (C8). Unregistered keys do not exist here. */
    const facets = facetPool(pool).map((e) => ({
      key: e.def.key,
      label: specLabel(e.def.key),
      type: e.numeric ? ("range" as const) : ("enum" as const),
      unit: e.def.unit,
      values: e.numeric ? undefined : e.values,
      min: e.numeric ? e.min : undefined,
      max: e.numeric ? e.max : undefined,
      count: e.values.length,
    }));
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
    /* ONE grammar, fail-closed (§B5+ 4 / C8): a clause we can't read is a
       400, never a silently-ignored filter. */
    const specDecode = decodeSpecs(sp.get("specs") ?? "");
    if (!specDecode.ok)
      return problems(`Invalid spec filter: ${specDecode.error}`, { specs: [specDecode.error] }, 400);
    const specClauses: SpecClause[] = specDecode.clauses;
    const sort = sp.get("sort");
    const page = Math.max(1, Number(sp.get("page") ?? 1));
    const pageSize = Math.min(100, Math.max(1, Number(sp.get("pageSize") ?? 20)));

    const items = activeProducts().map(liveProduct).filter((p) => {
      if (category && p.categoryName?.toLowerCase() !== category.toLowerCase() && p.categoryId !== category && !p.slug.startsWith(`${category}-`)) return false;
      if (brand && p.brand?.toLowerCase() !== brand.toLowerCase()) return false;
      if (minPrice && p.price < Number(minPrice)) return false;
      if (maxPrice && p.price > Number(maxPrice)) return false;
      if (inStockOnly && available(p.id) <= 0) return false;
      if (specClauses.length && !matchesSpecs(p.specs, specClauses)) return false;
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
    const p = findProduct(params.id as string);
    if (!p || p.status !== "Active") return problems("Not found.", {}, 404); // drafts are admin-visible only
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
    persist();
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
    persist();
    return HttpResponse.json({ id: `cart-${u.id}`, items: r.items, subtotal: +r.items.reduce((t, i) => t + i.subtotal, 0).toFixed(2), totalQuantity: r.items.reduce((t, i) => t + i.quantity, 0) });
  }),

  http.delete(`${BASE}/cart/items/:productId`, ({ request, params }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const items = cartRemove(u.id, params.productId as string);
    persist();
    return HttpResponse.json({ id: `cart-${u.id}`, items, subtotal: +items.reduce((t, i) => t + i.subtotal, 0).toFixed(2), totalQuantity: items.reduce((t, i) => t + i.quantity, 0) });
  }),

  http.delete(`${BASE}/cart`, ({ request }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    cartClear(u.id);
    persist();
    return HttpResponse.json({ id: `cart-${u.id}`, items: [], subtotal: 0, totalQuantity: 0 });
  }),

  /* ------------------------------ quote ------------------------------- */
  http.get(`${BASE}/checkout/quote`, ({ request }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const active = activeQuoteFor(u.id);
    // a lazy expiry settle may have just run — persist it at the seam
    persist();
    if (!active) return problems("No open quote.", {}, 404);
    // sessionToken travels as its own DTO field (§B5+ 2) — never URL archaeology
    return HttpResponse.json(active.quote);
  }),

  http.post(`${BASE}/checkout/quote`, async ({ request }) => {
    await shaped("");
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const body = (await request.json().catch(() => null)) as { shippingAddress?: { fullName?: string; phone?: string; address?: string; city?: string } } | null;
    const a = body?.shippingAddress;
    if (!a?.fullName || !a.address || !a.city || !a.phone)
      return problems("Validation failed.", { ShippingAddress: ["Full name, phone, address and city are required."] });
    const r = createQuote(u.id, { fullName: a.fullName, phone: a.phone, address: a.address, city: a.city });
    persist();
    if (!r.ok) return problems(r.error, {}, 409);
    // DTO-only response: internal userId/origin never leave the API
    return HttpResponse.json(
      { sessionToken: r.sessionToken, order: r.quote.order, hostedPayUrl: r.quote.hostedPayUrl, expiresAt: r.quote.expiresAt },
      { status: 201 }
    );
  }),

  http.get(`${BASE}/quotes/:token`, ({ params }) => {
    const token = params.token as string;
    const q = getQuote(token);
    persist(); // lazy expiry may settle on read (sweeper semantics)
    if (!q) return problems("Quote not found or expired.", {}, 404);
    return HttpResponse.json({ sessionToken: token, order: q.order, hostedPayUrl: q.hostedPayUrl, expiresAt: q.expiresAt });
  }),

  http.post(`${BASE}/quotes/:token/confirm`, async ({ params }) => {
    await shaped("");
    const q = getQuote(params.token as string);
    if (!q) return problems("Quote not found or expired.", {}, 410);
    const failed = MODE === "paymentfail";
    const applied = settleQuote(params.token as string, failed ? "PaymentFailed" : "Paid", failed ? "Card declined (mock)." : undefined);
    persist();
    if (!applied) return HttpResponse.json({ ok: true, replayed: true, order: q.order }); // idempotent replay
    return HttpResponse.json({ ok: !failed, order: q.order });
  }),

  http.post(`${BASE}/quotes/:token/cancel`, ({ params }) => {
    const q = getQuote(params.token as string);
    if (!q) return problems("Quote not found or expired.", {}, 410);
    settleQuote(params.token as string, "Cancelled", "Abandoned by user.");
    persist();
    return HttpResponse.json({ ok: true, order: q.order });
  }),

  /* ------------------------------ orders ------------------------------ */
  http.get(`${BASE}/orders`, ({ request }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    return HttpResponse.json(ordersFor(u.id));
  }),

  http.get(`${BASE}/orders/:id`, ({ request, params }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const o = orderFor(params.id as string, u.id);
    persist(); // orderFor reads through the lazy-settling quote view
    if (!o) return problems("Order not found.", {}, 404);
    return HttpResponse.json(o);
  }),

  http.post(`${BASE}/orders/:id/cancel`, ({ request, params }) => {
    const u = requireAuth(request);
    if (!u) return problems("Unauthorized.", {}, 401);
    const r = cancelOrder(params.id as string, u.id);
    persist();
    if (!r.ok) return problems(r.error, {}, 409);
    return HttpResponse.json({ ok: true });
  }),

  /* ------------------------------ admin (F6) ------------------------------
   * Mirrors AdminControllers.cs 1:1 — same routes, same role gate, same
   * FluentValidation-style problem maps — so the F8 cutover is a no-op. */
  http.get(`${BASE}/admin/products`, async ({ request }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const url = new URL(request.url);
    const search = (url.searchParams.get("search") ?? "").toLowerCase();
    const page = Math.max(1, Number(url.searchParams.get("page") ?? 1));
    const pageSize = Math.min(100, Math.max(1, Number(url.searchParams.get("pageSize") ?? 20)));
    let rows = allProducts().map((p) => ({ ...p, availableStock: available(p.id) }));
    if (search)
      rows = rows.filter((p) => `${p.name} ${p.sku} ${p.brand ?? ""}`.toLowerCase().includes(search));
    rows.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
    return HttpResponse.json({
      items: rows.slice((page - 1) * pageSize, page * pageSize),
      page, pageSize, totalCount: rows.length, totalPages: Math.ceil(rows.length / pageSize),
    } satisfies PagedResult<FixtureProduct>);
  }),

  http.post(`${BASE}/admin/products`, async ({ request }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const body = (await request.json().catch(() => null)) as (CreateProductRequest & { specs?: Specs }) | null;
    if (!body) return problems("Validation failed.", { "": ["Request body required."] });
    const r = adminCreateProduct(body);
    if (!r.ok) return problems(r.title ?? "Validation failed.", r.errors, r.status ?? 400);
    persist();
    return HttpResponse.json(r.value, { status: 201 });
  }),

  http.get(`${BASE}/admin/products/:id`, ({ request, params }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const p = allProducts().find((x) => x.id === params.id);
    if (!p) return problems("Not found.", {}, 404);
    return HttpResponse.json({ ...p, availableStock: available(p.id) });
  }),

  http.put(`${BASE}/admin/products/:id`, async ({ request, params }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const body = (await request.json().catch(() => null)) as (UpdateProductRequest & { specs?: Specs }) | null;
    if (!body) return problems("Validation failed.", { "": ["Request body required."] });
    const r = adminUpdateProduct(params.id as string, body);
    if (!r.ok) return problems(r.title ?? "Validation failed.", r.errors, r.status ?? (r.errors[""]?.length ? 404 : 400));
    persist();
    return HttpResponse.json(r.value);
  }),

  http.delete(`${BASE}/admin/products/:id`, ({ request, params }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    /* FK refuses (C7): the guard knowledge stays in each domain; the seam
       composes it — same messages the F6 gate verified. */
    if (cartHolds(params.id as string))
      return problems("This product is in a customer's cart — set it Inactive instead.", {}, 409);
    if (openQuoteHolds(params.id as string))
      return problems("An open quote holds this product — settle the quote first.", {}, 409);
    const r = adminDeleteProduct(params.id as string);
    if (!r.ok) return problems(r.error, {}, r.error.includes("not found") ? 404 : 409);
    persist();
    return new HttpResponse(null, { status: 204 });
  }),

  http.get(`${BASE}/admin/categories`, ({ request }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    return HttpResponse.json(allCategories());
  }),

  http.post(`${BASE}/admin/categories`, async ({ request }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const body = (await request.json().catch(() => null)) as { name?: string; slug?: string | null; description?: string | null; isActive?: boolean } | null;
    if (!body?.name?.trim()) return problems("Validation failed.", { Name: ["Name is required."] });
    const r = adminCreateCategory({ name: body.name, slug: body.slug, description: body.description, isActive: body.isActive ?? true });
    if (!r.ok) return problems(r.error, r.field ? { [r.field]: [r.error] } : {});
    persist();
    return HttpResponse.json(r.value, { status: 201 });
  }),

  http.put(`${BASE}/admin/categories/:id`, async ({ request, params }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const body = (await request.json().catch(() => null)) as { name?: string; slug?: string | null; description?: string | null; isActive?: boolean } | null;
    if (!body?.name?.trim()) return problems("Validation failed.", { Name: ["Name is required."] });
    const r = adminUpdateCategory(params.id as string, { name: body.name, slug: body.slug, description: body.description, isActive: body.isActive ?? true });
    if (!r.ok) return problems(r.error, r.field ? { [r.field]: [r.error] } : {}, r.error === "Category not found." ? 404 : 400);
    persist();
    return HttpResponse.json(r.value);
  }),

  http.delete(`${BASE}/admin/categories/:id`, ({ request, params }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const r = adminDeleteCategory(params.id as string);
    if (!r.ok) return problems(r.error, {}, r.error === "Category not found." ? 404 : 409);
    persist();
    return new HttpResponse(null, { status: 204 });
  }),

  http.get(`${BASE}/admin/inventory`, ({ request }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const url = new URL(request.url);
    const search = (url.searchParams.get("search") ?? "").toLowerCase();
    const rows: InventoryDto[] = allProducts()
      .map((p) => inventoryFor(p.id)!)
      .filter((r) => !search || `${r.productName} ${r.sku}`.toLowerCase().includes(search));
    return HttpResponse.json({
      items: rows, page: 1, pageSize: rows.length, totalCount: rows.length, totalPages: 1,
    } satisfies PagedResult<InventoryDto>);
  }),

  http.patch(`${BASE}/admin/inventory/:productId`, async ({ request, params }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const body = (await request.json().catch(() => null)) as { quantity?: number } | null;
    if (body?.quantity == null) return problems("Validation failed.", { Quantity: ["quantity is required."] });
    const r = adminSetStock(params.productId as string, body.quantity);
    if (!r.ok) return problems(r.error, { Quantity: [r.error] }, r.error === "Product not found." ? 404 : 400);
    persist();
    return HttpResponse.json(inventoryFor(params.productId as string));
  }),

  http.get(`${BASE}/admin/orders`, ({ request }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const url = new URL(request.url);
    const status = url.searchParams.get("status");
    const page = Math.max(1, Number(url.searchParams.get("page") ?? 1));
    const pageSize = Math.min(100, Math.max(1, Number(url.searchParams.get("pageSize") ?? 20)));
    const rows = adminListOrders(status);
    return HttpResponse.json({
      items: rows.slice((page - 1) * pageSize, page * pageSize),
      page, pageSize, totalCount: rows.length, totalPages: Math.ceil(rows.length / pageSize),
    } satisfies PagedResult<OrderDto>);
  }),

  http.get(`${BASE}/admin/orders/:id`, ({ request, params }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const o = adminListOrders().find((x) => x.id === params.id);
    if (!o) return problems("Not found.", {}, 404);
    return HttpResponse.json(o);
  }),

  /* the state machine is the contract — PATCH only accepts legal targets
     (ORDER_TRANSITIONS), and Pending transitions run the guarded settle so
     reservations move exactly like the real B1 write path. */
  http.patch(`${BASE}/admin/orders/:id/status`, async ({ request, params }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const body = (await request.json().catch(() => null)) as { status?: string } | null;
    const to = body?.status as OrderStatus | undefined;
    if (!to || !ORDER_STATUSES.includes(to))
      return problems("Validation failed.", { Status: [`Invalid status '${to ?? ""}'.`] });
    const r = adminSetOrderStatus(params.id as string, to);
    persist();
    if (!r.ok) return problems(r.error, { Status: [r.error] }, r.error === "Order not found." ? 404 : 400);
    return HttpResponse.json(r.value);
  }),

  /* --------------------------- admin · AI audit (§A7) --------------------------- */
  http.get(`${BASE}/admin/ai/ledger`, ({ request }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    return HttpResponse.json(aiLedgerList());
  }),

  http.get(`${BASE}/admin/ai/conversations`, ({ request }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    return HttpResponse.json(aiConversationsList());
  }),

  http.get(`${BASE}/admin/ai/settings`, ({ request }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    return HttpResponse.json(getAiSettings());
  }),

  http.put(`${BASE}/admin/ai/settings`, async ({ request }) => {
    const deny = adminOnly(request);
    if (deny) return deny;
    const body = (await request.json().catch(() => null)) as { enabled?: boolean; tools?: Record<string, boolean> } | null;
    if (!body) return problems("Validation failed.", { "": ["Request body required."] });
    const next = setAiSettings({ enabled: body.enabled, tools: body.tools });
    persist();
    return HttpResponse.json(next);
  }),

  /* ------------------------------- agent chat ------------------------------
   * §A5: POST /api/agent/chat → SSE. Word deltas first (real typing feel),
   * then the structured turn (tool trail, cards, quote). Requires login — v1
   * chat buckets are per-user (B3). NEXT_PUBLIC_MOCK_MODE: ratelimit → every
   * 3rd chat 429s with Retry-After; agentdown → provider unavailable, the UI
   * must degrade without breaking the rest of the shop (§A4 AI_NOT_CONFIGURED). */
  http.post(`${BASE}/agent/chat`, async ({ request }) => {
    const u = requireAuth(request);
    if (!u)
      return HttpResponse.json({ code: "UNAUTHORIZED", title: "Chat requires login in v1 — the bins and facets stay open.", message: "Chat requires login in v1 — the bins and facets stay open.", retryable: false }, { status: 401 });

    if (MODE === "agentdown" || !aiKillSwitchOn())
      return HttpResponse.json({ code: "AI_NOT_CONFIGURED", title: "No provider key on this deployment.", message: MODE === "agentdown" ? "No provider key on this deployment." : "The assistant is switched off from the admin console.", retryable: false }, { status: 503 });

    if (MODE === "ratelimit" && ++chatCount % 3 === 0) {
      return HttpResponse.json({ code: "RATE_LIMITED", title: "Too many chats per minute.", message: "Too many chats per minute.", retryable: true }, {
        status: 429,
        headers: { "Retry-After": "20" },
      });
    }

    const body = (await request.json().catch(() => null)) as { message?: string; conversationId?: string; answers?: Record<string, string | number | boolean> } | null;
    if (!body?.message?.trim() || !body.conversationId)
      return HttpResponse.json({ code: "VALIDATION", title: "message and conversationId are required.", errors: {}, retryable: false }, { status: 400 });

    const t0 = Date.now();
    const result = runAgentTurn(u.id, body.conversationId, body.message, body.answers);
    recordAiCalls(body.conversationId, ledgerRows(body.message, result, Date.now() - t0));
    persist(); // agent turns mutate the live db (cart writes, quotes) — same seam rule as REST

    const enc = new TextEncoder();
    /* frames are the SHARED wire vocabulary (lib/api/types ChatSseFrame) —
       the only consumer of this grammar is lib/chat/transport.ts (§B5+ 3). */
    const frame = (f: ChatSseFrame) => `data: ${JSON.stringify(f)}\n\n`;
    const stream = new ReadableStream<Uint8Array>({
      async start(controller) {
        for (const word of result.text.split(/(?<=\s)/)) {
          controller.enqueue(enc.encode(frame({ type: "delta", text: word })));
          await new Promise((r) => setTimeout(r, 14));
        }
        controller.enqueue(enc.encode(frame({ type: "turn", result })));
        controller.close();
      },
    });

    return new HttpResponse(stream, {
      headers: {
        "Content-Type": "text/event-stream",
        "Cache-Control": "no-cache",
        "X-Correlation-Id": `chat-${Date.now().toString(36)}`,
      },
    });
  }),

  /* dev helper: wipe mock server state (localStorage-backed) between demos */
  http.post(`${BASE}/_mock/reset`, async () => {
    const { resetDb } = await import("./db");
    resetDb();
    return HttpResponse.json({ ok: true });
  }),

  /* dev helper: fast-forward every pending quote past expiry (demo + the
     F3 Playwright gate needs the release path without a 30-minute wait) */
  http.post(`${BASE}/_mock/expire-quotes`, () => {
    const settled = expirePendingQuotes();
    persist();
    return HttpResponse.json({ ok: true, settled });
  }),
];
