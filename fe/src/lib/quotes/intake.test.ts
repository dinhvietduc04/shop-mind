import { describe, expect, it } from "vitest";
import { quoteOutcome } from "./intake";
import type { OrderDto, OrderStatus, PaymentStatus } from "@/lib/api/types";

const order = (status: OrderStatus, payment?: { outcome?: "captured" | "failed" | "expired" | null; transactionId?: string | null; status?: PaymentStatus }): OrderDto =>
  ({
    id: "o1",
    orderNumber: "SM-2026-ABCD",
    status,
    subtotal: 100,
    shippingFee: 0,
    totalAmount: 100,
    shippingAddress: { fullName: "x", phone: "y", address: "z", city: "c" },
    createdAt: new Date().toISOString(),
    items: [],
    payment: payment
      ? { id: "p1", amount: 100, status: payment.status ?? "Succeeded", transactionId: payment.transactionId ?? null, outcome: payment.outcome ?? null }
      : null,
  }) as OrderDto;

/* Expired versus declined is read from payment.outcome, the wire
   discriminator, and never from transactionId being null. */
describe("quoteOutcome is the single decode", () => {
  it("a live hold is open regardless of the clock", () => {
    expect(quoteOutcome(order("Pending"))).toBe("open");
  });

  it("paid and beyond", () => {
    expect(quoteOutcome(order("Paid", { outcome: "captured", transactionId: "TX" }))).toBe("paid");
    expect(quoteOutcome(order("Processing", { outcome: "captured" }))).toBe("fulfilled");
    expect(quoteOutcome(order("Delivered", { outcome: "captured" }))).toBe("fulfilled");
  });

  it("distinguishes expired from declined only through outcome", () => {
    expect(quoteOutcome(order("PaymentFailed", { outcome: "expired", transactionId: null }))).toBe("expired");
    expect(quoteOutcome(order("PaymentFailed", { outcome: "failed", transactionId: "MOCK-DECLINED" }))).toBe("declined");
  });

  it("legacy snapshots (no outcome written) decode conservatively as declined", () => {
    expect(quoteOutcome(order("PaymentFailed", { transactionId: null }))).toBe("declined");
  });

  it("abandonment is a human act, not a failure", () => {
    expect(quoteOutcome(order("Cancelled"))).toBe("abandoned");
  });
});
