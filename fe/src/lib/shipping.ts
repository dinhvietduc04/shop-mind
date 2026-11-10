/**
 * Shipping rule shared by the mock server (db) and, for display hints only,
 * the UI. Authoritative pricing always happens server-side (docs/PLAN.md
 * principle #1) — the bill/quote DTO is the source of truth.
 *
 * C1/Q15: the RULE lives here, once — the mock prices with it and the bill's
 * free-shipping meter previews with the same function. Before, the page
 * re-implemented the formula with a hardcoded 15 and the two could drift.
 */
export const FREE_SHIPPING_THRESHOLD = 500;
export const FLAT_SHIPPING_FEE = 15;

export const shippingFeeFor = (subtotal: number): number =>
  subtotal === 0 || subtotal >= FREE_SHIPPING_THRESHOLD ? 0 : FLAT_SHIPPING_FEE;
