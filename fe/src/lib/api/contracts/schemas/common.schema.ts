import { z } from "zod";

export const userRoleSchema = z.enum(["Customer", "Admin"]);

export const productStatusSchema = z.enum([
    "Active",
    "Inactive",
    "Draft",
])

export const orderStatusSchema = z.enum([
    "Pending",
    "Paid",
    "Processing",
    "Shipped",
    "Delivered",
    "Cancelled",
    "PaymentFailed",
])

export const paymentStatusSchema = z.enum([
    "Failed",
    "Succeeded",
    "Pending",
])

export const paymentOutcomeSchema = z.enum([
    "captured",
    "failed",
    "expired",
])

export const productImageRequestSchema = z.object({
    url: z.url(),
    altText: z.string().nullable(),
    displayOrder: z.number().int(),
})

export type CreateProductImageRequest =
    z.infer<typeof productImageRequestSchema>;

export const specsSchema = z.record(
    z.string(),
    z.union([
        z.string(),
        z.number(),
        z.boolean(),
        z.null(),
    ])
)