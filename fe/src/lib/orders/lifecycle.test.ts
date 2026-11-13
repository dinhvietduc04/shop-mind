import { describe, expect, it } from "vitest";
import {
  MAINLINE,
  ORDER_BAND,
  ORDER_STATUSES,
  isLegalTransition,
  isRevenueEligible,
  isTerminal,
  legalTargets,
  mainlineRank,
  stockFate,
} from "./lifecycle";
import type { OrderStatus } from "@/infrastructure/api";

/* Parity with the backend authority at backend/Shop.Domain/Rules/
   OrderTransitions.cs lines 7 to 16. If the .NET file changes, this test is
   the tripwire. */
const CS_TABLE: Record<OrderStatus, OrderStatus[]> = {
  Pending: ["Paid", "PaymentFailed", "Cancelled"],
  Paid: ["Processing", "Cancelled"],
  Processing: ["Shipped"],
  Shipped: ["Delivered"],
  Delivered: [],
  Cancelled: [],
  PaymentFailed: [],
};

describe("Quote lifecycle parity with OrderTransitions.cs", () => {
  it("enumerates exactly the shipped status union", () => {
    expect([...ORDER_STATUSES].sort()).toEqual(Object.keys(CS_TABLE).sort());
  });

  it("offers exactly the .NET legal targets from every state", () => {
    for (const s of ORDER_STATUSES) {
      expect([...legalTargets(s)].sort()).toEqual([...CS_TABLE[s]].sort());
    }
  });

  it("terminality matches (Delivered/Cancelled/PaymentFailed close the row)", () => {
    const side: OrderStatus[] = ["Delivered", "Cancelled", "PaymentFailed"];
    expect(side.filter(isTerminal)).toHaveLength(3);
    expect(ORDER_STATUSES.filter(isTerminal)).toHaveLength(3);
  });

  it("refuses regressions and side-branch re-entry", () => {
    expect(isLegalTransition("Paid", "Pending")).toBe(false);
    expect(isLegalTransition("Cancelled", "Paid")).toBe(false);
    expect(isLegalTransition("PaymentFailed", "Paid")).toBe(false); // ADR-0005 anomaly is server-side, not a legal move
    expect(isLegalTransition("Pending", "Processing")).toBe(false);
  });
});

describe("stock fate matches the GLOSSARY's Reservation/Commit/Release", () => {
  it("only Pending settlements move stock", () => {
    expect(stockFate("Paid", "Processing")).toBe("none");
    expect(stockFate("Shipped", "Delivered")).toBe("none");
    expect(stockFate("Cancelled", "Cancelled")).toBe("none");
  });

  it("Paid commits; failure and cancel release", () => {
    expect(stockFate("Pending", "Paid")).toBe("commit");
    expect(stockFate("Pending", "Cancelled")).toBe("release");
    expect(stockFate("Pending", "PaymentFailed")).toBe("release");
  });
});

describe("mainline rank (ADR-0005), revenue, and bands", () => {
  it("ranks the happy path strictly and leaves side branches unranked", () => {
    const ranks = MAINLINE.map(mainlineRank);
    expect(ranks).toEqual([0, 1, 2, 3, 4]);
    expect(mainlineRank("Cancelled")).toBe(-1);
    expect(mainlineRank("PaymentFailed")).toBe(-1);
  });

  it("revenue eligibility counts money received and never terminal failures", () => {
    for (const s of ["Paid", "Processing", "Shipped", "Delivered"] as OrderStatus[])
      expect(isRevenueEligible(s)).toBe(true);
    for (const s of ["Pending", "Cancelled", "PaymentFailed"] as OrderStatus[])
      expect(isRevenueEligible(s)).toBe(false);
  });

  it("every status has a band, and abandonment is grey, failure red", () => {
    for (const s of ORDER_STATUSES) expect(ORDER_BAND[s]).toBeTruthy();
    expect(ORDER_BAND.Cancelled).toBe("grey");
    expect(ORDER_BAND.PaymentFailed).toBe("red");
  });
});
