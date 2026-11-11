import { FIXTURE_PRODUCTS } from "../fixtures";
import { registerStore, type AdminResult } from "./snapshot";

/** Inventory domain: the Reservation/Commit/Release engine (GLOSSARY).
 *  These helpers mimic the atomic guarded UPDATEs of backend plan B1. They
 *  check and write in one step, so concurrent handler calls can never oversell.
 *  Pure in-memory: no persist(), no window. */

interface StockRow {
  quantity: number;
  reserved: number;
}

const seed = () => FIXTURE_PRODUCTS.map((p) => [p.id, { quantity: p.quantity, reserved: 0 }] as [string, StockRow]);
const stock = new Map<string, StockRow>(seed());

registerStore({
  name: "stock",
  dump: () => [...stock.entries()],
  restore: (v) => (v as [string, StockRow][]).forEach(([k, r]) => stock.set(k, r)),
  clear: () => {
    stock.clear();
    seed().forEach(([k, r]) => stock.set(k, r));
  },
});

export function available(productId: string): number {
  const row = stock.get(productId);
  return row ? row.quantity - row.reserved : 0;
}

/** Raw row view for composition (catalog assembles the InventoryDto). */
export function stockRow(productId: string): { quantity: number; reserved: number } | null {
  const row = stock.get(productId);
  return row ? { ...row } : null;
}

/** A newly created product enters the bins with its initial stock. */
export function seedStock(productId: string, quantity: number): void {
  stock.set(productId, { quantity, reserved: 0 });
}

export function forgetStock(productId: string): void {
  stock.delete(productId);
}

/** Atomic: reserve `qty` for a product. Returns false when stock is
 *  insufficient (no oversell). */
export function tryReserve(productId: string, qty: number): boolean {
  const row = stock.get(productId);
  if (!row || row.quantity - row.reserved < qty) return false;
  row.reserved += qty;
  return true;
}

/** Release a reservation back to the bins (expiry / decline / cancel). */
export function releaseReservation(productId: string, qty: number): void {
  const row = stock.get(productId);
  if (!row) return;
  row.reserved = Math.max(0, row.reserved - qty);
}

/** Atomic commit of a paid reservation: consume quantity and reserved. */
export function commitReservation(productId: string, qty: number): boolean {
  const row = stock.get(productId);
  if (!row || row.reserved < qty || row.quantity < qty) return false;
  row.quantity -= qty;
  row.reserved -= qty;
  return true;
}

/** Set total stock. Reserved stock is untouchable because open quotes hold it.
 *  Mirrors the guarded update of backend plan B1. */
export function adminSetStock(
  productId: string,
  quantity: number
): AdminResult<{ productId: string; quantity: number; reserved: number }> {
  const row = stock.get(productId);
  if (!row) return { ok: false, error: "Product not found." };
  if (!Number.isInteger(quantity) || quantity < 0)
    return { ok: false, error: "Quantity must be a non-negative integer." };
  if (quantity < row.reserved)
    return { ok: false, error: `Cannot go below reserved (${row.reserved}). Open quotes would be oversold.` };
  row.quantity = quantity;
  return { ok: true, value: { productId, quantity: row.quantity, reserved: row.reserved } };
}
