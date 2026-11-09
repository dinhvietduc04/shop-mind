/**
 * DTO contracts mirrored 1:1 from backend/Shop.Application/Dtos/Dtos.cs and
 * Common/PagedResult.cs. When F8 swaps MSW for the real Shop.Api, nothing in
 * the UI changes — these types ARE the contract. Money is a JSON number
 * (decimal 18,2 server-side); display always via formatUsd().
 */

export type UserRole = "Customer" | "Admin";
export type ProductStatus = "Draft" | "Active" | "Inactive";
export type OrderStatus =
  | "Pending" | "Paid" | "Processing" | "Shipped"
  | "Delivered" | "Cancelled" | "PaymentFailed";
export type PaymentStatus = "Pending" | "Succeeded" | "Failed";

export interface UserDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  role: UserRole;
}

export interface AuthResponse { token: string; user: UserDto }
export interface LoginRequest { email: string; password: string }
/** transcribed from backend/Shop.Application/Dtos/Dtos.cs RegisterRequest */
export interface RegisterRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
}

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

/** spec map (backend plan A3): key → scalar/array. Keys feed the facets API. */
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
  /** A3 forward contract — not in V1 backend yet; mock ships it now. */
  specs?: Specs;
}

export interface ProductDetailDto extends ProductDto {
  description: string | null;
  createdAt: string;
}

export interface ProductQuery {
  search?: string;
  category?: string; // slug or id
  brand?: string;
  minPrice?: number;
  maxPrice?: number;
  inStockOnly?: boolean;
  /** Spec filter DSL (§B5+ 4, one codec in lib/attributes): clauses
   *  `key:op:value` joined by ";", `in` values joined by "|".
   *  Unregistered key or malformed clause → 400 (fail-closed). */
  specs?: string;
  sort?: "price_asc" | "price_desc" | "newest" | "name_asc" | "name_desc";
  page?: number;
  pageSize?: number; // server cap 100
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
  /** §A3 registry unit ("GB", "in", …) — additive front-end surface */
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

/** B5 forward contract: quote = order + reservation + hosted pay session.
 *  sessionToken is delivered as its own field (§B5+ item 2) — clients must
 *  never decompose hostedPayUrl to find it (prod = cross-origin Stripe URL). */
export interface QuoteDto {
  sessionToken: string;
  order: OrderDto;
  hostedPayUrl: string;
  expiresAt: string;
}

/* --------------------------------- admin ----------------------------------
 * Transcribed from backend/Shop.Api/Controllers/AdminControllers.cs + the
 * matching records in Dtos.cs. Role-gated (Admin) server-side; the mock's
 * requireAdmin mirrors [Authorize(Roles = "Admin")]. */

/** FixtureProduct shape — admin list/detail rows carry the fields the public
 *  ProductDto hides (description, raw quantity, createdAt). */
export interface ProductAdminDto extends ProductDto {
  description: string;
  quantity: number;
  createdAt: string;
}

export interface CreateProductImageRequest {
  url: string;
  altText: string | null;
  displayOrder: number;
}
export interface CreateProductRequest {
  name: string;
  slug: string | null;
  sku: string;
  categoryId: string;
  description: string | null;
  brand: string | null;
  price: number;
  status: ProductStatus;
  images: CreateProductImageRequest[] | null;
  initialStock: number;
}
export interface UpdateProductRequest {
  name: string;
  slug: string | null;
  sku: string;
  categoryId: string;
  description: string | null;
  brand: string | null;
  price: number;
  status: ProductStatus;
  images: CreateProductImageRequest[] | null;
}

export interface CreateCategoryRequest {
  name: string;
  slug: string | null;
  description: string | null;
  isActive: boolean;
}
export interface UpdateCategoryRequest {
  name: string;
  slug: string | null;
  description: string | null;
  isActive: boolean;
}

export interface InventoryDto {
  productId: string;
  productName: string;
  sku: string;
  quantity: number;
  reservedQuantity: number;
  availableQuantity: number;
}
export interface UpdateInventoryRequest {
  quantity: number;
}

export interface UpdateOrderStatusRequest {
  status: OrderStatus;
}

/* Order-state facts (transitions, terminality, mainline rank, stock fate,
   revenue eligibility, bands) moved to lib/orders/lifecycle.ts — the ONE
   Quote-lifecycle module both the client and the mock server's guard call.
   This file keeps only the wire enum (OrderStatus). */

/* ------------------------------ AI audit (§A7) ------------------------------
 * Mock-only surface until the backend grows its audit read-model (plan §A7);
 * the ledger rows mirror what the real loop must emit per tool call. */

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
  /** Ai:Enabled kill switch — off → chat answers 503 AI_NOT_CONFIGURED. */
  enabled: boolean;
  /** per-tool switches (§B3 surface): off → that tool degrades inside a turn */
  tools: Record<string, boolean>;
}

/** Client-visible payment outcome (§B5+ item 1). The discriminator the UI
 *  reads instead of inferring "expired" from a null transactionId. */
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
 * Mirrors backend plan §A5 (POST /api/agent/chat) + §A2 tool names + §A3
 * elicitation fields. The mock engine is the deterministic FakeAiProvider
 * (§A4) — CI's own stand-in — so numbers in replies always trace to a tool
 * call against the same fixture DB the REST handlers use. */

export interface ToolTrailItem {
  tool: string; // search_products | get_product | get_product_facets | compare_products | add_to_cart | create_quote | get_cart
  detail?: string;
}

export type ElicitField =
  | { key: "budget"; label: string; type: "range"; min: number; max: number; unit: "usd" }
  | { key: string; label: string; type: "enum"; options: (string | number)[] }
  | { key: string; label: string; type: "bool" };

export interface ChatProductRef {
  id: string;
  name: string;
  sku: string;
  price: number;
  availableStock: number;
  brand: string | null;
  categoryName: string | null;
  matchReason?: string; // why the deterministic ranker picked it
  /** only carried on compare turns — the side-by-side needs the rows. */
  specs?: Specs;
}

export interface ChatQuoteRef {
  orderNumber: string;
  totalAmount: number;
  itemCount: number;
  expiresAt: string;
}

/** Final frame of a chat turn (delivered after the streamed text). */
export interface AgentTurnResult {
  text: string;
  toolTrail: ToolTrailItem[];
  products?: ChatProductRef[];
  compare?: { items: ChatProductRef[]; specKeys: string[] };
  elicit?: ElicitField[];
  quote?: ChatQuoteRef;
  cartCount?: number;
  /** per-conversation turn cap hit (B3): the answer above is partial. */
  limitReached?: boolean;
  /** machine code for policy refusals, e.g. QUOTE_HELD_BY_HUMAN. */
  code?: string;
  /** server-delivered truth so the UI renders "turn x/N" without hardcoding
   *  the cap (§B5+ item 3). Absent only before the first finished turn. */
  turnCap?: number;
  turnsUsed?: number;
}

/* ---------------- chat wire contract (AI_ASSISTANT_PLAN §B5+ item 3) --------
 * The frame vocabulary below is SHARED by the producer (mocks/handlers.ts SSE)
 * and the consumer (lib/chat/transport.ts — the only parser). F8: Shop.Api
 * must speak exactly this; nothing else in the app may know it. */
export type ChatSseFrame =
  | { type: "delta"; text: string }
  | { type: "turn"; result: AgentTurnResult }
  | { type: "error"; code: string; message: string };

/** Transport/policy failure taxonomy. Kind is UI behavior (retry button,
 *  countdown); code is the machine truth from the server envelope. */
export type ChatErrorKind = "rate" | "auth" | "provider" | "network" | "policy";
export interface ChatError {
  kind: ChatErrorKind;
  message: string;
  code?: string;
  retryAfter?: number;
}

export interface ChatRequest {
  message: string;
  conversationId: string;
  /** structured answers from an elicitation card, folded in as intent. */
  answers?: Record<string, string | number | boolean>;
}


export const formatUsd = (n: number) =>
  n.toLocaleString("en-US", { style: "currency", currency: "USD" });
