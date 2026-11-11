/**
 * Client-side mirror of the assistant tool risk classes (ADR-0006). The
 * backend generates its MCP manifest from `IToolRiskPolicy` and exposes no
 * list, so this is the one client mirror behind admin hints, kill-switch
 * defaults, ledger weights, and cache repair. NEVER means absent from the
 * manifest, so the capability is missing rather than denied.
 */

export type RiskClass = "READ" | "WRITE" | "QUOTE" | "NEVER";
export type CacheTouch = "cart" | "stock";

export interface ToolDef {
  name: string;
  risk: RiskClass;
  /** human-toggleable in the admin console (NEVER tools have no switch because
   *  there is no capability to turn off) */
  switchable: boolean;
  /** wired into the FakeAiProvider today; planned tools show in the console
   *  as disabled switches, not switches that appear to work */
  implemented: boolean;
  /** audit stand-in: deterministic ledger duration per call */
  weightMs: number;
  hint: string;
  /** live-db data this tool moves (or re-syncs), which drives the turn's
   *  invalidation recipes */
  invalidates: CacheTouch[];
}

export const TOOLS: readonly ToolDef[] = [
  /* READ: public browsing */
  {
    name: "search_products",
    risk: "READ",
    switchable: true,
    implemented: true,
    weightMs: 140,
    hint: "the bins. Off means no recommendations at all",
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
    invalidates: ["cart", "stock"],
  },
  {
    name: "get_orders",
    risk: "READ",
    switchable: true,
    implemented: false,
    weightMs: 40,
    hint: "planned, not wired into the fake engine yet",
    invalidates: [],
  },
  {
    name: "get_order",
    risk: "READ",
    switchable: true,
    implemented: false,
    weightMs: 35,
    hint: "planned, not wired into the fake engine yet",
    invalidates: [],
  },
  {
    name: "get_order_status",
    risk: "READ",
    switchable: true,
    implemented: false,
    weightMs: 25,
    hint: "planned, not wired into the fake engine yet",
    invalidates: [],
  },

  /* WRITE: cart lines (still never money) */
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
    hint: "planned: line quantities, not wired into the fake engine yet",
    invalidates: ["cart", "stock"],
  },
  {
    name: "remove_from_cart",
    risk: "WRITE",
    switchable: true,
    implemented: false,
    weightMs: 55,
    hint: "planned: leaving a line, not wired into the fake engine yet",
    invalidates: ["cart", "stock"],
  },

  /* QUOTE: the Assist boundary */
  {
    name: "create_quote",
    risk: "QUOTE",
    switchable: true,
    implemented: true,
    weightMs: 180,
    hint: "opening reservations, the Assist boundary",
    invalidates: ["cart", "stock"],
  },

  /* NEVER: absent from the manifest by construction. Recorded so the admin
     console can state the boundary; they are never switchable because a pay
     tool cannot exist in the manifest. */
  {
    name: "pay",
    risk: "NEVER",
    switchable: false,
    implemented: false,
    weightMs: 0,
    hint: "absent from the manifest. Money moves only on the hosted page",
    invalidates: [],
  },
  {
    name: "refund",
    risk: "NEVER",
    switchable: false,
    implemented: false,
    weightMs: 0,
    hint: "absent from the manifest. A human handles refunds",
    invalidates: [],
  },
  {
    name: "cancel_order",
    risk: "NEVER",
    switchable: false,
    implemented: false,
    weightMs: 0,
    hint: "absent at Assist. Cancel stays a human action",
    invalidates: [],
  },
];

/** pseudo-tool marker: the ledger row that costs the model call itself. */
export const LLM_TURN = "llm_turn";

export const toolDef = (name: string): ToolDef | undefined =>
  TOOLS.find((t) => t.name === name);

/** every real tool with a switch. */
export const SWITCHABLE_TOOLS: readonly ToolDef[] = TOOLS.filter((t) => t.switchable);

/** the boundary statement for the console. */
export const NEVER_TOOLS: readonly ToolDef[] = TOOLS.filter((t) => t.risk === "NEVER");

/** implemented and switchable tools are what the fake engine honors today (db defaults). */
export const AI_TOOLS: readonly string[] = SWITCHABLE_TOOLS.filter((t) => t.implemented).map(
  (t) => t.name
);

/** deterministic ledger duration for the stand-in rows. */
export const ledgerWeight = (name: string): number => toolDef(name)?.weightMs ?? 50;

/** Did this turn move (or re-sync) anything the pages must re-read? */
export function turnTouches(trailTools: string[]): { cart: boolean; stock: boolean } {
  const touched = new Set<CacheTouch>();
  for (const name of trailTools) toolDef(name)?.invalidates.forEach((x) => touched.add(x));
  return { cart: touched.has("cart"), stock: touched.has("stock") };
}
