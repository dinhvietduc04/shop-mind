import type { CartItemDto } from "@/lib/api/types";
import { available } from "./inventory";
import { findProduct } from "./catalog";
import { registerStore } from "./snapshot";

/** Cart domain — the per-user cart. Reads prices from the live catalog row;
 *  the QUOTE is where anything becomes money (server-recalculated). */

const carts = new Map<string, Map<string, number>>(); // userId -> productId -> qty

registerStore({
  name: "carts",
  dump: () => [...carts.entries()].map(([u, m]) => [u, [...m.entries()]] as [string, [string, number][]]),
  restore: (v) => (v as [string, [string, number][]][]).forEach(([u, entries]) => carts.set(u, new Map(entries))),
  clear: () => carts.clear(),
});

function cartMap(userId: string): Map<string, number> {
  let m = carts.get(userId);
  if (!m) {
    m = new Map();
    carts.set(userId, m);
  }
  return m;
}

export function getCart(userId: string): CartItemDto[] {
  const m = cartMap(userId);
  const items: CartItemDto[] = [];
  for (const [productId, quantity] of m) {
    const p = findProduct(productId);
    if (!p) continue;
    items.push({
      id: `ci-${productId}`,
      productId,
      productName: p.name,
      sku: p.sku,
      quantity,
      unitPrice: p.price,
      subtotal: +(p.price * quantity).toFixed(2),
      imageUrl: p.images.find((i) => i.url)?.url || null,
    });
  }
  return items;
}

/** FK refuse for the product-delete guard (composed by the handlers). */
export function cartHolds(productId: string): boolean {
  for (const m of carts.values()) if (m.has(productId)) return true;
  return false;
}

export function cartAdd(
  userId: string,
  productId: string,
  qty: number
): { ok: true; items: CartItemDto[] } | { ok: false; error: string } {
  const p = findProduct(productId);
  if (!p) return { ok: false, error: "Product not found." };
  if (qty <= 0) return { ok: false, error: "Quantity must be greater than zero." };
  const m = cartMap(userId);
  const newQty = (m.get(productId) ?? 0) + qty;
  if (available(productId) < newQty)
    return { ok: false, error: `Insufficient stock for ${p.name} (available ${available(productId)}, requested ${newQty}).` };
  m.set(productId, newQty);
  return { ok: true, items: getCart(userId) };
}

export function cartUpdate(
  userId: string,
  productId: string,
  qty: number
): { ok: true; items: CartItemDto[] } | { ok: false; error: string } {
  if (qty <= 0) return { ok: false, error: "Quantity must be greater than zero." };
  if (available(productId) < qty)
    return { ok: false, error: `Insufficient stock (available ${available(productId)}, requested ${qty}).` };
  cartMap(userId).set(productId, qty);
  return { ok: true, items: getCart(userId) };
}

export function cartRemove(userId: string, productId: string): CartItemDto[] {
  cartMap(userId).delete(productId);
  return getCart(userId);
}

export function cartClear(userId: string): void {
  cartMap(userId).clear();
}
