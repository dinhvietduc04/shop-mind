import { qk } from "./keys";
import { queryClient } from "./queryClient";

/**
 * Invalidation recipes (C6): "this write moves stock ⇒ every stock tile is
 * stale" was written six times across five hooks and — worst — foreign
 * re-implemented inside the chat store. Each recipe is named once; mutations
 * and the turn transport CALL recipes instead of repeating arrays.
 *
 * They read the app-singleton queryClient (the one Providers passes), so
 * call sites need no qc parameter.
 */
const inv = (queryKey: readonly unknown[]) =>
  queryClient.invalidateQueries({ queryKey });

/** A cart line changed — the masthead count and the bill are stale. */
export const cartStale = () => inv(qk.cart());

/** Stock moved (reserved / committed / released): the whole wall is stale. */
export const stockWallStale = () => {
  inv(qk.productsAll());
  inv(qk.productAny());
};

/** The Quote's state moved — the held bill + discovery + the ledger re-read. */
export const quoteWallsStale = (sessionToken?: string) => {
  if (sessionToken) inv(qk.quote(sessionToken));
  inv(qk.orders());
};

/** Discovery pointer only (chat-turn repair — don't drag the whole ledger). */
export const activeQuoteStale = () => inv(qk.activeQuote());

/** The customer's order ledger — list plus one row. */
export const orderLedgerStale = (id?: string) => {
  inv(qk.orders());
  if (id) inv(qk.order(id));
};

/** The admin's order desk — list plus one row. */
export const adminLedgerStale = (id?: string) => {
  inv(qk.adminOrdersAll());
  if (id) inv(qk.adminOrder(id));
};

/** A catalog write happened: the storefront wall, sheets, facets, category
 *  rail, admin tables and any cart (lines may disappear on delete) are all
 *  stale — "edit in one tab, correct in the other" (F6 contract). Both the
 *  admin hooks and the product intake module call THIS recipe. */
export const catalogWriteStale = () => {
  stockWallStale();
  cartStale();
  inv(qk.facetsAll());
  inv(qk.categories());
  inv(qk.adminAll());
};
