/**
 * Shipping rule shared by the mock server (db) and, for display hints only,
 * the UI. Authoritative pricing always happens server-side (docs/PLAN.md
 * principle #1) — the bill/quote DTO is the source of truth.
 */
export const FREE_SHIPPING_THRESHOLD = 500;
export const FLAT_SHIPPING_FEE = 15;
