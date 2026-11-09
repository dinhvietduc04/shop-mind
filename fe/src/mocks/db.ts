/**
 * The fake server, split by domain (C7) behind a compatibility barrel.
 *
 * What was one 714-line file with six unrelated concepts sharing one
 * interface is now: server/snapshot.ts (persistence registry), server/
 * identity.ts, server/inventory.ts (Reservation/Commit/Release engine),
 * server/catalog.ts (mutable products/categories), server/cart.ts,
 * server/quotes.ts (the quote ledger: create → guarded settle → outcome),
 * server/ai.ts (settings + §A7 ledger). Each registers its stores with the
 * snapshot registry; NONE of them persists or reads localStorage — persistence
 * happens once per request at the HTTP seam (handlers.ts calling `persist()`),
 * and loading is deferred to `initServer()` on browser boot. The quote
 * ledger's riskiest invariants are therefore plain-function testable in Node
 * (see mocks/server/quote-ledger.test.ts).
 *
 * This file keeps only the old import surface so handlers.ts / agent.ts
 * don't churn; F8 deletes all of it — the seam that survives is the route
 * list in handlers.ts and the DTO contract in lib/api/types.
 */

export { initServer, persist, resetServer as resetDb, uuid } from "./server/snapshot";
export type { AdminResult } from "./server/snapshot";

export {
  findUserRecord,
  registerUser,
  getAddress,
  setAddress,
  userByToken,
  type UserRecord,
} from "./server/identity";

export {
  available,
  adminSetStock,
  commitReservation,
  releaseReservation,
  tryReserve,
} from "./server/inventory";

export {
  activeProducts,
  adminCreateCategory,
  adminCreateProduct,
  adminDeleteCategory,
  adminDeleteProduct,
  adminUpdateCategory,
  adminUpdateProduct,
  allCategories,
  allProducts,
  findProduct,
  inventoryFor,
  liveProduct,
  slugify,
  type WriteResult,
} from "./server/catalog";

export {
  activeQuoteFor,
  adminListOrders,
  adminSetOrderStatus,
  cancelOrder,
  createQuote,
  expirePendingQuotes,
  findTokenFor,
  getQuote,
  openQuoteHolds,
  orderFor,
  ordersFor,
  QUOTE_EXPIRED_REASON,
  settleQuote,
  type QuoteRecord,
} from "./server/quotes";

export {
  cartAdd,
  cartClear,
  cartHolds,
  cartRemove,
  cartUpdate,
  getCart,
} from "./server/cart";

export {
  aiConversationsList,
  aiKillSwitchOn,
  aiLedgerList,
  getAiSettings,
  recordAiCalls,
  setAiSettings,
  toolEnabled,
} from "./server/ai";

/* Browser-side load point: this barrel is imported ONLY by the mock network
 * graph (handlers via browser.ts, agent) — never by Node tests, which import
 * the domain modules directly and get pristine fixture state. Loading HERE
 * (still deferred past any localStorage read at DOMAIN module scope, which
 * was the testability win) keeps dev Fast Refresh honest: whenever the mock
 * graph is re-evaluated, the maps come back from the snapshot before the
 * first persist() could overwrite it with fresh seeds. */
import { initServer } from "./server/snapshot";
initServer();
