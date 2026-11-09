import type { CategoryDto, CreateProductRequest, InventoryDto, Specs, UpdateProductRequest } from "@/lib/api/types";
import { slugify as slugBase } from "@/lib/slug";
import { FIXTURE_CATEGORIES, FIXTURE_PRODUCTS, type FixtureProduct } from "../fixtures";
import { validateProductWrite, type WriteResult } from "@/lib/admin/product-schema";
import { forgetStock, seedStock, stockRow } from "./inventory";
import { registerStore, uuid, type AdminResult } from "./snapshot";

export { type AdminResult };
export { type WriteResult };
export const slugify = slugBase;

/** Catalog domain: the mutable product/category registry (F6). Everything —
 *  public storefront, agent engine, admin CRUD — reads these accessors.
 *  FK-refuse checks that need OTHER domains (live carts, open quotes) are
 *  composed by the HTTP handlers, not imported here — no cycles, no god file. */

const products = new Map<string, FixtureProduct>(FIXTURE_PRODUCTS.map((p) => [p.id, p]));
const categories = new Map<string, CategoryDto>(FIXTURE_CATEGORIES.map((c) => [c.id, c]));

registerStore({
  name: "products",
  dump: () => [...products.entries()],
  restore: (v) => {
    products.clear();
    (v as [string, FixtureProduct][]).forEach(([k, p]) => products.set(k, p));
  },
  clear: () => {
    products.clear();
    FIXTURE_PRODUCTS.forEach((p) => products.set(p.id, p));
  },
});

registerStore({
  name: "categories",
  dump: () => [...categories.entries()],
  restore: (v) => {
    categories.clear();
    (v as [string, CategoryDto][]).forEach(([k, c]) => categories.set(k, c));
  },
  clear: () => {
    categories.clear();
    FIXTURE_CATEGORIES.forEach((c) => categories.set(c.id, c));
  },
});

export function allProducts(): FixtureProduct[] {
  return [...products.values()];
}

export function allCategories(): CategoryDto[] {
  return [...categories.values()];
}

export function findProduct(idOrSlug: string): FixtureProduct | undefined {
  return products.get(idOrSlug) ?? [...products.values()].find((p) => p.slug === idOrSlug);
}

/** Public surface = Active only (Draft/Inactive are admin-visible, §5.6). */
export function activeProducts(): FixtureProduct[] {
  return allProducts().filter((p) => p.status === "Active");
}

/** Attach the LIVE available stock to a catalog row. */
export function liveProduct(p: FixtureProduct): FixtureProduct {
  const row = stockRow(p.id);
  return { ...p, availableStock: row ? row.quantity - row.reserved : 0 };
}

export function inventoryFor(productId: string): InventoryDto | null {
  const p = products.get(productId);
  const row = stockRow(productId);
  if (!p || !row) return null;
  return {
    productId,
    productName: p.name,
    sku: p.sku,
    quantity: row.quantity,
    reservedQuantity: row.reserved,
    availableQuantity: row.quantity - row.reserved,
  };
}

/* ------------------------------ product CRUD ------------------------------
 * C9: field legality comes from the SHARED intake schema (one statement of
 * the rules — same messages the form shows); this module adds only the
 * domain facts zod cannot know: uniqueness, category existence, and the row
 * build. Problem maps are camelCase, like the real
 * ExceptionHandlingMiddleware output — the form consumes them unmapped. */

function productProblems(req: CreateProductRequest | UpdateProductRequest, withStock: boolean) {
  const problems = validateProductWrite(req as unknown as Record<string, unknown>, { withStock });
  if (problems) return { ok: false as const, title: "Validation failed.", errors: problems };
  return null;
}

export function adminCreateProduct(req: CreateProductRequest & { specs?: Specs }): WriteResult<FixtureProduct> {
  const bad = productProblems(req, true);
  if (bad) return bad;
  const name = req.name!.trim();
  const sku = req.sku!.trim().toUpperCase();
  const slug = req.slug?.trim() ? slugify(req.slug) : slugify(name);
  const errors: Record<string, string[]> = {};
  if (!categories.has(req.categoryId!)) errors.categoryId = ["Category not found."];
  if ([...products.values()].some((p) => p.sku === sku)) errors.sku = [`SKU '${sku}' is already in use.`];
  if ([...products.values()].some((p) => p.slug === slug)) errors.slug = [`Slug '${slug}' is already in use.`];
  if (Object.keys(errors).length > 0) return { ok: false, title: "Validation failed.", errors };
  const cat = categories.get(req.categoryId!)!;
  const p: FixtureProduct = {
    id: `p-${uuid()}`,
    name,
    slug,
    sku,
    brand: req.brand?.trim() || null,
    price: +req.price!.toFixed(2),
    status: req.status,
    categoryId: cat.id,
    categoryName: cat.name,
    availableStock: req.initialStock,
    images: (req.images ?? [])
      .filter((i) => i.url?.trim())
      .map((i, n) => ({ id: `img-${uuid()}`, url: i.url.trim(), altText: i.altText?.trim() || null, displayOrder: i.displayOrder ?? n })),
    specs: req.specs ?? {},
    description: req.description?.trim() ?? "",
    quantity: req.initialStock,
    createdAt: new Date().toISOString(),
  };
  products.set(p.id, p);
  seedStock(p.id, req.initialStock);
  return { ok: true, value: p };
}

export function adminUpdateProduct(
  id: string,
  req: UpdateProductRequest & { specs?: Specs }
): WriteResult<FixtureProduct> {
  const p = products.get(id);
  if (!p) return { ok: false, status: 404, title: "Product not found.", errors: {} };
  const bad = productProblems(req, false);
  if (bad) return bad;
  const sku = req.sku!.trim().toUpperCase();
  const slug = req.slug?.trim() ? slugify(req.slug) : slugify(req.name!);
  const errors: Record<string, string[]> = {};
  if ([...products.values()].some((x) => x.id !== id && x.sku === sku)) errors.sku = [`SKU '${sku}' is already in use.`];
  if ([...products.values()].some((x) => x.id !== id && x.slug === slug)) errors.slug = [`Slug '${slug}' is already in use.`];
  const cat = categories.get(req.categoryId!);
  if (!cat) errors.categoryId = ["Category not found."];
  if (Object.keys(errors).length > 0) return { ok: false, title: "Validation failed.", errors };
  const next: FixtureProduct = {
    ...p,
    name: req.name!.trim() || p.name,
    slug,
    sku,
    brand: req.brand?.trim() || null,
    price: +req.price!.toFixed(2),
    status: req.status,
    categoryId: cat!.id,
    categoryName: cat!.name,
    description: req.description?.trim() ?? "",
    images: (req.images ?? [])
      .filter((i) => i.url?.trim())
      .map((i, n) => ({ id: p.images[n]?.id ?? `img-${uuid()}`, url: i.url.trim(), altText: i.altText?.trim() || null, displayOrder: i.displayOrder ?? n })),
    specs: req.specs ?? p.specs,
  };
  products.set(id, next);
  return { ok: true, value: next };
}

/** Hard delete, unguarded by carts/quotes here (the handler composes those
 *  FK refuses) — the catalog only drops its own rows. */
export function adminDeleteProduct(id: string): AdminResult<true> {
  if (!products.has(id)) return { ok: false, error: "Product not found." };
  products.delete(id);
  forgetStock(id);
  return { ok: true, value: true };
}

/* ----------------------------- category CRUD ------------------------------ */

interface CategoryWrite {
  name: string;
  slug?: string | null;
  description?: string | null;
  isActive: boolean;
}

export function adminCreateCategory(req: CategoryWrite): AdminResult<CategoryDto> {
  const name = req.name?.trim();
  if (!name) return { ok: false, error: "Name is required.", field: "Name" };
  const slug = req.slug?.trim() ? slugify(req.slug) : slugify(name);
  if ([...categories.values()].some((c) => c.slug === slug))
    return { ok: false, error: `Slug '${slug}' is already in use.`, field: "Slug" };
  const c: CategoryDto = { id: `c-${uuid()}`, name, slug, description: req.description?.trim() || null, isActive: req.isActive };
  categories.set(c.id, c);
  return { ok: true, value: c };
}

export function adminUpdateCategory(id: string, req: CategoryWrite): AdminResult<CategoryDto> {
  const c = categories.get(id);
  if (!c) return { ok: false, error: "Category not found." };
  const slug = req.slug?.trim() ? slugify(req.slug) : slugify(req.name);
  if ([...categories.values()].some((x) => x.id !== id && x.slug === slug))
    return { ok: false, error: `Slug '${slug}' is already in use.`, field: "Slug" };
  const next: CategoryDto = { ...c, name: req.name.trim() || c.name, slug, description: req.description?.trim() || null, isActive: req.isActive };
  categories.set(id, next);
  // denormalized categoryName lives on products — keep it in step
  for (const [pid, p] of [...products.entries()])
    if (p.categoryId === id) products.set(pid, { ...p, categoryName: next.name });
  return { ok: true, value: next };
}

export function adminDeleteCategory(id: string): AdminResult<true> {
  if (!categories.has(id)) return { ok: false, error: "Category not found." };
  if ([...products.values()].some((p) => p.categoryId === id))
    return { ok: false, error: "Products still live in this category — move or delete them first." };
  categories.delete(id);
  return { ok: true, value: true };
}
