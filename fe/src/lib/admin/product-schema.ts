import { z } from "zod";

/**
 * One field description for product intake. The form pre-checks with it and
 * the mock server uses it as authority. Problem-map keys are camelCase to
 * match the backend's `errors:{camelField:[messages]}` map
 * (ExceptionHandlingMiddleware.cs).
 */

export const scalar = z.union([z.string(), z.number(), z.boolean(), z.null()]);

/** A3 specs contract: each key maps to a scalar. Never nested JSON, which
 *  the facets engine can't read. */
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
 * update path sends it only through the inventory PATCH, and the intake module
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

/** The write result grammar for product routes. */
export type WriteResult<T> =
  | { ok: true; value: T }
  | { ok: false; status?: number; title?: string; errors: Record<string, string[]> };
