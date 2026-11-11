import type { OrderStatus } from "@/lib/api/types";
import type { BandName } from "@/lib/bands";

/**
 * Client mirror of `backend/Shop.Domain/Rules/OrderTransitions.cs`. The
 * transition table follows ADR-0005, including the mainline rank and the stock
 * fate of each move. The client and the mock server both read this module.
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

/** ADR-0005 mainline rank; -1 marks a terminal side branch (Cancelled/PaymentFailed). */
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

/** One band ladder for order state. Customer chips and admin tables agree. */
export const ORDER_BAND: Record<OrderStatus, BandName> = {
  Pending: "yellow",
  Paid: "green",
  Processing: "green",
  Shipped: "blue",
  Delivered: "blue",
  Cancelled: "grey", // human abandonment, grey rather than the red of failure
  PaymentFailed: "red",
};
