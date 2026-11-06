import type { CartItemDto, OrderDto, OrderStatus, QuoteDto, ShippingAddressDto } from "@/lib/api/types";
import {
  FIXTURE_PRODUCTS,
  FLAT_SHIPPING_FEE,
  FREE_SHIPPING_THRESHOLD,
  MOCK_USERS,
  type FixtureProduct,
} from "./fixtures";

/**
 * In-memory server state for MSW. Stock mutation helpers mimic the atomic
 * guarded UPDATEs of backend plan B1 — check and write in one step, so
 * concurrent handler calls can never oversell.
 */

interface StockRow { quantity: number; reserved: number }

/**
 * MSW handlers execute in the PAGE context — a reload would wipe plain Maps.
 * The fake server therefore persists its whole state in localStorage, so the
 * cart, reservations, stock decrements and orders survive navigation (just
 * like a real backend would). `/api/_mock/reset` clears it.
 */
const DB_KEY = "shopmind.mockdb.v1";

const stock = new Map<string, StockRow>(
  FIXTURE_PRODUCTS.map((p) => [p.id, { quantity: p.quantity, reserved: 0 }])
);

const carts = new Map<string, Map<string, number>>(); // userId -> productId -> qty
const orders = new Map<string, OrderDto>();
const quotes = new Map<string, QuoteDto & { userId: string }>(); // sessionToken -> quote
const userActiveQuote = new Map<string, string>(); // userId -> sessionToken

function save() {
  if (typeof window === "undefined") return;
  const snap = {
    stock: [...stock.entries()],
    carts: [...carts.entries()].map(([u, m]) => [u, [...m.entries()]] as [string, [string, number][]]),
    orders: [...orders.entries()],
    quotes: [...quotes.entries()],
    userActiveQuote: [...userActiveQuote.entries()],
  };
  localStorage.setItem(DB_KEY, JSON.stringify(snap));
}

function load() {
  if (typeof window === "undefined") return;
  try {
    const raw = localStorage.getItem(DB_KEY);
    if (!raw) return;
    const s = JSON.parse(raw) as {
      stock: [string, StockRow][];
      carts: [string, [string, number][]][];
      orders: [string, OrderDto][];
      quotes: [string, QuoteDto & { userId: string }][];
      userActiveQuote: [string, string][];
    };
    s.stock.forEach(([k, v]) => stock.set(k, v));
    s.carts.forEach(([u, entries]) => carts.set(u, new Map(entries)));
    s.orders.forEach(([k, v]) => orders.set(k, v));
    s.quotes.forEach(([k, v]) => quotes.set(k, v));
    s.userActiveQuote.forEach(([k, v]) => userActiveQuote.set(k, v));
  } catch {
    localStorage.removeItem(DB_KEY);
  }
}

load();

export function resetDb() {
  if (typeof window !== "undefined") localStorage.removeItem(DB_KEY);
  stock.clear();
  FIXTURE_PRODUCTS.forEach((p) => stock.set(p.id, { quantity: p.quantity, reserved: 0 }));
  carts.clear();
  orders.clear();
  quotes.clear();
  userActiveQuote.clear();
}

const rand = () => Math.random().toString(36).slice(2, 10);
export const uuid = () => `${rand()}-${rand()}`;

export function available(productId: string): number {
  const row = stock.get(productId);
  return row ? row.quantity - row.reserved : 0;
}

export function liveProduct(p: FixtureProduct): FixtureProduct {
  return { ...p, availableStock: available(p.id) };
}

/** Atomic: reserve `qty` for product. False = insufficient (no oversell). */
export function tryReserve(productId: string, qty: number): boolean {
  const row = stock.get(productId);
  if (!row || row.quantity - row.reserved < qty) return false;
  row.reserved += qty;
  save();
  return true;
}

/** Atomic commit of a paid reservation: consume quantity AND reserved. */
export function commitReservation(productId: string, qty: number): boolean {
  const row = stock.get(productId);
  if (!row || row.reserved < qty || row.quantity < qty) return false;
  row.quantity -= qty;
  row.reserved -= qty;
  save();
  return true;
}

export function releaseReservation(productId: string, qty: number): void {
  const row = stock.get(productId);
  if (!row) return;
  row.reserved = Math.max(0, row.reserved - qty);
  save();
}

/* ---------------------------------- auth ---------------------------------- */

export function userByToken(authorization?: string | null): { id: string; email: string; admin: boolean } | null {
  const token = authorization?.startsWith("Bearer ") ? authorization.slice(7) : null;
  if (!token || !token.startsWith("mock.")) return null;
  const email = token.slice(5);
  const rec = MOCK_USERS[email];
  if (!rec) return null;
  return { id: rec.user.id, email, admin: rec.user.role === "Admin" };
}

/* ---------------------------------- cart ---------------------------------- */

function cartMap(userId: string): Map<string, number> {
  let m = carts.get(userId);
  if (!m) { m = new Map(); carts.set(userId, m); }
  return m;
}

export function getCart(userId: string): CartItemDto[] {
  const m = cartMap(userId);
  const items: CartItemDto[] = [];
  for (const [productId, quantity] of m) {
    const p = FIXTURE_PRODUCTS.find((x) => x.id === productId);
    if (!p) continue;
    items.push({
      id: `ci-${productId}`, productId, productName: p.name, sku: p.sku,
      quantity, unitPrice: p.price, subtotal: +(p.price * quantity).toFixed(2),
      imageUrl: p.images.find((i) => i.url)?.url || null,
    });
  }
  return items;
}

export function cartAdd(userId: string, productId: string, qty: number): { ok: true; items: CartItemDto[] } | { ok: false; error: string } {
  const p = FIXTURE_PRODUCTS.find((x) => x.id === productId);
  if (!p) return { ok: false, error: "Product not found." };
  if (qty <= 0) return { ok: false, error: "Quantity must be greater than zero." };
  const m = cartMap(userId);
  const newQty = (m.get(productId) ?? 0) + qty;
  if (available(productId) < newQty) return { ok: false, error: `Insufficient stock for ${p.name} (available ${available(productId)}, requested ${newQty}).` };
  m.set(productId, newQty);
  save();
  return { ok: true, items: getCart(userId) };
}

export function cartUpdate(userId: string, productId: string, qty: number): { ok: true; items: CartItemDto[] } | { ok: false; error: string } {
  if (qty <= 0) return { ok: false, error: "Quantity must be greater than zero." };
  if (available(productId) < qty) return { ok: false, error: `Insufficient stock (available ${available(productId)}, requested ${qty}).` };
  cartMap(userId).set(productId, qty);
  save();
  return { ok: true, items: getCart(userId) };
}

export function cartRemove(userId: string, productId: string): CartItemDto[] {
  cartMap(userId).delete(productId);
  save();
  return getCart(userId);
}

export function cartClear(userId: string): void {
  cartMap(userId).clear();
  save();
}

/* ---------------------------------- quote --------------------------------- */

const QUOTE_TTL_MS = 30 * 60 * 1000;

export function createQuote(
  userId: string,
  address: ShippingAddressDto
): { ok: true; quote: QuoteDto } | { ok: false; error: string } {
  const items = getCart(userId);
  if (items.length === 0) return { ok: false, error: "Cart is empty." };

  // Replace policy: one active quote per user — release the old reservation.
  const oldToken = userActiveQuote.get(userId);
  if (oldToken) settleQuote(oldToken, "Cancelled", "Replaced by a newer quote.");

  // Reserve atomically per line; on any failure roll back what we reserved.
  const reserved: string[] = [];
  for (const it of items) {
    if (!tryReserve(it.productId, it.quantity)) {
      reserved.forEach((r) => {
        const prev = items.find((x) => x.productId === r);
        if (prev) releaseReservation(r, prev.quantity);
      });
      return { ok: false, error: `Insufficient stock for one or more items right now — nothing was reserved.` };
    }
    reserved.push(it.productId);
  }

  const subtotal = +items.reduce((t, i) => t + i.subtotal, 0).toFixed(2);
  const shippingFee = subtotal >= FREE_SHIPPING_THRESHOLD || subtotal === 0 ? 0 : FLAT_SHIPPING_FEE;
  const total = +(subtotal + shippingFee).toFixed(2);
  const sessionToken = uuid() + uuid();
  const order: OrderDto = {
    id: `o-${sessionToken.slice(0, 8)}`,
    orderNumber: `SM-${new Date().getFullYear()}-${rand().toUpperCase()}`,
    status: "Pending",
    subtotal,
    shippingFee,
    totalAmount: total,
    shippingAddress: address,
    createdAt: new Date().toISOString(),
    items: items.map((i) => ({ productId: i.productId, productName: i.productName, sku: i.sku, unitPrice: i.unitPrice, quantity: i.quantity, subtotal: i.subtotal })),
    payment: null,
  };
  const quote: QuoteDto & { userId: string } = {
    order,
    hostedPayUrl: `/psp/${sessionToken}`,
    expiresAt: new Date(Date.now() + QUOTE_TTL_MS).toISOString(),
    userId,
  };
  orders.set(order.id, order);
  quotes.set(sessionToken, quote);
  userActiveQuote.set(userId, sessionToken);
  save();
  return { ok: true, quote };
}

export function getQuote(sessionToken: string): (QuoteDto & { userId: string }) | null {
  const q = quotes.get(sessionToken);
  if (!q) return null;
  // lazy expiry — same guarded transition the sweeper would run server-side
  if (q.order.status === "Pending" && new Date(q.expiresAt).getTime() < Date.now()) {
    settleQuote(sessionToken, "PaymentFailed", "Quote expired.");
  }
  return q;
}

/** Idempotent guarded transition — first caller wins, replay is a no-op. */
export function settleQuote(sessionToken: string, outcome: Extract<OrderStatus, "Paid" | "Cancelled" | "PaymentFailed">, reason?: string): boolean {
  const q = quotes.get(sessionToken);
  if (!q || q.order.status !== "Pending") return false;
  if (outcome === "Paid") {
    for (const it of q.order.items) commitReservation(it.productId, it.quantity);
    cartClear(q.userId);
    q.order.payment = { id: uuid(), amount: q.order.totalAmount, status: "Succeeded", transactionId: `MOCK-TXN-${uuid()}` };
  } else {
    for (const it of q.order.items) releaseReservation(it.productId, it.quantity);
    q.order.payment = outcome === "PaymentFailed"
      ? { id: uuid(), amount: q.order.totalAmount, status: "Failed", transactionId: reason === "Quote expired." ? null : `MOCK-DECLINED-${uuid()}` }
      : null;
  }
  q.order.status = outcome;
  if (userActiveQuote.get(q.userId) === sessionToken) userActiveQuote.delete(q.userId);
  save();
  return true;
}

export function ordersFor(userId: string): OrderDto[] {
  const list = [...orders.values()].filter((o) => {
    const q = [...quotes.values()].find((qq) => qq.order.id === o.id);
    return q?.userId === userId;
  });
  return list.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}

/** Cancel only when legal per the backend state machine (Pending/Paid → Cancelled). */
export function cancelOrder(orderId: string, userId: string): { ok: true } | { ok: false; error: string } {
  const q = [...quotes.values()].find((qq) => qq.order.id === orderId);
  if (!q || q.userId !== userId) return { ok: false, error: "Order not found." };
  const o = q.order;
  if (o.status === "Pending") { settleQuote(findTokenFor(orderId)!, "Cancelled", "Cancelled by user."); return { ok: true }; }
  if (o.status === "Paid") return { ok: false, error: "Paid orders must be refunded via support (mock). Try before shipment." };
  return { ok: false, error: `Order is already ${o.status}.` };
}

function findTokenFor(orderId: string): string | undefined {
  for (const [token, q] of quotes) if (q.order.id === orderId) return token;
  return undefined;
}
