/**
 * IEERC resistor color-code ladder.
 * Bands carry real data here: category identity, stock state, price digits.
 * Digits are ALWAYS printed alongside bands (colorblind fallback, per the
 * design brief).
 */

export type BandName =
  | "black" | "brown" | "red" | "orange" | "yellow" | "green"
  | "blue" | "violet" | "grey" | "white" | "gold" | "silver";

/** IEERC digit values 0 to 9 plus tolerance metals. */
export const BAND_DIGIT: Record<BandName, number | null> = {
  black: 0, brown: 1, red: 2, orange: 3, yellow: 4, green: 5,
  blue: 6, violet: 7, grey: 8, white: 9, gold: null, silver: null,
};

/** Literal classes so the Tailwind scanner can see every band utility. */
export const BAND_BG: Record<BandName, string> = {
  black: "bg-band-black",
  brown: "bg-band-brown",
  red: "bg-band-red",
  orange: "bg-band-orange",
  yellow: "bg-band-yellow",
  green: "bg-band-green",
  blue: "bg-band-blue",
  violet: "bg-band-violet",
  grey: "bg-band-grey",
  white: "bg-band-white",
  gold: "bg-band-gold",
  silver: "bg-band-silver",
};

/** CSS class for a band fill (theme-stable). */
export function bandClass(band: BandName): string {
  return BAND_BG[band];
}

/** One multiplier band per price, the stripe a real resistor uses for
 *  magnitude, not a stripe per digit. The digits are printed regardless, and
 *  no shopper decodes them. ×1 black through ×100k green. */
export function magnitudeBand(priceUsd: number): BandName {
  if (priceUsd < 10) return "black";
  if (priceUsd < 100) return "brown";
  if (priceUsd < 1000) return "red";
  if (priceUsd < 10000) return "orange";
  if (priceUsd < 100000) return "yellow";
  return "green";
}

export const MAGNITUDE_LABEL: Partial<Record<BandName, string>> = {
  black: "single dollars",
  brown: "tens",
  red: "hundreds",
  orange: "thousands",
  yellow: "ten-thousands",
  green: "hundred-thousands",
};

export type StockState = "in" | "low" | "out";

export function stockState(qty: number): StockState {
  if (qty <= 0) return "out";
  if (qty <= 4) return "low";
  return "in";
}

export const STOCK_BAND: Record<StockState, BandName> = {
  in: "green",
  low: "yellow",
  out: "red",
};

export const STOCK_LABEL: Record<StockState, string> = {
  in: "in bin",
  low: "low stock",
  out: "bin empty",
};

/** Category slug to band assignment, fixed by the legend. One hue, one meaning. */
export const CATEGORY_BAND: Record<string, BandName> = {
  laptops: "brown",
  smartphones: "orange",
  monitors: "blue",
  keyboards: "green",
  mice: "grey",
  headphones: "violet",
  components: "red",
  accessories: "gold",
};

export function bandForCategory(slug: string | null | undefined): BandName {
  return (slug && CATEGORY_BAND[slug]) || "grey";
}
