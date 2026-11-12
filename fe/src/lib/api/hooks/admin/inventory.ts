
import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { api } from "../../client";
import { qk } from "../../keys";
import { catalogWriteStale } from "../../invalidation";
import type {
  InventoryDto,
  PagedResult,
  UpdateInventoryRequest,
} from "../../types";

export function useInventory() {
  return useQuery({
    queryKey: qk.adminInventory(),
    queryFn: async () =>
      (
        await api<PagedResult<InventoryDto>>(
          "/admin/inventory"
        )
      ).items,
  });
}

export function useSetStock() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (v: {
      productId: string;
      quantity: number;
    }) =>
      api<InventoryDto>(
        `/admin/inventory/${v.productId}`,
        {
          method: "PATCH",
          body: JSON.stringify({
            quantity: v.quantity,
          } satisfies UpdateInventoryRequest),
        }
      ),
    onSuccess: (data) => {
      catalogWriteStale();

      qc.setQueryData<InventoryDto[]>(
        qk.adminInventory(),
        (old) =>
          old
            ? old.map((r) =>
                r.productId === data.productId ? data : r
              )
            : old
      );
    },
  });
}
