import { specDef } from "./registry";
import type { Specs } from "@/lib/api/types";

/**
 * The Spec filter codec (C8) — ONE grammar between every producer (UI chips,
 * any future REST caller) and the one consumer (mock search matching).
 *
 * The wire shape mirrors the backend MCP arg contract one-to-one
 * (AI_ASSISTANT_PLAN §A3 / §B5+ 4): each clause is `<key>:<op>:<value>`,
 * clauses joined by `;`, `in` values joined by `|`.
 *
 *     ?specs=ram_gb:gte:16;brand:in:Lenovo|Dell
 *     ops by registry type — range: gte|lte|eq · enum: eq|in · bool: eq
 *
 * FAIL-CLOSED: an unregistered key, an unknown op, or a non-coercible value
 * is an ERROR the endpoint answers with 400 — never a silently-ignored
 * clause. (The old mock parsed one grammar while the UI wrote another and
 * dropped anything it couldn't read: chip filters silently no-op'd.)
 */

export type SpecOp = "gte" | "lte" | "eq" | "in";

export interface SpecClause {
  key: string;
  op: SpecOp;
  value: number | string | boolean | string[];
}

const ALLOWED_OPS: Record<"range" | "enum" | "bool", readonly SpecOp[]> = {
  range: ["gte", "lte", "eq"],
  enum: ["eq", "in"],
  bool: ["eq"],
};

export type Decoded =
  | { ok: true; clauses: SpecClause[] }
  | { ok: false; error: string };

/** Serialize clauses to the REST grammar. Empty list → "". */
export function encodeSpecs(clauses: SpecClause[]): string {
  return clauses
    .map((c) =>
      c.op === "in"
        ? `${c.key}:in:${(c.value as string[]).join("|")}`
        : `${c.key}:${c.op}:${String(c.value)}`
    )
    .join(";");
}

/** Encode the UI's chip state (key → selected values). */
export function encodeChipState(specs: Record<string, string[]>): string {
  const clauses: SpecClause[] = Object.entries(specs)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, values]) =>
      values.length > 1 ? { key, op: "in" as const, value: values } : { key, op: "eq" as const, value: values[0] }
    );
  return encodeSpecs(clauses);
}

/** Parse + validate against the registry. Any violation is an error, not a shrug. */
export function decodeSpecs(raw: string): Decoded {
  const bad = (error: string): Decoded => ({ ok: false, error });
  const clauses: SpecClause[] = [];
  for (const part of raw.split(";").filter((p) => p.trim() !== "")) {
    const seg = part.trim();
    const first = seg.indexOf(":");
    const second = seg.indexOf(":", first + 1);
    if (first <= 0 || second <= first) return bad(`clause "${seg}" is not key:op:value`);
    const key = seg.slice(0, first);
    const op = seg.slice(first + 1, second) as SpecOp;
    const valueRaw = seg.slice(second + 1);
    const def = specDef(key);
    if (!def) return bad(`"${key}" is not a registered spec`);
    if (!ALLOWED_OPS[def.type].includes(op))
      return bad(`op "${op}" is not allowed for ${def.type} spec "${key}"`);
    if (valueRaw === "") return bad(`"${key}" has no value`);

    if (op === "in") {
      clauses.push({ key, op, value: valueRaw.split("|").filter((v) => v !== "") });
      continue;
    }
    if (def.type === "range") {
      const n = Number(valueRaw);
      if (!Number.isFinite(n)) return bad(`"${key}" needs a number, got "${valueRaw}"`);
      clauses.push({ key, op, value: n });
      continue;
    }
    if (def.type === "bool") {
      if (valueRaw !== "true" && valueRaw !== "false")
        return bad(`"${key}" needs true/false, got "${valueRaw}"`);
      clauses.push({ key, op, value: valueRaw === "true" });
      continue;
    }
    clauses.push({ key, op, value: valueRaw });
  }
  return { ok: true, clauses };
}

/** Does a product's Specs satisfy EVERY clause (AND across keys)? */
export function matchesSpecs(specs: Specs | undefined, clauses: SpecClause[]): boolean {
  return clauses.every((c) => {
    const actual = specs?.[c.key];
    if (actual === undefined) return false;
    if (c.op === "in") {
      const list = c.value as string[];
      return list.some((v) => String(actual).toLowerCase() === v.toLowerCase());
    }
    if (c.op === "gte" || c.op === "lte") {
      if (typeof actual !== "number" || typeof c.value !== "number") return false;
      return c.op === "gte" ? actual >= c.value : actual <= c.value;
    }
    // eq — bool-aware, everything else case-insensitive string compare
    if (typeof actual === "boolean") return actual === c.value;
    return String(actual).toLowerCase() === String(c.value).toLowerCase();
  });
}
