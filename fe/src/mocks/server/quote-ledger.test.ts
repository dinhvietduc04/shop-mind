import { beforeEach, describe, expect, it } from "vitest";
import { allProducts } from "./catalog";
import { adminSetStock, available, tryReserve } from "./inventory";
import { cartAdd } from "./cart";
import {
  activeQuoteFor,
  adminListOrders,
  createQuote,
  expirePendingQuotes,
  getQuote,
  openQuoteHolds,
  settleQuote,
} from "./quotes";
import { resetServer } from "./snapshot";
import type { ShippingAddressDto } from "@/infrastructure/api";

/**
 * Plain Node assertions cover the quote ledger's riskiest invariants:
 * reserve-with-rollback, idempotent settle, expiry release, and
 * agent-versus-human origin scope. A test imports the domain modules, resets
 * the server, and calls them directly.
 */

const U = "u-test";
const ADDR: ShippingAddressDto = { fullName: "T", phone: "1", address: "a", city: "c" };

const tp = () => allProducts().find((p) => p.slug === "thinkpad-x1-carbon-gen-11")!;

beforeEach(() => resetServer());

describe("reserve and settle: stock fates at the money boundary", () => {
  it("creating a quote reserves per line atomically", () => {
    cartAdd(U, tp().id, 2);
    const before = available(tp().id);
    const r = createQuote(U, ADDR);
    expect(r.ok).toBe(true);
    expect(available(tp().id)).toBe(before - 2); // reserved, not yet sold
    if (r.ok) expect(r.quote.order.status).toBe("Pending");
  });

  it("Paid commits (quantity consumed); replay is a no-op", () => {
    cartAdd(U, tp().id, 1);
    const r = createQuote(U, ADDR);
    if (!r.ok) throw new Error("quote failed");
    const qtyBefore = available(tp().id); // already reserved
    expect(settleQuote(r.sessionToken, "Paid")).toBe(true);
    expect(available(tp().id)).toBe(qtyBefore); // commit consumes quantity and reserved
    expect(settleQuote(r.sessionToken, "Paid")).toBe(false); // first caller won
    expect(settleQuote(r.sessionToken, "Cancelled")).toBe(false);
  });

  it("expiry releases stock and stamps the payment outcome", () => {
    cartAdd(U, tp().id, 1);
    const before = available(tp().id);
    const r = createQuote(U, ADDR);
    if (!r.ok) throw new Error("quote failed");
    expect(available(tp().id)).toBe(before - 1);
    const n = expirePendingQuotes();
    expect(n).toBe(1);
    expect(available(tp().id)).toBe(before); // available again
    const q = getQuote(r.sessionToken);
    expect(q?.order.status).toBe("PaymentFailed");
    expect(q?.order.payment?.outcome).toBe("expired"); // the wire discriminator
    expect(q?.order.payment?.transactionId).toBeNull();
  });

  it("a decline is stamped failed, not expired, and gets a transaction id", () => {
    cartAdd(U, tp().id, 1);
    const r = createQuote(U, ADDR);
    if (!r.ok) throw new Error("quote failed");
    settleQuote(r.sessionToken, "PaymentFailed", "Card declined (mock).");
    const q = getQuote(r.sessionToken);
    expect(q?.order.payment?.outcome).toBe("failed");
    expect(q?.order.payment?.transactionId).toContain("MOCK-DECLINED");
  });

  it("mid-flight stock loss rolls back every line, with no partial reservations", () => {
    const monitor = allProducts().find((p) => p.slug === "ultrasharp-27-4k")!;
    const gpu = allProducts().find((p) => p.slug === "rtx-4070")!;
    cartAdd(U, monitor.id, 1);
    cartAdd(U, gpu.id, 5);
    // stock drops between cart edits and quote creation, which is the race the guard exists for
    adminSetStock(gpu.id, 3);
    const mBefore = available(monitor.id);
    const gBefore = available(gpu.id);
    const r = createQuote(U, ADDR);
    expect(r.ok).toBe(false);
    expect(available(monitor.id)).toBe(mBefore); // line one rolled back
    expect(available(gpu.id)).toBe(gBefore); // line two never stuck
  });
});

describe("origin-scoped replace policy (A2, QUOTE_HELD_BY_HUMAN)", () => {
  it("an agent cannot replace a human's Open Quote", () => {
    cartAdd(U, tp().id, 1);
    const human = createQuote(U, ADDR, "human");
    expect(human.ok).toBe(true);
    const agent = createQuote(U, ADDR, "agent");
    if (agent.ok) throw new Error("should have been refused");
    expect(agent.code).toBe("QUOTE_HELD_BY_HUMAN");
    // the human's quote is untouched
    if (human.ok) expect(getQuote(human.sessionToken)?.order.status).toBe("Pending");
  });

  it("an agent replaces its own quote, releasing the old reservation", () => {
    cartAdd(U, tp().id, 1);
    const a1 = createQuote(U, ADDR, "agent");
    if (!a1.ok) throw new Error("a1 failed");
    const heldAfterFirst = available(tp().id);
    cartAdd(U, tp().id, 1); // grow the cart line
    const a2 = createQuote(U, ADDR, "agent");
    expect(a2.ok).toBe(true);
    if (!a2.ok) return;
    expect(getQuote(a1.sessionToken)?.order.status).toBe("Cancelled");
    // old reservation released, new one doubled, so net available drops by 2
    expect(available(tp().id)).toBe(heldAfterFirst + 1 - 2);
  });

  it("a settled quote stops being an Open Quote", () => {
    cartAdd(U, tp().id, 1);
    const r = createQuote(U, ADDR);
    if (!r.ok) throw new Error("quote failed");
    expect(activeQuoteFor(U)?.sessionToken).toBe(r.sessionToken);
    settleQuote(r.sessionToken, "Cancelled", "Abandoned by user.");
    expect(activeQuoteFor(U)).toBeNull();
  });
});

describe("delete guards use the ledger domain's own check", () => {
  it("openQuoteHolds refuses hard-delete while a reservation is open", () => {
    cartAdd(U, tp().id, 1);
    createQuote(U, ADDR);
    expect(openQuoteHolds(tp().id)).toBe(true);
  });

  it("admin order list includes every origin", () => {
    cartAdd(U, tp().id, 1);
    createQuote(U, ADDR, "agent");
    expect(adminListOrders("Pending").length).toBe(1);
  });

  it("tryReserve is the no-oversell guard", () => {
    const p = tp();
    const start = available(p.id);
    expect(tryReserve(p.id, start + 1)).toBe(false);
    expect(tryReserve(p.id, 1)).toBe(true);
    expect(available(p.id)).toBe(start - 1);
  });
});
