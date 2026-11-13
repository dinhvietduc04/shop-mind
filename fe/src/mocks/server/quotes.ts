import type { OrderDto, OrderStatus, QuoteDto, ShippingAddressDto } from "@/infrastructure/api";
import { isLegalTransition, stockFate } from "@/lib/orders/lifecycle";
import { shippingFeeFor } from "@/lib/shipping";
import { cartClear, getCart } from "./cart";
import { commitReservation, releaseReservation, tryReserve } from "./inventory";
import { registerStore, uuid, type AdminResult } from "./snapshot";

/** The quote ledger domain: the Quote (an Order in the awaiting-payment role),
 *  its Payment Session, and the guarded settle that commits or releases the
 *  Reservation. This module holds the highest-risk invariants: atomic per-line
 *  reserve-with-rollback, the one-open-quote replace policy with agent origin
 *  scope (`QUOTE_HELD_BY_HUMAN`), and idempotent settle.
 *
 *  Pure: no persist(), no window. The HTTP seam decides when to snapshot, and
 *  quote-ledger.test.ts runs these domains without a persistence layer.
 */

const QUOTE_TTL_MS = 30 * 60 * 1000;

/** internal view: the DTO plus the fields that never leave the API */
export type QuoteRecord = QuoteDto & { userId: string; origin: "human" | "agent" };

const orders = new Map<string, OrderDto>();
const quotes = new Map<string, QuoteRecord>(); // sessionToken -> quote
const userActiveQuote = new Map<string, string>(); // userId -> sessionToken

registerStore({
  name: "orders",
  dump: () => [...orders.entries()],
  restore: (v) => (v as [string, OrderDto][]).forEach(([k, o]) => orders.set(k, o)),
  clear: () => orders.clear(),
});

registerStore({
  name: "quotes",
  dump: () => [...quotes.entries()],
  restore: (v) => {
    (v as [string, QuoteRecord & { origin?: "human" | "agent" }][]).forEach(([k, q]) =>
      quotes.set(k, { ...q, origin: q.origin ?? "human" })
    );
    /* After a reload the two maps hold separate deserialized copies of the
       same order. Re-point every quote at the orders-map instance so status
       writes (PSP settle, admin transitions) can never fork. The orders map
       is the single source of truth. */
    for (const q of quotes.values()) {
      const o = orders.get(q.order.id);
      if (o) q.order = o;
    }
  },
  clear: () => quotes.clear(),
});

registerStore({
  name: "userActiveQuote",
  dump: () => [...userActiveQuote.entries()],
  restore: (v) => (v as [string, string][]).forEach(([k, t]) => userActiveQuote.set(k, t)),
  clear: () => userActiveQuote.clear(),
});

/* --------------------------------- create --------------------------------- */

export function createQuote(
  userId: string,
  address: ShippingAddressDto,
  origin: "human" | "agent" = "human"
): { ok: true; quote: QuoteRecord; sessionToken: string } | { ok: false; error: string; code?: string } {
  const items = getCart(userId);
  if (items.length === 0) return { ok: false, error: "Cart is empty." };

  // Replace policy: one active quote per user. Release the old reservation.
  // The agent may replace its own quotes. A human-origin quote is untouchable
  // ("QUOTE_HELD_BY_HUMAN") until the human acts.
  const oldToken = userActiveQuote.get(userId);
  if (oldToken) {
    const prev = quotes.get(oldToken);
    if (origin === "agent" && prev?.origin === "human" && prev.order.status === "Pending") {
      return { ok: false, error: "A quote is already open from the bill page. The assistant cannot touch a human-owned quote.", code: "QUOTE_HELD_BY_HUMAN" };
    }
    settleQuote(oldToken, "Cancelled", "Replaced by a newer quote.");
  }

  // Reserve atomically per line; on any failure roll back what we reserved.
  const reserved: { productId: string; qty: number }[] = [];
  for (const it of items) {
    if (!tryReserve(it.productId, it.quantity)) {
      reserved.forEach((r) => releaseReservation(r.productId, r.qty));
      return { ok: false, error: `Insufficient stock for one or more items right now. Nothing was reserved.` };
    }
    reserved.push({ productId: it.productId, qty: it.quantity });
  }

  const subtotal = +items.reduce((t, i) => t + i.subtotal, 0).toFixed(2);
  const shippingFee = shippingFeeFor(subtotal); // the only rule (lib/shipping)
  const total = +(subtotal + shippingFee).toFixed(2);
  const sessionToken = `${uuid()}${uuid()}`;
  const order: OrderDto = {
    id: `o-${sessionToken.slice(0, 8)}`,
    orderNumber: `SM-${new Date().getFullYear()}-${Math.random().toString(36).slice(2, 10).toUpperCase()}`,
    status: "Pending",
    subtotal,
    shippingFee,
    totalAmount: total,
    shippingAddress: address,
    createdAt: new Date().toISOString(),
    items: items.map((i) => ({ productId: i.productId, productName: i.productName, sku: i.sku, unitPrice: i.unitPrice, quantity: i.quantity, subtotal: i.subtotal })),
    payment: null,
  };
  const quote: QuoteRecord = {
    sessionToken,
    order,
    hostedPayUrl: `/psp/${sessionToken}`,
    expiresAt: new Date(Date.now() + QUOTE_TTL_MS).toISOString(),
    userId,
    origin,
  };
  orders.set(order.id, order);
  quotes.set(sessionToken, quote);
  userActiveQuote.set(userId, sessionToken);
  return { ok: true, quote, sessionToken };
}

/* -------------------------------- read/settle ------------------------------ */

/** The bill page discovers an open quote even when the agent created it.
 *  Chat never links to the PSP directly (Assist invariant). */
export function activeQuoteFor(userId: string): { sessionToken: string; quote: QuoteDto } | null {
  const token = userActiveQuote.get(userId);
  if (!token) return null;
  const q = getQuote(token);
  if (!q || q.order.status !== "Pending") return null;
  return { sessionToken: token, quote: { sessionToken: token, order: q.order, hostedPayUrl: q.hostedPayUrl, expiresAt: q.expiresAt } };
}

export function getQuote(sessionToken: string): QuoteRecord | null {
  const q = quotes.get(sessionToken);
  if (!q) return null;
  // lazy expiry, using the same guarded transition the sweeper would run server-side
  if (q.order.status === "Pending" && new Date(q.expiresAt).getTime() < Date.now()) {
    settleQuote(sessionToken, "PaymentFailed", QUOTE_EXPIRED_REASON);
  }
  return q;
}

/** The one reason string that marks an expiry settle (getQuote, sweeper, demo
 *  helper). Outcome decoding reads it in one place. */
export const QUOTE_EXPIRED_REASON = "Quote expired.";

/** Idempotent guarded transition: the first caller wins, and a replay is a
 *  no-op. The lifecycle module decides which moves touch stock (Reservation
 *  to Commit or Release); this file executes that fate, never decides it.
 *  payment.outcome records why a PaymentFailed happened. */
export function settleQuote(
  sessionToken: string,
  outcome: Extract<OrderStatus, "Paid" | "Cancelled" | "PaymentFailed">,
  reason?: string
): boolean {
  const q = quotes.get(sessionToken);
  if (!q || q.order.status !== "Pending") return false;
  const fate = stockFate(q.order.status, outcome);
  if (fate === "commit") {
    for (const it of q.order.items) commitReservation(it.productId, it.quantity);
    cartClear(q.userId);
    q.order.payment = { id: uuid(), amount: q.order.totalAmount, status: "Succeeded", transactionId: `MOCK-TXN-${uuid()}`, outcome: "captured" };
  } else if (fate === "release") {
    for (const it of q.order.items) releaseReservation(it.productId, it.quantity);
    if (outcome === "PaymentFailed") {
      const expired = reason === QUOTE_EXPIRED_REASON;
      q.order.payment = {
        id: uuid(),
        amount: q.order.totalAmount,
        status: "Failed",
        transactionId: expired ? null : `MOCK-DECLINED-${uuid()}`,
        outcome: expired ? "expired" : "failed",
      };
    } else {
      q.order.payment = null; // abandoned before any attempt, so there is nothing to record
    }
  }
  q.order.status = outcome;
  orders.set(q.order.id, q.order); // keep the ledger copy in step
  if (userActiveQuote.get(q.userId) === sessionToken) userActiveQuote.delete(q.userId);
  return true;
}

export function findTokenFor(orderId: string): string | undefined {
  for (const [token, q] of quotes) if (q.order.id === orderId) return token;
  return undefined;
}

/** FK refuse for the product-delete guard. */
export function openQuoteHolds(productId: string): boolean {
  for (const q of quotes.values())
    if (q.order.status === "Pending" && q.order.items.some((i) => i.productId === productId)) return true;
  return false;
}

/* --------------------------------- queries -------------------------------- */

export function ordersFor(userId: string): OrderDto[] {
  const list = [...orders.values()].filter((o) => {
    const q = [...quotes.values()].find((qq) => qq.order.id === o.id);
    return q?.userId === userId;
  });
  return list.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}

/** Cancel is legal only from Pending. Paid orders must be refunded. */
export function cancelOrder(orderId: string, userId: string): { ok: true } | { ok: false; error: string } {
  const q = [...quotes.values()].find((qq) => qq.order.id === orderId);
  if (!q || q.userId !== userId) return { ok: false, error: "Order not found." };
  const o = q.order;
  if (o.status === "Pending") {
    settleQuote(q.sessionToken, "Cancelled", "Cancelled by user.");
    return { ok: true };
  }
  if (o.status === "Paid") return { ok: false, error: "Paid orders must be refunded via support (mock). Try before shipment." };
  return { ok: false, error: `Order is already ${o.status}.` };
}

/** Dev helper for the expiry demo/test gate: force every pending quote past
 *  its expiry and let the lazy sweeper run the guarded transition. */
export function expirePendingQuotes(): number {
  let n = 0;
  for (const [token, q] of [...quotes]) {
    if (q.order.status !== "Pending") continue;
    q.expiresAt = new Date(Date.now() - 1000).toISOString();
    getQuote(token); // triggers the guarded settle, which records PaymentFailed and releases stock
    n++;
  }
  return n;
}

/** Order detail by id, owner-scoped. Mirrors the real GET /orders/{id} 404/403 merge. */
export function orderFor(orderId: string, userId: string): OrderDto | null {
  const q = [...quotes.values()].find((qq) => qq.order.id === orderId);
  if (!q || q.userId !== userId) return null;
  // read through getQuote so a still-pending expired order transitions
  getQuote(q.sessionToken);
  return orders.get(orderId) ?? null;
}

/* ------------------------------ admin order ops ---------------------------- */

/** GET /api/admin/orders: every order, newest first, with an optional status filter. */
export function adminListOrders(status?: string | null): OrderDto[] {
  const list = [...orders.values()].filter((o) => !status || o.status === status);
  return list.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}

/** PATCH /api/admin/orders/{id}/status. The server enforces the dropdown's
 *  legality too (defense in depth: the same lifecycle function the client
 *  uses). Pending transitions run through the guarded settle so reservations
 *  commit or release exactly like the real B1 write path; later ones are plain
 *  status writes. */
export function adminSetOrderStatus(orderId: string, to: OrderStatus): AdminResult<OrderDto> {
  const o = orders.get(orderId);
  if (!o) return { ok: false, error: "Order not found." };
  if (!isLegalTransition(o.status, to))
    return { ok: false, error: `Illegal transition from ${o.status} to ${to}.`, field: "Status" };
  if (o.status === "Pending") {
    const token = findTokenFor(orderId);
    if (token) {
      // money-boundary path: settle() commits or releases reservations
      if (!settleQuote(token, to as "Paid" | "Cancelled" | "PaymentFailed", "Admin transition."))
        return { ok: false, error: "Order is no longer pending (it may have just expired)." };
      return { ok: true, value: orders.get(orderId)! };
    }
  }
  o.status = to;
  orders.set(orderId, o);
  return { ok: true, value: o };
}
