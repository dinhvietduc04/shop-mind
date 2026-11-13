import type { Specs } from "@/infrastructure/api";

/**
 * The attribute registry holds the per-key Specs vocabulary from ADR-0007,
 * mirrored client-side as `(key, type, unit, label)`. The backend owns the
 * authority for this vocabulary. The mock derives facets and elicitation from
 * here, so one rule is stated once: "Specs outside the registry do not exist
 * for facets or filtered search" (GLOSSARY: Spec).
 */

export type SpecType = "range" | "enum" | "bool";

export interface SpecDef {
  key: string;
  label: string;
  type: SpecType;
  unit?: string;
}

/** Fixture catalog seed vocabulary. Unregistered keys written by the admin stay
 *  invisible to facets and filters by design. */
const SPEC_DEFS: readonly SpecDef[] = [
  { key: "ram_gb", label: "RAM (GB)", type: "range", unit: "GB" },
  { key: "storage_gb", label: "Storage (GB)", type: "range", unit: "GB" },
  { key: "screen_in", label: "Screen (in)", type: "range", unit: "in" },
  { key: "cpu", label: "CPU", type: "enum" },
  { key: "weight_kg", label: "Weight (kg)", type: "range", unit: "kg" },
  { key: "thunderbolt", label: "Thunderbolt", type: "bool" },
  { key: "camera_mp", label: "Camera (MP)", type: "range", unit: "MP" },
  { key: "s_pen", label: "S Pen", type: "bool" },
  { key: "resolution", label: "Resolution", type: "enum" },
  { key: "usb_c", label: "USB-C", type: "bool" },
  { key: "panel", label: "Panel", type: "enum" },
  { key: "wireless", label: "Wireless", type: "bool" },
  { key: "backlit", label: "Backlit", type: "bool" },
  { key: "layout", label: "Layout", type: "enum" },
  { key: "dpi_max", label: "Max DPI", type: "range", unit: "dpi" },
  { key: "buttons", label: "Buttons", type: "range" },
  { key: "anc", label: "Noise cancelling", type: "bool" },
  { key: "battery_h", label: "Battery (h)", type: "range", unit: "h" },
  { key: "vram_gb", label: "VRAM (GB)", type: "range", unit: "GB" },
  { key: "interface", label: "Interface", type: "enum" },
  { key: "tdp_w", label: "TDP (W)", type: "range", unit: "W" },
  { key: "ports", label: "Ports", type: "range" },
  { key: "4k_hdmi", label: "4K HDMI", type: "bool" },
  { key: "pd_w", label: "Power delivery (W)", type: "range", unit: "W" },
];

const BY_KEY = new Map(SPEC_DEFS.map((d) => [d.key, d]));

export const specDef = (key: string): SpecDef | undefined => BY_KEY.get(key);
export const specRegistered = (key: string): boolean => BY_KEY.has(key);

/** The one label renderer. */
export const specLabel = (key: string): string =>
  BY_KEY.get(key)?.label ?? key.replace(/_/g, " ");

/* --------------------------- facet pool (scan) ---------------------------- */

export interface PoolEntry {
  def: SpecDef;
  /** distinct populated values, scan order (enum) */
  values: (string | number)[];
  /** true when every populated value is numeric */
  numeric: boolean;
  min?: number;
  max?: number;
}

/**
 * One scan of the registry against populated data feeds both the catalog facet
 * endpoint and the assistant's elicitation questions. Bool attributes never
 * become questions, since they filter by existence and humans do not pick
 * "true".
 */
export function facetPool(products: { specs?: Specs }[]): PoolEntry[] {
  const seen = new Map<string, PoolEntry>();
  for (const p of products) {
    for (const [k, v] of Object.entries(p.specs ?? {})) {
      if (typeof v === "boolean") continue;
      const def = specDef(k);
      if (!def) continue; // unregistered keys do not exist here
      const entry = seen.get(k) ?? { def, values: [], numeric: true };
      const sv = v as string | number;
      if (!entry.values.includes(sv)) entry.values.push(sv);
      if (typeof sv !== "number") entry.numeric = false;
      seen.set(k, entry);
    }
  }
  return [...seen.values()].map((e) =>
    e.numeric
      ? {
          ...e,
          min: Math.min(...(e.values as number[])),
          max: Math.max(...(e.values as number[])),
        }
      : e
  );
}
