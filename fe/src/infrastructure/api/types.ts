/**
 * DTO contracts mirrored from backend/Shop.Application/Dtos/Dtos.cs and
 * Common/PagedResult.cs. The UI must not invent fields. Money is a JSON number
 * (decimal 18,2 server-side); display always through formatUsd().
 *
 * This module owns the response models only. Requests and the schema-owned
 * types (UserDto, AuthResponse, AgentTurnResult, ChatSseFrame, ProductQuery…)
 * are not defined here and are not re-exported here: import them from
 * ./contracts/schemas, where the Zod schema is their single definition.
 */

export type UserRole = "Customer" | "Admin";
export type ProductStatus = "Draft" | "Active" | "Inactive";
export type OrderStatus =
  | "Pending"
  | "Paid"
  | "Processing"
  | "Shipped"
  | "Delivered"
  | "Cancelled"
  | "PaymentFailed";
export type PaymentStatus = "Pending" | "Succeeded" | "Failed";

export interface CategoryDto {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  isActive: boolean;
}

export interface ProductImageDto {
  id: string;
  url: string;
  altText: string | null;
  displayOrder: number;
}

/** spec map: key to scalar or array. Keys feed the facets API. */
export type Specs = Record<string, string | number | boolean | null>;

export interface ProductDto {
  id: string;
  name: string;
  slug: string;
  sku: string;
  brand: string | null;
  price: number;
  status: ProductStatus;
  categoryId: string;
  categoryName: string | null;
  availableStock: number;
  images: ProductImageDto[];
  /** Forward contract; the mock ships specs before the V1 backend does. */
  specs?: Specs;
}

export interface ProductDetailDto extends ProductDto {
  description: string | null;
  createdAt: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface FacetDto {
  key: string;
  label: string;
  type: "range" | "enum";
  /** Registry unit ("GB", "in"), an additive front-end field */
  unit?: string;
  values?: (string | number)[];
  min?: number;
  max?: number;
  count: number;
}

export interface CartItemDto {
  id: string;
  productId: string;
  productName: string;
  sku: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
  imageUrl: string | null;
}

export interface CartDto {
  id: string;
  items: CartItemDto[];
  subtotal: number;
  totalQuantity: number;
}

export interface ShippingAddressDto {
  fullName: string;
  phone: string;
  address: string;
  city: string;
}

/** A quote combines an order, a reservation, and a hosted pay session. The
 *  server delivers sessionToken as its own field. Clients must never decompose
 *  hostedPayUrl to find it, because in production the URL points at Stripe on
 *  another origin. */
export interface QuoteDto {
  sessionToken: string;
  order: OrderDto;
  hostedPayUrl: string;
  expiresAt: string;
}

/* --------------------------------- admin ----------------------------------
 * Transcribed from backend/Shop.Api/Controllers/AdminControllers.cs and the
 * matching records in Dtos.cs. Role-gated (Admin) server-side; the mock's
 * `adminOnly` mirrors [Authorize(Roles = "Admin")]. */

/** FixtureProduct shape. Admin list and detail rows carry the fields the public
 *  ProductDto hides (description, raw quantity, createdAt). */
export interface ProductAdminDto extends ProductDto {
  description: string;
  quantity: number;
  createdAt: string;
}

export interface InventoryDto {
  productId: string;
  productName: string;
  sku: string;
  quantity: number;
  reservedQuantity: number;
  availableQuantity: number;
}

/* ------------------------------ AI audit ------------------------------
 * Mock-only until the backend grows its audit read-model. The ledger rows
 * mirror what the real loop must emit per tool call. */

export interface AiLedgerEntryDto {
  id: string;
  ts: string; // ISO
  conversationId: string;
  tool: string;
  status: "ok" | "error" | "limited";
  durationMs: number;
  tokens: number; // prompt+completion estimate for the turn's LLM call; 0 for pure tool rows
}

export interface AiConversationDto {
  conversationId: string;
  turns: number;
  toolCalls: number;
  firstTs: string;
  lastTs: string;
}

export interface AiSettingsDto {
  /** Ai:Enabled kill switch. When off, chat answers 503 AI_NOT_CONFIGURED. */
  enabled: boolean;
  /** per-tool switches. When off, that tool degrades inside a turn */
  tools: Record<string, boolean>;
}

/** Client-visible payment outcome. The discriminator the UI reads instead of
 *  inferring "expired" from a null transactionId. */
export type PaymentOutcome = "captured" | "failed" | "expired";

export interface PaymentDto {
  id: string;
  amount: number;
  status: PaymentStatus;
  transactionId: string | null;
  /** null only for pre-settlement snapshots; every settle writes it. */
  outcome?: PaymentOutcome | null;
}

export interface OrderItemDto {
  productId: string;
  productName: string;
  sku: string;
  unitPrice: number;
  quantity: number;
  subtotal: number;
}

export interface OrderDto {
  id: string;
  orderNumber: string;
  status: OrderStatus;
  subtotal: number;
  shippingFee: number;
  totalAmount: number;
  shippingAddress: ShippingAddressDto;
  createdAt: string;
  items: OrderItemDto[];
  payment: PaymentDto | null;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly errors?: Record<string, string[]>,
    readonly retryAfterSeconds?: number
  ) {
    super(title);
  }
}

/* ----------------------------- agent chat ------------------------------
 * Elicitation vocabulary for the assistant UI. The turn envelope
 * (AgentTurnResult) and the SSE frames (ChatSseFrame) are schema-owned in
 * ./contracts/schemas/chat.schema.ts. The mock engine is
 * the deterministic FakeAiProvider, so numbers in replies always trace to a
 * tool call against the same fixture DB the REST handlers use. */

export interface ToolTrailItem {
  tool: string; // search_products | get_product | get_product_facets | compare_products | add_to_cart | create_quote | get_cart
  detail?: string;
}

export interface ChatProductRef {
  id: string;
  name: string;
  sku: string;
  price: number;
  availableStock: number;
  brand: string | null;
  categoryName: string | null;
  matchReason?: string;
  specs?: Specs;
}

export type ElicitField =
  | {
      key: "budget";
      label: string;
      type: "range";
      min: number;
      max: number;
      unit: "usd";
    }
  | {
      key: string;
      label: string;
      type: "enum";
      options: Array<string | number>;
    }
  | {
      key: string;
      label: string;
      type: "bool";
    };

export interface ChatError {
  kind: "auth" | "rate" | "network" | "validation" | "unknown" | "provider" | "policy";
  message: string;
  code?: string;
  retryAfter?: number;
}

export const formatUsd = (value: number) =>
  new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
