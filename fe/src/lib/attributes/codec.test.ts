import { describe, expect, it } from "vitest";
import {
  decodeSpecs,
  encodeChipState,
  encodeSpecs,
  matchesSpecs,
  type SpecClause,
} from "./codec";
import { facetPool, specLabel, specRegistered } from "./registry";

/* One grammar between every producer and the matcher, and it fails CLOSED:
   an unknown key, op, or value is an error, never a dropped clause. */

describe("codec round trips", () => {
  it("encodes chips to the wire grammar", () => {
    expect(encodeChipState({ panel: ["IPS"] })).toBe("panel:eq:IPS");
    expect(encodeChipState({ panel: ["IPS", "VA"], layout: ["full"] })).toBe(
      "layout:eq:full;panel:in:IPS|VA"
    );
    expect(encodeSpecs([{ key: "ram_gb", op: "gte", value: 16 }])).toBe("ram_gb:gte:16");
  });

  it("decodes what it encodes", () => {
    const d = decodeSpecs("ram_gb:gte:16;panel:in:IPS|VA;wireless:eq:true");
    expect(d.ok).toBe(true);
    if (!d.ok) return;
    expect(d.clauses).toEqual([
      { key: "ram_gb", op: "gte", value: 16 },
      { key: "panel", op: "in", value: ["IPS", "VA"] },
      { key: "wireless", op: "eq", value: true },
    ]);
  });

  it("empty and blank input decode to zero clauses (no filter is not an error)", () => {
    expect(decodeSpecs("")).toEqual({ ok: true, clauses: [] });
  });
});

describe("codec fails CLOSED", () => {
  it("rejects unregistered keys", () => {
    const d = decodeSpecs("battery_years:gte:5");
    expect(d.ok).toBe(false);
    if (!d.ok) expect(d.error).toContain("not a registered spec");
  });

  it("rejects ops that do not fit the registry type", () => {
    expect(decodeSpecs("panel:gte:3").ok).toBe(false); // enum can't be ordered
    expect(decodeSpecs("ram_gb:in:8|16").ok).toBe(false); // range: use gte/lte/eq
  });

  it("rejects non-numeric values for range keys and non-boolean values for bool keys", () => {
    expect(decodeSpecs("ram_gb:gte:lots").ok).toBe(false);
    expect(decodeSpecs("wireless:eq:maybe").ok).toBe(false);
  });

  it("rejects malformed clauses instead of ignoring them", () => {
    expect(decodeSpecs("ram_gb>=16").ok).toBe(false); // the OLD grammar is now an error
    expect(decodeSpecs("cpu").ok).toBe(false);
  });
});

describe("codec matching", () => {
  const specs = { ram_gb: 16, cpu: "Intel Core i7", wireless: true } as const;
  const c = (key: string, op: SpecClause["op"], value: SpecClause["value"]): SpecClause =>
    ({ key, op, value });

  it("ANDs across clauses", () => {
    expect(matchesSpecs(specs, [c("ram_gb", "gte", 16), c("cpu", "eq", "intel core i7")])).toBe(true);
    expect(matchesSpecs(specs, [c("ram_gb", "gte", 32)])).toBe(false);
  });

  it("in matches any listed value; missing keys never match", () => {
    expect(matchesSpecs(specs, [c("cpu", "in", ["Apple M3", "Intel Core i7"])])).toBe(true);
    expect(matchesSpecs({ storage_gb: 512 }, [c("ram_gb", "gte", 8)])).toBe(false);
    expect(matchesSpecs(undefined, [c("ram_gb", "gte", 8)])).toBe(false);
  });

  it("bool eq is truth-strict", () => {
    expect(matchesSpecs(specs, [c("wireless", "eq", true)])).toBe(true);
    expect(matchesSpecs(specs, [c("wireless", "eq", false)])).toBe(false);
  });
});

describe("attribute registry", () => {
  it("labels come from the registry, mangle is the fallback", () => {
    expect(specLabel("ram_gb")).toBe("RAM (GB)");
    expect(specLabel("4k_hdmi")).toBe("4K HDMI");
    expect(specRegistered("battery_years")).toBe(false);
    expect(specLabel("battery_years")).toBe("battery years");
  });

  it("facetPool keeps registered keys with populated values, so unregistered and bool keys stay invisible", () => {
    const pool = facetPool([
      { specs: { ram_gb: 16, halo_ring: true, battery_years: 10 } },
      { specs: { ram_gb: 8, cpu: "M3" } },
    ]);
    const keys = pool.map((p) => p.def.key);
    expect(keys).toContain("ram_gb");
    expect(keys).not.toContain("battery_years"); // unregistered
    expect(keys).not.toContain("halo_ring"); // bools never become questions
    const ram = pool.find((p) => p.def.key === "ram_gb")!;
    expect(ram.numeric).toBe(true);
    expect([ram.min, ram.max]).toEqual([8, 16]);
  });
});
