"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/infrastructure/api";
import { qk } from "@/infrastructure/query";
import type { ShippingAddressDto } from "@/infrastructure/api";

export function useAccountAddress() {
  return useQuery({
    queryKey: qk.address(),
    queryFn: () => api<{ address: ShippingAddressDto | null }>("/account/address"),
  });
}

export function useSaveAddress() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: (address: ShippingAddressDto) =>
      api<ShippingAddressDto>("/account/address", {
        method: "PUT",
        body: JSON.stringify(address),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: qk.address() });
    },
  });
}
