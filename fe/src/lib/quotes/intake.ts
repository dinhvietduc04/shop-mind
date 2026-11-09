import { useEffect } from "react";
import { useSyncExternalStore } from "react";
import { useActiveQuote, useQuote } from "@/lib/api/hooks";
import type { OrderDto, ShippingAddressDto } from "@/lib/api/types";
import { mainlineRank } from "@/lib/orders/lifecycle";

/**
 * Quote intake (C1) — the module a human's Payment Session lives in.
 *
 * Owns three things that used to be page-local folklore inside /quote:
 *  1. the session pointer: which Open Quote THIS TAB holds (sessionStorage +
 *     cross-tab event; the server stays the truth source, the pointer is a
 *     claim of attention, never authority);
 *  2. reconciliation: a pointer naming a session the server dropped, while
 *     discovery sees a live quote (e.g. one the assistant opened), re-points
 *     instead of wedging the bill on a dead-session card;
 *  3. outcome: WHAT STATE a Quote is in, decoded once from the wire truth —
 *     `payment.outcome` (§B5+ item 1) — so no screen performs transactionId
 *     archaeology or splits hostedPayUrl to find its own session token.
 */

/* ---------------------------------- demo ---------------------------------- */

/** The identity the bill falls back to when a member never saved an address.
 *  Mock convenience only: the real API should 422 a missing preset instead
 *  of letting the UI fabricate one — flagged for the B-milestone contract. */
export const DEMO_ADDRESS: ShippingAddressDto = {
  fullName: "Demo Customer",
  phone: "+1-555-0100",
  address: "123 Catalog Lane",
  city: "Vienna",
};

/* --------------------------------- outcome -------------------------------- */

export type QuoteOutcome =
  | "open" // Pending — live reservations, human may still pay
  | "paid"
  | "fulfilled" // past Paid on the mainline (Processing/Shipped/Delivered)
  | "declined" // a payment attempt failed
  | "expired" // the hold ran out — never charged, stock returned
  | "abandoned"; // a human cancelled it

/**
 * The single decode of a Quote's terminal story. Expired vs declined is
 * read from `payment.outcome` (the server's truth) — NOT from a null
 * transactionId, which was the mock's internal encoding leaking upward.
 */
export function quoteOutcome(o: OrderDto): QuoteOutcome {
  switch (o.status) {
    case "Pending":
      return "open";
    case "Paid":
      return "paid";
    case "Cancelled":
      return "abandoned";
    case "PaymentFailed":
      return o.payment?.outcome === "expired" ? "expired" : "declined";
    default:
      // only mainline statuses live past Paid — the machine says so
      return mainlineRank(o.status) > 1 ? "fulfilled" : "paid";
  }
}

/* --------------------------------- pointer -------------------------------- */

const QKEY = "shopmind.openquote";
const QEVENT = "shopmind:quote";

function publish(value: string | null) {
  if (typeof window === "undefined") return;
  if (value) sessionStorage.setItem(QKEY, value);
  else sessionStorage.removeItem(QKEY);
  window.dispatchEvent(new Event(QEVENT));
}

function subscribe(cb: () => void) {
  const handler = () => cb();
  window.addEventListener(QEVENT, handler);
  window.addEventListener("storage", handler);
  return () => {
    window.removeEventListener(QEVENT, handler);
    window.removeEventListener("storage", handler);
  };
}

/** "this tab holds the human's Payment Session" */
export const holdOpenQuote = (sessionToken: string) => publish(sessionToken);
/** park the tab: stop pointing at any session */
export const releaseOpenQuote = () => publish(null);

/** Read via useSyncExternalStore so hydration matches the server snapshot
 *  (null) and the token appears only after mount — no setState-in-effect. */
export function useOpenQuoteToken(): string | null {
  return useSyncExternalStore(
    subscribe,
    () => (typeof window === "undefined" ? null : sessionStorage.getItem(QKEY)),
    () => null
  );
}

/**
 * The held Quote: pointer first, server discovery as the fallback, then
 * reconciliation. Callers get the live view + the quote it names; cached
 * discovery must not resurrect a pointer after the session it found was
 * settled — only a SUCCESSFUL current fetch counts as "there is one".
 */
export function useHeldQuote(): {
  token: string | null;
  quote: (import("@/lib/api/types").QuoteDto & { order: OrderDto }) | null;
  isPending: boolean;
} {
  const pointer = useOpenQuoteToken();
  const active = useActiveQuote();
  const token = pointer ?? (active.isSuccess ? active.data?.sessionToken ?? null : null);
  const { data: quote, isPending } = useQuote(token ?? "");

  useEffect(() => {
    if (
      pointer &&
      !isPending &&
      !quote &&
      active.isSuccess &&
      active.data?.order.status === "Pending" &&
      active.data.sessionToken !== pointer
    ) {
      holdOpenQuote(active.data.sessionToken);
    }
  }, [pointer, quote, isPending, active.data, active.isSuccess]);

  return { token, quote: quote ?? null, isPending };
}
