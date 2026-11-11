/**
 * Persistence seam for the fake server. MSW runs in the page context, so the
 * whole state lives in localStorage and survives navigation. Domains never
 * persist. `handlers.ts` calls `persist()` after a write, and `initServer()` is
 * the only load point. Keeping the load out of module evaluation lets Node
 * tests read a pristine fixture server.
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

/** Each domain calls this at module scope; registration order is load order. */
export function registerStore(store: Store): void {
  stores.push(store);
}

const hasWindow = () => typeof window !== "undefined" && typeof localStorage !== "undefined";

/** Persist the whole registry. Idempotent, cheap, and the only writer. */
export function persist(): void {
  if (!hasWindow()) return;
  const snap: Record<string, unknown> = {};
  for (const s of stores) snap[s.name] = s.dump();
  localStorage.setItem(DB_KEY, JSON.stringify(snap));
}

let initialized = false;

/**
 * Deferred load. Browser boot (`startMockApi`) calls this before the worker
 * starts. Tests skip it and get a pristine fixture-seeded server.
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

/** Full reset. Every store returns to its seeded state, and the snapshot is
 *  dropped. */
export function resetServer(): void {
  if (hasWindow()) localStorage.removeItem(DB_KEY);
  for (const s of stores) s.clear();
}
