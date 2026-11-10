/**
 * The assistant tool registry — client-side mirror of the Risk-Class
 * vocabulary (§A2, ADR-0006) (C4). The real backend generates its MCP
 * manifest from `IToolRiskPolicy` server-side and exposes NO list to
 * clients, so this registry is the single legitimate home for the mirror:
 * admin hints, kill-switch defaults, ledger weights, and the cache-repair
 * signal all read from ONE statement of what the agent may do — and what it
 * mechanically cannot (NEVER = absent from the manifest entirely: capability
 * missing, not capability denied).
 *
 * Adding a tool is now one entry here, not five dictionaries across the
 * mock/UI seam.
 */

export type RiskClass = "READ" | "WRITE" | "QUOTE" | "NEVER";
export type CacheTouch = "cart" | "stock";

export interface ToolDef {
  name: string;
  risk: RiskClass;
  /** human-toggleable in the §B3 console (NEVER tools have no switch — there
   *  is no capability to turn off) */
  switchable: boolean;
  /** wired into the FakeAiProvider today; planned tools show in the console
   *  as honestly-disabled switches, not fake-working ones */
  implemented: boolean;
  /** §A7 audit stand-in: deterministic ledger duration per call */
  weightMs: number;
  hint: string;
  /** live-db surfaces this tool moves (or re-syncs) — drives the turn's
   *  invalidation recipes */
  invalidates: CacheTouch[];
}

export const TOOLS: readonly ToolDef[] = [
  /* READ — public browsing (§A2) */
  {
    name: "search_products",
    risk: "READ",
    switchable: true,
    implemented: true,
    weightMs: 140,
    hint: "the bins — off means no recommendations at all",
    invalidates: [],
  },
  {
    name: "get_product",
    risk: "READ",
    switchable: true,
    implemented: true,
    weightMs: 45,
    hint: "spec-sheet read behind the top pick",
    invalidates: [],
  },
  {
    name: "get_product_facets",
    risk: "READ",
    switchable: true,
    implemented: true,
    weightMs: 70,
    hint: "attribute questions in the interview",
    invalidates: [],
  },
  {
    name: "compare_products",
    risk: "READ",
    switchable: true,
    implemented: true,
    weightMs: 90,
    hint: "the side-by-side table",
    invalidates: [],
  },
  {
    name: "get_cart",
    risk: "READ",
    switchable: true,
    implemented: true,
    weightMs: 30,
    hint: "reading the customer's cart",
    // preserves the F6 sniff's behavior byte-for-behavior: a cart read also
    // re-syncs the human's walls. Revisit when real cache semantics land.
    invalidates: ["cart", "stock"],
  },
  {
    name: "get_orders",
    risk: "READ",
    switchable: true,
    implemented: false,
    weightMs: 40,
    hint: "planned (§A2) — not wired into the fake engine yet",
    invalidates: [],
  },
  {
    name: "get_order",
    risk: "READ",
    switchable: true,
    implemented: false,
    weightMs: 35,
    hint: "planned (§A2) — not wired into the fake engine yet",
    invalidates: [],
  },
  {
    name: "get_order_status",
    risk: "READ",
    switchable: true,
    implemented: false,
    weightMs: 25,
    hint: "planned (§A2) — not wired into the fake engine yet",
    invalidates: [],
  },

  /* WRITE — cart lines (still never money) */
  {
    name: "add_to_cart",
    risk: "WRITE",
    switchable: true,
    implemented: true,
    weightMs: 60,
    hint: "writing cart lines (still never pays)",
    invalidates: ["cart", "stock"],
  },
  {
    name: "update_cart",
    risk: "WRITE",
    switchable: true,
    implemented: false,
    weightMs: 55,
    hint: "planned (§A2): line quantities — not wired into the fake engine yet",
    invalidates: ["cart", "stock"],
  },
  {
    name: "remove_from_cart",
    risk: "WRITE",
    switchable: true,
    implemented: false,
    weightMs: 55,
    hint: "planned (§A2): leaving a line — not wired into the fake engine yet",
    invalidates: ["cart", "stock"],
  },

  /* QUOTE — the Assist boundary */
  {
    name: "create_quote",
    risk: "QUOTE",
    switchable: true,
    implemented: true,
    weightMs: 180,
    hint: "opening reservations — the Assist boundary",
    invalidates: ["cart", "stock"],
  },

  /* NEVER — absent from the manifest by construction (§A2). Recorded so the
     admin console can STATE the boundary; they are never switchable because
     a pay tool cannot exist in the surface. */
  {
    name: "pay",
    risk: "NEVER",
    switchable: false,
    implemented: false,
    weightMs: 0,
    hint: "absent from the manifest — money moves only on the hosted page",
    invalidates: [],
  },
  {
    name: "refund",
    risk: "NEVER",
    switchable: false,
    implemented: false,
    weightMs: 0,
    hint: "absent from the manifest — human support surface",
    invalidates: [],
  },
  {
    name: "cancel_order",
    risk: "NEVER",
    switchable: false,
    implemented: false,
    weightMs: 0,
    hint: "absent at Assist — cancel stays a human action",
    invalidates: [],
  },
];

/** pseudo-tool marker: the ledger row that costs the model call itself (§A7). */
export const LLM_TURN = "llm_turn";

export const toolDef = (name: string): ToolDef | undefined =>
  TOOLS.find((t) => t.name === name);

/** the §B3 console surface: every real tool with a switch. */
export const SWITCHABLE_TOOLS: readonly ToolDef[] = TOOLS.filter((t) => t.switchable);

/** the boundary statement for the console. */
export const NEVER_TOOLS: readonly ToolDef[] = TOOLS.filter((t) => t.risk === "NEVER");

/** implemented + switchable = what the fake engine honors today (db defaults). */
export const AI_TOOLS: readonly string[] = SWITCHABLE_TOOLS.filter((t) => t.implemented).map(
  (t) => t.name
);

/** deterministic ledger duration for the §A7 stand-in rows. */
export const ledgerWeight = (name: string): number => toolDef(name)?.weightMs ?? 50;

/** Did this turn move (or re-sync) anything the pages must re-read?
 *  Replaces the F6 tool-name sniff inside the chat transport. */
export function turnTouches(trailTools: string[]): { cart: boolean; stock: boolean } {
  const touched = new Set<CacheTouch>();
  for (const name of trailTools) toolDef(name)?.invalidates.forEach((x) => touched.add(x));
  return { cart: touched.has("cart"), stock: touched.has("stock") };
}
