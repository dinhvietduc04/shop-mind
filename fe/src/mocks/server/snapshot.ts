/**
 * The fake server's persistence seam (C7).
 *
 * MSW handlers execute in the PAGE context — a reload would wipe plain Maps.
 * So the fake server persists its whole state in localStorage and survives
 * navigation, like a real backend would. `/api/_mock/reset` clears it.
 *
 * The rule this file enforces: DOMAIN MODULES NEVER PERSIST. Each domain
 * registers its (dump, restore, clear) triple here; `persist()` is called by
 * the HTTP seam (handlers.ts) after requests that may have written — and
 * nowhere else. That makes the domain modules plain in-memory code: the
 * quote-settle sequence, the oversell guard and the origin-scope replace
 * policy are callable and assertable in Node, no window, no MSW.
 *
 * F8 note: this whole file disappears with the mock server. The real seam is
 * the HTTP route list in handlers.ts.
 */

const DB_KEY = "shopmind.mockdb.v1";

const rand = () => Math.random().toString(36).slice(2, 10);
export const uuid = () => `${rand()}-${rand()}`;

/** Admin write result grammar shared by the catalog/inventory domains. */
export type AdminResult<T> = { ok: true; value: T } | { ok: false; error: string; field?: string };

export interface Store {
  name: string;
  dump: () => unknown;
  restore: (value: unknown) => void;
  clear: () => void;
}

const stores: Store[] = [];

/** Called at module scope by each domain — registration order = load order. */
export function registerStore(store: Store): void {
  stores.push(store);
}

const hasWindow = () => typeof window !== "undefined" && typeof localStorage !== "undefined";

/** Persist the whole registry. Idempotent, cheap, and the ONLY writer. */
export function persist(): void {
  if (!hasWindow()) return;
  const snap: Record<string, unknown> = {};
  for (const s of stores) snap[s.name] = s.dump();
  localStorage.setItem(DB_KEY, JSON.stringify(snap));
}

let initialized = false;

/**
 * Deferred load — NEVER at module-eval time (the old db.ts read localStorage
 * the moment it was imported, which foreclosed any Node test). The browser
 * boot (startMockApi) calls this before the worker starts; tests skip it and
 * get a pristine fixture-seeded server.
 */
export function initServer(): void {
  if (initialized) return;
  initialized = true;
  if (!hasWindow()) return;
  try {
    const raw = localStorage.getItem(DB_KEY);
    if (!raw) return;
    const snap = JSON.parse(raw) as Record<string, unknown>;
    for (const s of stores) {
      if (s.name in snap) s.restore(snap[s.name]);
    }
  } catch {
    localStorage.removeItem(DB_KEY);
  }
}

/** Full reset — every store cleared to its seeded state, snapshot dropped.
 *  (The old resetDb enumerated all ten stores by hand; now it's the registry.) */
export function resetServer(): void {
  if (hasWindow()) localStorage.removeItem(DB_KEY);
  for (const s of stores) s.clear();
}
