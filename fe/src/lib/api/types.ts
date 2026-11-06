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

/** B5 forward contract: quote = order + reservation + hosted pay session. */
export interface QuoteDto {
  order: OrderDto;
  hostedPayUrl: string;
  expiresAt: string;
}

export interface PaymentDto {
  id: string;
  amount: number;
  status: PaymentStatus;
  transactionId: string | null;
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

export const formatUsd = (n: number) =>
  n.toLocaleString("en-US", { style: "currency", currency: "USD" });
