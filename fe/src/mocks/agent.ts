import { allCategories, allProducts, liveProduct } from "./server/catalog";
import { available } from "./server/inventory";
import { cartAdd, getCart } from "./server/cart";
import { createQuote } from "./server/quotes";
import { toolEnabled } from "./server/ai";
import type {
  AgentTurnResult,
  ChatProductRef,
  ElicitField,
} from "@/lib/api/types";
import { matchesSpecs } from "@/lib/attributes/codec";
import { facetPool, specLabel } from "@/lib/attributes/registry";
import { DEMO_ADDRESS } from "@/lib/quotes/intake";

/**
 * The mock agent is the deterministic FakeAiProvider. No LLM: intent parsing, catalog queries through the same db the REST
 * handlers use, and template prose. That is deliberate. Every number in a
 * reply is interpolated from a tool call made in this turn (plan rule #1:
 * "never invent price/stock"). Turn budget mirrors B3 (8 turns/conversation).
 */

export const TURN_CAP = 8;
/* Quote intake owns the one demo identity. The fake agent quotes
   with the same constant the bill page falls back to, not a byte-copy. */

/* ------------------------------ conversation ------------------------------ */

interface SpecIntent {
  key: string;
  op: "gte" | "lte" | "eq";
  value: number | string;
}

interface ConvState {
  turns: number;
  /** product ids of the last result set; used by "add it" and "compare these". */
  lastPicks: string[];
  intent: { category?: string; budget?: number; specs: SpecIntent[] };
}

const convs = new Map<string, ConvState>();

const freshConv = (): ConvState => ({ turns: 0, lastPicks: [], intent: { specs: [] } });

/* --------------------------------- intents -------------------------------- */

const CATEGORY_WORDS: Record<string, string[]> = {
  laptops: ["laptop", "notebook", "ultrabook", "macbook", "thinkpad", "computer"],
  smartphones: ["phone", "smartphone", "galaxy", "iphone"],
  monitors: ["monitor", "display", "screen"],
  keyboards: ["keyboard", "keys"],
  mice: ["mouse", "mice"],
  headphones: ["headphone", "headphones", "earphone", "anc", "noise cancel"],
  components: ["gpu", "graphics card", "rtx", "video card"],
  accessories: ["dock", "docking", "hub", "usb-c hub", "dongle"],
};

const USE_CASE_HINTS: Array<{ words: string[]; spec: SpecIntent; label: string }> = [
  { words: ["backend dev", "developer", "programming", "coding", "dev", "docker", "vm"], spec: { key: "ram_gb", op: "gte", value: 16 }, label: "ram_gb>=16 (dev workloads)" },
  { words: ["gaming", "games", "game"], spec: { key: "vram_gb", op: "gte", value: 12 }, label: "vram_gb>=12 (gaming)" },
  { words: ["video editing", "editing", "rendering"], spec: { key: "ram_gb", op: "gte", value: 32 }, label: "ram_gb>=32 (editing)" },
];

const money = (n: number) => `$${n.toFixed(2)}`;

function parseIntent(message: string, prior: ConvState["intent"]) {
  const t = message.toLowerCase();
  const intent: ConvState["intent"] = { ...prior, specs: [...prior.specs] };

  for (const [cat, words] of Object.entries(CATEGORY_WORDS)) {
    if (words.some((w) => t.includes(w))) {
      intent.category = cat;
      break;
    }
  }

  const budget =
    t.match(/(?:under|below|less than|up to|max|budget(?:\s+of)?|around|about)\s+\$?\s?([\d,]+(?:\.\d+)?)/) ??
    t.match(/\$\s?([\d,]+(?:\.\d+)?)/);
  if (budget) intent.budget = Number(budget[1].replace(/,/g, ""));

  for (const h of USE_CASE_HINTS) {
    if (h.words.some((w) => t.includes(w))) {
      if (!intent.specs.some((s) => s.key === h.spec.key)) intent.specs.push(h.spec);
    }
  }
  const ram = t.match(/(\d+)\s*gb(?:\s+of)?\s+ram|ram[^\d]{0,8}(\d+)\s*gb/);
  if (ram) {
    const v = Number(ram[1] ?? ram[2]);
    intent.specs = intent.specs.filter((s) => s.key !== "ram_gb").concat({ key: "ram_gb", op: "gte", value: v });
  }
  const storage = t.match(/(\d+)\s*(gb|tb)\s+(ssd|storage|drive)/);
  if (storage) {
    const v = Number(storage[1]) * (storage[2] === "tb" ? 1000 : 1);
    intent.specs = intent.specs.filter((s) => s.key !== "storage_gb").concat({ key: "storage_gb", op: "gte", value: v });
  }
  return intent;
}

function detectAction(message: string): "add" | "quote" | "compare" | "cart" | "pay" | "cancel" | null {
  const t = message.toLowerCase().trim();
  if (/\b(pay (it|this|now|for it|the bill)|just pay|charge (it|me|the card)|complete (the )?payment|send (me )?a payment link)\b/.test(t)) return "pay";
  if (/(cancel|refund)( the| my)? order/.test(t)) return "cancel";
  if (/\b(quote|reserve (the )?stock|hold (the )?(items|stock))\b/.test(t)) return "quote";
  if (/\b(add|take|grab|want|put)\b.*(it|this|one|cart)\b/.test(t) || /^add\b/.test(t)) return "add";
  if (/\bcompare\b/.test(t)) return "compare";
  if (/\b(what'?s in|show|see)( my| the)? cart\b/.test(t)) return "cart";
  return null;
}

/* --------------------------------- catalog -------------------------------- */

/* spec matching goes through the shared codec. The agent's intent clauses and
   the REST filter are the same grammar, evaluated the same way */

function searchProducts(intent: ConvState["intent"]): ChatProductRef[] {
  const rows = allProducts().map(liveProduct).filter((p) => {
    if (p.status !== "Active") return false; // drafts never enter the bins
    if (intent.category && p.categoryName?.toLowerCase() !== intent.category) return false;
    if (intent.budget != null && p.price > intent.budget) return false;
    return matchesSpecs(p.specs, intent.specs);
  });
  // deterministic ranker (A3): in-stock first, then price asc
  rows.sort((a, b) => Number(b.availableStock > 0) - Number(a.availableStock > 0) || a.price - b.price);
  return rows.map((p) => ({
    id: p.id, name: p.name, sku: p.sku, price: p.price,
    availableStock: p.availableStock, brand: p.brand, categoryName: p.categoryName,
    specs: p.specs,
  }));
}

/* --------------------------------- replies -------------------------------- */

const specDetail = (intent: ConvState["intent"]) => {
  const parts: string[] = [];
  if (intent.category) parts.push(`category=${intent.category}`);
  if (intent.budget != null) parts.push(`price<=${intent.budget}`);
  for (const s of intent.specs) parts.push(`${s.key}${s.op === "gte" ? ">=" : s.op}${s.value}`);
  return parts.join(", ") || "unfiltered";
};

/** Ask at most three targeted questions before filtering (A3). Budget first,
 *  then the category's real facet keys, never invented attributes. The key
 *  pool is the shared attribute scan: the catalog facet endpoint and the
 *  interview ask from the same keys. */
function elicitFor(category: string | undefined): ElicitField[] {
  const fields: ElicitField[] = [];
  const poolProducts = category
    ? allProducts().filter((p) => p.categoryName?.toLowerCase() === category)
    : allProducts();
  const prices = poolProducts.map((p) => p.price);
  fields.push({ key: "budget", label: "Budget ceiling (USD)", type: "range", min: 50, max: Math.ceil(Math.max(...(category ? prices : [1500]))), unit: "usd" });
  // get_product_facets switched off (admin kill switch) means attributes can't be
  // read, so the interview stops at the one question it can still ask.
  if (!toolEnabled("get_product_facets")) return fields.slice(0, 1);

  if (category) {
    let added = 0;
    for (const e of facetPool(poolProducts)) {
      if (added >= 2) break;
      fields.push({
        key: e.def.key,
        label: `${e.numeric ? "Minimum " : ""}${specLabel(e.def.key)}`,
        type: "enum",
        options: [...e.values].sort((a, b) => (typeof a === "number" && typeof b === "number" ? a - b : String(a).localeCompare(String(b)))),
      });
      added++;
    }
  } else {
    fields.push({
      key: "category", label: "Which bin?", type: "enum",
      options: allCategories().map((c) => c.slug),
    });
  }
  return fields.slice(0, 3);
}

/** The turn envelope includes values from the server: the per-conversation
 *  cap and how much of it this conversation has spent. The UI renders
 *  "turn x/N" from these instead of a hardcoded literal. */
export function runAgentTurn(userId: string, conversationId: string, message: string, answers?: Record<string, string | number | boolean>): AgentTurnResult {
  const result = agentTurnCore(userId, conversationId, message, answers);
  const turns = convs.get(conversationId)?.turns ?? 1;
  return { ...result, turnCap: TURN_CAP, turnsUsed: Math.min(turns, TURN_CAP) };
}

function agentTurnCore(userId: string, conversationId: string, message: string, answers?: Record<string, string | number | boolean>): AgentTurnResult {
  let conv = convs.get(conversationId);
  if (!conv) { conv = freshConv(); convs.set(conversationId, conv); }
  conv.turns++;

  if (conv.turns > TURN_CAP) {
    return {
      text: `That is ${TURN_CAP} turns, the per-conversation cap for one assistant session (rate limits are part of the product, not a bug). Everything in your cart and any open quote is untouched. Start a new conversation and your cart is still here.`,
      toolTrail: [],
      limitReached: true,
    };
  }

  /* elicitation answers fold into intent before parsing free text */
  if (answers) {
    if (answers.budget != null) conv.intent.budget = Number(answers.budget);
    if (typeof answers.category === "string") conv.intent.category = answers.category;
    for (const [k, v] of Object.entries(answers)) {
      if (k === "budget" || k === "category" || typeof v === "boolean") continue;
      if (k.startsWith("drop:")) {
        if (String(v) === "drop") conv.intent.specs = conv.intent.specs.filter((s) => s.key !== k.slice(5));
        continue;
      }
      const num = Number(v);
      conv.intent.specs = conv.intent.specs.filter((s) => s.key !== k).concat(
        Number.isFinite(num) ? { key: k, op: "gte", value: num } : { key: k, op: "eq", value: String(v) }
      );
    }
  }

  const intent = parseIntent(message, conv.intent);
  conv.intent = intent;
  const action = detectAction(message);
  const turn = conv.turns;

  /* --------------------------- money boundary -------------------------- */
  if (action === "pay") {
    return {
      text: `I can't pay, and it's not a permission I'm lacking. There is no tool that touches a card at this level. The bill page opens the hosted payment page; that click is yours alone.`,
      toolTrail: [],
      code: "ASSIST_LEVEL_NO_PAYMENT",
    };
  }
  if (action === "cancel") {
    return {
      text: "Cancelling an order is human-only at Assist level. The order ledger lets you do it while an order is still pending.",
      toolTrail: [],
      code: "ASSIST_LEVEL_HUMAN_ONLY",
    };
  }

  /* -------------------------------- actions -------------------------------- */
  /* Admin kill switches (/admin/ai): a switched-off tool is not silently
     skipped. The turn says so, and the ledger row records it. */
  if (action === "cart" && !toolEnabled("get_cart"))
    return { text: "The get_cart tool is switched off by an administrator right now, so I can't read your cart. The cart page itself stays yours.", toolTrail: [{ tool: "get_cart", detail: "disabled by policy" }], code: "TOOL_DISABLED" };
  if (action === "cart") {
    const items = getCart(userId);
    const subtotal = items.reduce((t, i) => t + i.subtotal, 0);
    return {
      text: items.length
        ? `Your cart holds ${items.length} line${items.length === 1 ? "" : "s"}: ${items.map((i) => `${i.productName} ×${i.quantity}`).join(", ")}. Subtotal ${money(subtotal)}.`
        : "Your cart is empty. Name a category or say what you need it for, and I'll search the bins.",
      toolTrail: [{ tool: "get_cart", detail: `${items.length} lines` }],
      cartCount: items.reduce((t, i) => t + i.quantity, 0),
    };
  }

  if (action === "add") {
    if (!toolEnabled("add_to_cart"))
      return { text: "add_to_cart is switched off by an administrator right now. The product pages still let you fill the cart yourself. That button was always a human action.", toolTrail: [{ tool: "add_to_cart", detail: "disabled by policy" }], code: "TOOL_DISABLED" };
    let targetId: string | undefined;
    const pickIdx = message.match(/add (?:#|no\.? ?)?(\d)\b/i);
    if (pickIdx) targetId = conv.lastPicks[Number(pickIdx[1]) - 1];
    if (!targetId) {
      const named = allProducts().find((p) => message.toLowerCase().includes(p.name.toLowerCase().split(" ").slice(0, 2).join(" ")));
      targetId = named?.id ?? conv.lastPicks[0];
    }
    if (!targetId) {
      return { text: "Nothing to add yet. Tell me what you're after first (a laptop under $1,200 for backend dev does exist in my bins).", toolTrail: [] };
    }
    const p = allProducts().find((x) => x.id === targetId)!;
    const before = available(targetId);
    const r = cartAdd(userId, targetId, 1);
    if (!r.ok) {
      return {
        text: `Could not add ${p.name}: ${r.error}`,
        toolTrail: [{ tool: "add_to_cart", detail: `id=${targetId} qty=1, rejected` }],
      };
    }
    return {
      text: `Added ${p.name} for ${money(p.price)}. Bin stock went from ${before} to ${available(targetId)} at the time of the call; quote-time is where the server reserves it for real.`,
      toolTrail: [{ tool: "add_to_cart", detail: `id=${targetId}, qty=1`, }, { tool: "get_cart", detail: `${r.items.length} lines` }],
      cartCount: r.items.reduce((t, i) => t + i.quantity, 0),
    };
  }

  if (action === "quote") {
    if (!toolEnabled("create_quote"))
      return { text: "create_quote is switched off by an administrator right now, so I can't open a reservation. You can create the quote yourself from the bill page.", toolTrail: [{ tool: "create_quote", detail: "disabled by policy" }], code: "TOOL_DISABLED" };
    const items = getCart(userId);
    if (items.length === 0) {
      return { text: "No quote. The cart is empty. Find something first; I can search by budget, category or spec.", toolTrail: [] };
    }
    const r = createQuote(userId, DEMO_ADDRESS, "agent");
    if (!r.ok) {
      return {
        text: `I could not open that quote: ${r.error}`,
        toolTrail: [{ tool: "create_quote", detail: "rejected" }],
        code: r.code,
      };
    }
    return {
      text: `Quote ${r.quote.order.orderNumber} is open: ${items.length} line${items.length === 1 ? "" : "s"}, total ${money(r.quote.order.totalAmount)}, stock reserved until the countdown ends. Review the bill and pay on the hosted page. I stop here by design.`,
      toolTrail: [{ tool: "create_quote", detail: `${items.length} lines, atomically reserved` }],
      quote: {
        orderNumber: r.quote.order.orderNumber,
        totalAmount: r.quote.order.totalAmount,
        itemCount: items.length,
        expiresAt: r.quote.expiresAt,
      },
      cartCount: items.reduce((t, i) => t + i.quantity, 0),
    };
  }

  /* ------------------------------- discovery ------------------------------- */
  if (intent.category) {
    if (!toolEnabled("search_products"))
      return { text: "search_products is switched off by an administrator right now. I can't read the bins, so I won't pretend to. Catalog pages and manual shopping are unaffected.", toolTrail: [{ tool: "search_products", detail: "disabled by policy" }], code: "TOOL_DISABLED" };
    /* A3: elicit specs before recommending. A fuzzy ask (no budget, no specs,
       general ask wording) gets targeted questions instead of a raw dump. */
    const fuzzy = intent.budget == null && intent.specs.length === 0 && /(looking|need|want|recommend|suggest|help|best|pick)/.test(message.toLowerCase());
    if (fuzzy && turn < TURN_CAP) {
      return {
        text: `Before I filter bin ${intent.category}, answer three quick questions so the ranking uses real attributes, not guesses (turn ${turn}).`,
        toolTrail: [{ tool: "search_products", detail: `category=${intent.category}` }, { tool: "get_product_facets", detail: `category=${intent.category}` }],
        elicit: elicitFor(intent.category),
      };
    }

    const hits = searchProducts(intent);
    conv.lastPicks = hits.map((h) => h.id);

    if (hits.length === 0) {
      /* name the two products that disagree, then offer the relax */
      const withoutBudget = searchProducts({ ...intent, budget: undefined });
      const relax = withoutBudget[0] ?? searchProducts({ ...intent, specs: [] })[0];
      if (relax && turn < TURN_CAP) {
        const whichKey = intent.specs[0]?.key;
        const want = intent.specs[0]?.value;
        const ceiling = intent.budget;
        const blocker =
          whichKey && typeof want === "number" && ceiling != null
            ? allProducts().map(liveProduct).find(
                (p) =>
                  p.categoryName?.toLowerCase() === intent.category &&
                  typeof p.specs?.[whichKey] === "number" &&
                  (p.specs![whichKey] as number) < want &&
                  p.price <= ceiling
              )
            : undefined;
        const relaxFields: ElicitField[] = [];
        if (intent.budget != null && withoutBudget[0]) {
          relaxFields.push({
            key: "budget",
            label: "Change the price range",
            type: "range",
            min: intent.budget,
            max: Math.ceil(withoutBudget[0].price * 1.2),
            unit: "usd",
          });
        }
        if (intent.specs.length) {
          relaxFields.push({
            key: `drop:${intent.specs[0].key}`,
            label: `Relax ${specLabel(intent.specs[0].key)}`,
            type: "enum",
            options: ["keep", "drop"],
          });
        }
        return {
          text: blocker
            ? `Nothing in ${intent.category} matches ${specDetail(intent)} together. The budget fits ${blocker.name} (${money(blocker.price)}) but it reads ${String(blocker.specs?.[whichKey!])} ${specLabel(whichKey!)}. That is under the ${intent.specs[0].value} you asked for. The ${intent.specs[0].value}-class one${withoutBudget.length ? `, ${withoutBudget[0].name}, costs ${money(withoutBudget[0].price)}, over budget.` : " is out of stock."} Which side do you want to give?`
            : `No ${intent.category} matches ${specDetail(intent)}, and unlike most stores I'm telling you what exists instead. Try loosening one constraint.`,
          toolTrail: [
            { tool: "search_products", detail: specDetail(intent) },
            { tool: "search_products", detail: "relaxed: budget dropped" },
          ],
          elicit: relaxFields,
        };
      }
      return { text: `Nothing in ${intent.category ?? "the catalog"} matches ${specDetail(intent)}, and there is nothing sane to relax.`, toolTrail: [{ tool: "search_products", detail: specDetail(intent) }] };
    }

    if ((action === "compare" || /compare|versus|vs\b/.test(message.toLowerCase())) && hits.length >= 2 && toolEnabled("compare_products")) {
      const two = hits.slice(0, 2);
      const specKeys = [...new Set(two.flatMap((t) => Object.keys(allProducts().find((p) => p.id === t.id)?.specs ?? {})))];
      return {
        text: `Side by side, from this turn's reads (turn ${turn}): ${two[0].name} ${money(two[0].price)} vs ${two[1].name} ${money(two[1].price)}. Say "add it" for the top one, or "quote" when the cart looks right.`,
        toolTrail: [{ tool: "search_products", detail: specDetail(intent) }, { tool: "compare_products", detail: `${two.map((t) => t.sku).join(" vs ")}` }],
        compare: { items: two, specKeys },
      };
    }

    const top = hits.slice(0, 3);
    const lines = top.map((h, i) => `${i + 1}. ${h.name} at ${money(h.price)}, ${h.availableStock} in bin (${h.brand ?? "unbranded"}${intent.specs.length ? `, ${intent.specs.map((s) => `${specLabel(s.key)} ${s.op === "gte" ? "≥" : s.op === "lte" ? "≤" : "="}${s.value}`)} matched` : ""})`);
    return {
      text: `${hits.length} match in bin ${intent.category}${intent.budget != null ? ` under ${money(intent.budget)}` : ""}, ranked in-stock then price (turn ${turn}):\n${lines.join("\n")}\n${top.length ? `My pick is ${top[0].name}. "add it" puts it in your cart; "compare" shows the top two side by side.` : ""}`,
      toolTrail: [{ tool: "search_products", detail: specDetail(intent) }, ...(top.length ? [{ tool: "get_product", detail: `top=${top[0].sku}` }] : [])],
      products: top.map((h, i) => ({ ...h, matchReason: i === 0 ? "cheapest in stock matching all filters" : "next in deterministic price order" })),
    };
  }

  /* no category and no action, so open the interview and never guess (A3) */
  return {
    text: "Tell me the bin and roughly what it's for, and I'll ask up to three sharp questions before I filter anything. That's the whole suggestion system here: attributes you state, stock I read, no black box.",
    toolTrail: [{ tool: "get_product_facets", detail: "all categories" }],
    elicit: elicitFor(undefined),
  };
}
