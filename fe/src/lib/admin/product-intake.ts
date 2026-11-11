import { api, idemKey } from "@/lib/api/client";
import { catalogWriteStale } from "@/lib/api/invalidation";
import { ApiError, type CreateProductRequest, type ProductAdminDto, type Specs, type UpdateProductRequest } from "@/lib/api/types";
import { validateProductWrite, type ProductProblem } from "./product-schema";

/**
 * Product intake. Owns the whole write sequence. Create carries `initialStock`
 * in one request. Update sends the product, then a stock PATCH only when the
 * number moved. A failed stock PATCH returns a partial success with
 * `stockWarning`, so the saved product is never hidden.
 *
 * Local pre-validation uses the same field description the mock server
 * enforces (`product-schema`), so the form and the server cannot disagree.
 */

export type ProductWriteInput = {
  productId?: string; // present selects the update path
  body: UpdateProductRequest & { specs?: Specs };
  stock?: number;
  previousStock?: number; // update path: was the stock number moved?
};

export type ProductWriteOutcome =
  | { ok: true; product: ProductAdminDto }
  | { ok: true; product: ProductAdminDto; stockWarning: string }
  | { ok: false; problems: ProductProblem; title: string } // server field errors
  | { ok: false; network: true }; // unreachable / non-field failure

export async function saveProduct(input: ProductWriteInput): Promise<ProductWriteOutcome> {
  const problems = validateProductWrite(
    { ...input.body, initialStock: input.stock },
    { withStock: true }
  );
  if (problems) return { ok: false, problems, title: "Validation failed." };

  try {
    if (input.productId) {
      const product = await api<ProductAdminDto>(`/admin/products/${input.productId}`, {
        method: "PUT",
        body: JSON.stringify(input.body),
        idempotencyKey: idemKey(),
      });
      catalogWriteStale();
      // stock lives on the inventory row, so PATCH only when the number moved
      if (input.stock !== undefined && input.stock !== input.previousStock) {
        try {
          await api(`/admin/inventory/${input.productId}`, {
            method: "PATCH",
            body: JSON.stringify({ quantity: input.stock }),
          });
          catalogWriteStale();
        } catch (e) {
          if (e instanceof ApiError)
            return { ok: true, product, stockWarning: e.title || "Stock write failed." };
          throw e;
        }
      }
      return { ok: true, product };
    }
    const product = await api<ProductAdminDto>("/admin/products", {
      method: "POST",
      body: JSON.stringify({ ...input.body, initialStock: input.stock ?? 0 } satisfies CreateProductRequest),
      idempotencyKey: idemKey(),
    });
    catalogWriteStale();
    return { ok: true, product };
  } catch (e) {
    if (e instanceof ApiError)
      return { ok: false, problems: e.errors ?? {}, title: e.title };
    return { ok: false, network: true };
  }
}
