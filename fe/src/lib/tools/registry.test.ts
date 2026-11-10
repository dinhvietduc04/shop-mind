import { describe, expect, it } from "vitest";
import {
  AI_TOOLS,
  NEVER_TOOLS,
  SWITCHABLE_TOOLS,
  TOOLS,
  ledgerWeight,
  toolDef,
  turnTouches,
} from "./registry";

/* Parity with the backend plan §A2 risk tiers (AI_ASSISTANT_PLAN.md:224–231)
   and ADR-0006: NEVER means ABSENT, so it must carry no switch. */
describe("tool registry — §A2 parity", () => {
  it("risk classes match the plan exactly", () => {
    const READ = ["search_products", "get_product", "get_product_facets", "compare_products", "get_cart", "get_orders", "get_order", "get_order_status"];
    const WRITE = ["add_to_cart", "update_cart", "remove_from_cart"];
    for (const n of READ) expect(toolDef(n)?.risk).toBe("READ");
    for (const n of WRITE) expect(toolDef(n)?.risk).toBe("WRITE");
    expect(toolDef("create_quote")?.risk).toBe("QUOTE");
    for (const n of ["pay", "refund", "cancel_order"]) expect(toolDef(n)?.risk).toBe("NEVER");
  });

  it("NEVER tools are not switchable — capability absent is not a toggle", () => {
    expect(NEVER_TOOLS.length).toBeGreaterThan(0);
    for (const t of NEVER_TOOLS) expect(t.switchable).toBe(false);
  });

  it("every switchable tool has a hint and a deterministic weight", () => {
    for (const t of SWITCHABLE_TOOLS) {
      expect(t.hint.length).toBeGreaterThan(0);
      expect(t.implemented ? t.weightMs > 0 : true).toBe(true);
    }
  });

  it("the kill-switch defaults equal the seven tools the fake engine wires", () => {
    expect(AI_TOOLS).toEqual([
      "search_products",
      "get_product",
      "get_product_facets",
      "compare_products",
      "get_cart",
      "add_to_cart",
      "create_quote",
    ]);
  });

  it("unknown tools fall back to the default ledger weight", () => {
    expect(ledgerWeight("some_future_tool")).toBe(50);
    expect(TOOLS.every((t) => t.name === t.name.trim())).toBe(true);
  });
});

describe("turnTouches — the registry answers what the transport used to sniff", () => {
  it("flags cart writes and quote creation", () => {
    expect(turnTouches(["search_products", "add_to_cart"])).toEqual({ cart: true, stock: true });
    expect(turnTouches(["create_quote"])).toEqual({ cart: true, stock: true });
  });

  it("preserves the F6 get_cart re-sync semantics (behavior-pinned)", () => {
    expect(turnTouches(["get_cart"]).cart).toBe(true);
  });

  it("stays quiet on pure catalog reads", () => {
    expect(turnTouches(["search_products", "get_product", "compare_products", "get_product_facets"])).toEqual({
      cart: false,
      stock: false,
    });
  });

  it("planned WRITE tools would repair the cache the same way when wired", () => {
    expect(turnTouches(["update_cart", "remove_from_cart"])).toEqual({ cart: true, stock: true });
  });
});
