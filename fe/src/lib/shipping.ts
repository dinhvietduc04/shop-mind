/**
 * Shipping rule shared by the mock server and the bill's free-shipping meter.
 * Pricing authority is server-side; the quote DTO is the source of truth.
 */
export const FREE_SHIPPING_THRESHOLD = 500;
export const FLAT_SHIPPING_FEE = 15;

export const shippingFeeFor = (subtotal: number): number =>
  subtotal === 0 || subtotal >= FREE_SHIPPING_THRESHOLD ? 0 : FLAT_SHIPPING_FEE;
