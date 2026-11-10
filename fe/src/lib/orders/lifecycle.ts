import type { OrderStatus } from "@/lib/api/types";
import type { BandName } from "@/lib/bands";

/**
 * The Quote lifecycle — the ONE module where order-state facts are stated
 * (C5). Client mirror of `backend/Shop.Domain/Rules/OrderTransitions.cs:7–16`
 * (verified byte-identical at review time), extended with the semantics the
 * satellites used to each hand- their own copy of:
 *
 *  - legality + terminality (admin dropdown + the mock server's guard both
 *    call `legalTargets` / `isLegalTransition` — defense-in-depth without a
 *    second enumeration of the machine),
 *  - the mainline rank of ADR-0005 (`Pending < Paid < Processing < Shipped <
 *    Delivered`; Cancelled/PaymentFailed are unranked side branches),
 *  - STOCK FATE per transition — the GLOSSARY's Reservation/Commit/Release.
 *    This used to live only as an if-branch inside `db.ts`'s status write,
 *    echoed nowhere but dashboard prose,
 *  - revenue eligibility (the dashboard KPI rule, previously an inline array),
 *  - the display band (one ladder for the customer timeline and admin chips —
 *    they were two maps that had already drifted).
 *
 * What deliberately does NOT live here: ADR-0005's replay/warning/Payment-
 * Anomaly outcome matrix. That is server-side conditional-update machinery;
 * it arrives with the B-milestones and a REST field we have not drafted.
 */

export const ORDER_STATUSES: readonly OrderStatus[] = [
  "Pending",
  "Paid",
  "Processing",
  "Shipped",
  "Delivered",
  "Cancelled",
  "PaymentFailed",
];

const TRANSITIONS: Record<OrderStatus, readonly OrderStatus[]> = {
  Pending: ["Paid", "PaymentFailed", "Cancelled"],
  Paid: ["Processing", "Cancelled"],
  Processing: ["Shipped"],
  Shipped: ["Delivered"],
  Delivered: [],
  Cancelled: [],
  PaymentFailed: [],
};

export const legalTargets = (from: OrderStatus): readonly OrderStatus[] =>
  TRANSITIONS[from] ?? [];

export const isLegalTransition = (from: OrderStatus, to: OrderStatus): boolean =>
  legalTargets(from).includes(to);

export const isTerminal = (s: OrderStatus): boolean => legalTargets(s).length === 0;

/** Happy-path mainline (the fulfillment timeline's five dots; ADR-0005 rank). */
export const MAINLINE: readonly OrderStatus[] = [
  "Pending",
  "Paid",
  "Processing",
  "Shipped",
  "Delivered",
];

/** ADR-0005 mainline rank; -1 = terminal side branch (Cancelled/PaymentFailed). */
export const mainlineRank = (s: OrderStatus): number => MAINLINE.indexOf(s);

/** Reservation/Commit/Release: which moves physically touch stock. */
export type StockFate = "commit" | "release" | "none";
export function stockFate(from: OrderStatus, to: OrderStatus): StockFate {
  if (from !== "Pending") return "none"; // only Pending settlements hold reservations
  if (to === "Paid") return "commit";
  if (to === "Cancelled" || to === "PaymentFailed") return "release";
  return "none";
}

/** Revenue KPI: money received counts; terminal failures never do. */
export const isRevenueEligible = (s: OrderStatus): boolean =>
  s === "Paid" || s === "Processing" || s === "Shipped" || s === "Delivered";

/** One band ladder for order state — customer chips and admin tables agree. */
export const ORDER_BAND: Record<OrderStatus, BandName> = {
  Pending: "yellow",
  Paid: "green",
  Processing: "green",
  Shipped: "blue",
  Delivered: "blue",
  Cancelled: "grey", // human abandonment — grey, not the red of failure
  PaymentFailed: "red",
};
