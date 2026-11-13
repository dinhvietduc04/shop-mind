import { z } from "zod";
import { specsSchema } from "./common.schema";

export const chatRequestSchema = z.object({
  message: z.string().min(1),
  conversationId: z.string().min(1),
  answers: z.record(
    z.string(),
    z.union([z.string(), z.number(), z.boolean()]),
  ).optional(),
});

export const toolTrailItemSchema = z.object({
  tool: z.string(),
  detail: z.string().optional(),
});

export const chatProductRefSchema = z.object({
  id: z.string(),
  name: z.string(),
  sku: z.string(),
  price: z.number(),
  availableStock: z.number(),
  brand: z.string().nullable(),
  categoryName: z.string().nullable(),
  matchReason: z.string().optional(),
  specs: specsSchema.optional(),
});

export const elicitFieldSchema = z.discriminatedUnion("type", [
  z.object({
    key: z.literal("budget"),
    label: z.string(),
    type: z.literal("range"),
    min: z.number(),
    max: z.number(),
    unit: z.literal("usd"),
  }),
  z.object({
    key: z.string(),
    label: z.string(),
    type: z.literal("enum"),
    options: z.array(z.union([z.string(), z.number()])),
  }),
  z.object({
    key: z.string(),
    label: z.string(),
    type: z.literal("bool"),
  }),
]);

export const chatQuoteRefSchema = z.object({
  orderNumber: z.string(),
  totalAmount: z.number(),
  itemCount: z.number(),
  expiresAt: z.string(),
});

/** Final frame of a chat turn (delivered after the streamed text). */
export const agentTurnResultSchema = z.object({
  text: z.string(),
  toolTrail: z.array(toolTrailItemSchema),
  products: z.array(chatProductRefSchema).optional(),
  compare: z.object({
    items: z.array(chatProductRefSchema),
    specKeys: z.array(z.string()),
  }).optional(),
  elicit: z.array(elicitFieldSchema).optional(),
  quote: chatQuoteRefSchema.optional(),
  cartCount: z.number().optional(),
  /** per-conversation turn cap hit. The answer above is partial. */
  limitReached: z.boolean().optional(),
  /** machine code for policy refusals, e.g. QUOTE_HELD_BY_HUMAN. */
  code: z.string().optional(),
  /** server-delivered truth so the UI renders "turn x/N" without hardcoding
   *  the cap. Absent only before the first finished turn. */
  turnCap: z.number().optional(),
  turnsUsed: z.number().optional(),
});

/* ---------------- chat wire contract ----------------
 * The frame vocabulary below is shared by the producer (mocks/handlers.ts SSE)
 * and the consumer (lib/chat/transport.ts, the only parser). Shop.Api must
 * speak exactly this; nothing else in the app may know it. */
export const chatSseFrameSchema = z.discriminatedUnion("type", [
  z.object({
    type: z.literal("delta"),
    text: z.string(),
  }),
  z.object({
    type: z.literal("turn"),
    result: agentTurnResultSchema,
  }),
  z.object({
    type: z.literal("error"),
    code: z.string(),
    message: z.string(),
  }),
]);

export type ChatRequest = z.infer<typeof chatRequestSchema>;
export type AgentTurnResult = z.infer<typeof agentTurnResultSchema>;
export type ChatSseFrame = z.infer<typeof chatSseFrameSchema>;