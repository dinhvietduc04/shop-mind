"use client";

import { useParams } from "next/navigation";
import { ErrorNote, Skeleton } from "@/components/ui";
import { ProductForm } from "@/components/admin/product-form";
import { useAdminProduct } from "@/lib/api/hooks";

/** Mount-after-settle: the form's initial state is the loaded product, so the
 *  query result is passed as a prop from a child boundary. No sync effect. */
export default function EditProductPage() {
  const { id } = useParams<{ id: string }>();
  const { data, isPending, isError } = useAdminProduct(id);

  if (isPending) return <Skeleton className="mt-6 h-96" />;
  if (isError || !data)
    return (
      <div className="mt-6 max-w-[520px]">
        <ErrorNote><strong>Product not found.</strong> It may have been deleted in another tab.</ErrorNote>
      </div>
    );
  return <ProductForm product={data} />;
}
