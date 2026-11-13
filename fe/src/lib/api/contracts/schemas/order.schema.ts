import { z } from "zod";
import { orderStatusSchema } from "./common.schema";

export const updateInventorySchema = z.object({
  quantity: z.number().int().nonnegative(),
});

export const updateOrderStatusSchema = z.object({
  status: orderStatusSchema,
});

export type UpdateInventoryRequest =
  z.infer<typeof updateInventorySchema>;

export type UpdateOrderStatusRequest =
  z.infer<typeof updateOrderStatusSchema>;