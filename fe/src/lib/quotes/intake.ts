import { useEffect, useSyncExternalStore } from "react";
import { useActiveQuote, useQuote } from "@/features/checkout";
import type { OrderDto, ShippingAddressDto } from "@/infrastructure/api";
import { mainlineRank } from "@/lib/orders/lifecycle";

/**
 * Quote intake. The server holds the truth; the session pointer this tab
 * stores is a claim of attention, not authority. `payment.outcome` is the one
 * terminal-state decode.
 */

/* ---------------------------------- demo ---------------------------------- */

/** Fallback identity for a member with no saved address. The mock lets the UI
 *  fabricate one; the real API must answer 422 instead. */
export const DEMO_ADDRESS: ShippingAddressDto = {
  fullName: "Demo Customer",
  phone: "+1-555-0100",
  address: "123 Catalog Lane",
  city: "Vienna",
};

/* --------------------------------- outcome -------------------------------- */

export type QuoteOutcome =
  | "open" // Pending. The reservation is live, and a human may still pay
  | "paid"
  | "fulfilled" // past Paid on the mainline (Processing/Shipped/Delivered)
  | "declined" // a payment attempt failed
  | "expired" // the hold ran out, nothing was charged, and the stock returned
  | "abandoned"; // a human cancelled it

/**
 * The single decode of a Quote's terminal state. It reads expired versus
 * declined from `payment.outcome`, the server's source of truth.
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
      // only mainline statuses live past Paid, as the transition table says
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
 *  (null) and the token appears only after mount, with no setState-in-effect. */
export function useOpenQuoteToken(): string | null {
  return useSyncExternalStore(
    subscribe,
    () => (typeof window === "undefined" ? null : sessionStorage.getItem(QKEY)),
    () => null
  );
}

/**
 * The held Quote: pointer first, server discovery as the fallback, then
 * reconciliation. Callers get the live view and the quote it names. Cached
 * discovery must not restore a pointer after the session it found was
 * settled. Only a successful current fetch counts as "there is one".
 */
export function useHeldQuote(): {
  token: string | null;
  quote: (import("@/infrastructure/api").QuoteDto & { order: OrderDto }) | null;
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
