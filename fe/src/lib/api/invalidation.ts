import { qk } from "./keys";
import { queryClient } from "./queryClient";

/**
 * Invalidation recipes. A write that moves stock leaves every stock tile
 * stale, so the rule is named once here and mutations call the recipe instead
 * of repeating key arrays. They read the app-singleton `queryClient`, so call
 * sites pass no `qc`.
 */
const inv = (queryKey: readonly unknown[]) =>
  queryClient.invalidateQueries({ queryKey });

/** A cart line changed. The masthead count and the bill are stale. */
export const cartStale = () => inv(qk.cart());

/** Stock moved (reserved / committed / released), so the whole wall is stale. */
export const stockWallStale = () => {
  inv(qk.productsAll());
  inv(qk.productAny());
};

/** The Quote's state moved, so the held bill, the discovery pointer, and the
 *  order ledger re-read. */
export const quoteWallsStale = (sessionToken?: string) => {
  if (sessionToken) inv(qk.quote(sessionToken));
  inv(qk.orders());
};

/** Discovery pointer only, for chat-turn repair. Don't refetch the whole ledger. */
export const activeQuoteStale = () => inv(qk.activeQuote());

/** The customer's order list and one row. */
export const orderLedgerStale = (id?: string) => {
  inv(qk.orders());
  if (id) inv(qk.order(id));
};

/** The admin's order list and one row. */
export const adminLedgerStale = (id?: string) => {
  inv(qk.adminOrdersAll());
  if (id) inv(qk.adminOrder(id));
};

/** A catalog write happened, so the storefront wall, sheets, facets, category
 *  rail, admin tables and any cart (lines may disappear on delete) are all
 *  stale. The admin hooks and the product intake module both call this. */
export const catalogWriteStale = () => {
  stockWallStale();
  cartStale();
  inv(qk.facetsAll());
  inv(qk.categories());
  inv(qk.adminAll());
};
