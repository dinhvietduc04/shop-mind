import { qk } from "./keys";
import { queryClient } from "./queryClient";

const inv = (queryKey: readonly unknown[]) => queryClient.invalidateQueries({ queryKey });

export const cartStale = () => inv(qk.cart());
export const stockWallStale = () => {
  inv(qk.productsAll());
  inv(qk.productAny());
};

export const quoteWallsStale = (sessionToken?: string) => {
  if (sessionToken) inv(qk.quote(sessionToken));
  inv(qk.orders());
};

export const activeQuoteStale = () => inv(qk.activeQuote());

export const orderLedgerStale = (id?: string) => {
  inv(qk.orders());
  if (id) inv(qk.order(id));
};

export const adminLedgerStale = (id?: string) => {
  inv(qk.adminOrdersAll());
  if (id) inv(qk.adminOrder(id));
};

export const catalogWriteStale = () => {
  stockWallStale();
  cartStale();
  inv(qk.facetsAll());
  inv(qk.categories());
  inv(qk.adminAll());
};
