import { z } from "zod";
import { productImageRequestSchema, productStatusSchema } from "./common.schema";

export const productQuerySchema = z.object({
    search: z.string().optional(),
    /** slug or id */
    category: z.string().optional(),
    brand: z.string().optional(),
    minPrice: z.number().nonnegative().optional(),
    maxPrice: z.number().nonnegative().optional(),
    inStockOnly: z.boolean().optional(),
    /** Spec filter DSL (one codec in lib/attributes): clauses
     *  `key:op:value` joined by ";", `in` values joined by "|".
     *  Unregistered key or malformed clause fails with 400 (fail-closed). */
    specs: z.string().optional(),

    sort: z.enum([
        "price_asc",
        "price_desc",
        "newest",
        "name_asc",
        "name_desc",
    ]).optional(),

    page: z.number().int().positive().optional(),
    /** server cap 100 */
    pageSize: z.number().positive().int().max(100).optional(),
}).refine(
    (value) => 
        value.minPrice === undefined ||
        value.maxPrice === undefined ||
        value.minPrice <= value.maxPrice,
        {
            message: "Minimum price cannot exceed maximum price.",
            path: ["maxPrice"],
        }
)

const productFields = {
  name: z.string().min(1),
  slug: z.string().nullable(),
  sku: z.string().min(1),
  categoryId: z.string().min(1),
  description: z.string().nullable(),
  brand: z.string().nullable(),
  price: z.number().nonnegative(),
  status: productStatusSchema,
  images: z.array(productImageRequestSchema).nullable(),
};

export const createProductSchema = z.object({
  ...productFields,
  initialStock: z.number().int().nonnegative(),
});

export const updateProductSchema = z.object(productFields);

export const createCategorySchema = z.object({
  name: z.string().min(1),
  slug: z.string().nullable(),
  description: z.string().nullable(),
  isActive: z.boolean(),
});

export const updateCategorySchema = createCategorySchema;

export type ProductQuery = z.infer<typeof productQuerySchema>;
export type CreateProductRequest = z.infer<typeof createProductSchema>;
export type UpdateProductRequest = z.infer<typeof updateProductSchema>;
export type CreateCategoryRequest = z.infer<typeof createCategorySchema>;
export type UpdateCategoryRequest = z.infer<typeof updateCategorySchema>;