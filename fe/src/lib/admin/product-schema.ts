import { z } from "zod";

/**
 * Product intake — the ONE field description (C9).
 *
 * Before, what made a product write legal was stated three times: zod
 * strings in the form, near-identical English messages in the mock db, and a
 * PascalCase→allowlist mapping back. Here it is stated once; the form uses
 * it for pre-checks and the mock server uses it as authority — and the
 * error keys are camelCase, exactly like the real backend's
 * `errors:{camelField:[messages]}` problem map (ExceptionHandlingMiddleware.cs:71–81).
 */

export const scalar = z.union([z.string(), z.number(), z.boolean(), z.null()]);

/** A3 specs contract: key → scalar. Never nested JSON — the facets engine
 *  can't read that. */
export const specsSchema = z.record(
  z.string().regex(/^[a-z0-9_]+$/, "keys are lower_snake_case"),
  scalar
);

const productFields = z.object({
  name: z.string().trim().min(1, "Name is required."),
  slug: z.string().trim().optional(),
  sku: z.string().trim().min(1, "SKU is required."),
  brand: z.string().trim().optional(),
  categoryId: z.string().trim().min(1, "Pick a category."),
  status: z.enum(["Draft", "Active", "Inactive"]),
  price: z.coerce.number().positive("Price must be greater than zero."),
  description: z.string().trim().optional(),
});

const stockField = z.coerce
  .number()
  .int("Stock must be whole units.")
  .min(0, "Initial stock cannot be negative.");

export type ProductProblem = Record<string, string[]>;

/**
 * Validate a product write. Returns null when clean, else the backend-shaped
 * problem map. `withStock` checks the stock number (create carries it; the
 * update path sends it only through the inventory PATCH — the intake module
 * decides).
 */
export function validateProductWrite(
  input: Record<string, unknown>,
  opts: { withStock?: boolean } = {}
): ProductProblem | null {
  const problems: ProductProblem = {};
  const push = (key: string, msg: string) => (problems[key] ??= []).push(msg);

  const fields = productFields.safeParse(input);
  if (!fields.success) for (const i of fields.error.issues) push(String(i.path[0] ?? ""), i.message);

  if (opts.withStock) {
    const stock = stockField.safeParse(input.initialStock ?? input.stock);
    if (!stock.success) for (const i of stock.error.issues) push("initialStock", i.message);
  }

  if ("specs" in input && input.specs !== undefined && input.specs !== null) {
    const specs = specsSchema.safeParse(input.specs);
    if (!specs.success) push("specs", specs.error.issues[0]?.message ?? "Specs must be a flat JSON object of scalars.");
  }

  return Object.keys(problems).length ? problems : null;
}

/** The write result grammar for product routes (replaces the old
 *  single-error AdminResult on this path). */
export type WriteResult<T> =
  | { ok: true; value: T }
  | { ok: false; status?: number; title?: string; errors: Record<string, string[]> };
