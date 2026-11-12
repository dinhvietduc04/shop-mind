import {
    useMutation,
    useQuery,
    useQueryClient,
} from "@tanstack/react-query"
import { api } from "../../client";
import { qk } from "../../keys";
import type {
    AiConversationDto,
    AiLedgerEntryDto,
    AiSettingsDto,
} from "../../types"

export function useAiLedger() {
    return useQuery({
        queryKey: qk.aiLedger(),
        queryFn: () => api<AiLedgerEntryDto[]>("/admin/ai/ledger"),
        refetchInterval: 5000,
    })
}

export function useAiConversations() {
    return useQuery({
                queryKey: qk.aiConversations(),
        queryFn: () => api<AiConversationDto[]>("/admin/ai/conversations"),
        refetchInterval: 5000,
    })
}

export function useAiSettings() {
  return useQuery({
    queryKey: qk.aiSettings(),
    queryFn: () => api<AiSettingsDto>("/admin/ai/settings"),
  });
}

export function useSaveAiSettings() {
    const qc = useQueryClient();

    return useMutation({
        mutationFn: (patch: Partial<AiSettingsDto>) => 
        api<AiSettingsDto>("/admin/ai/settings", {
            method: "PUT",
            body: JSON.stringify(patch),
        }),
        onSuccess: (next) => qc.setQueryData(qk.aiSettings(), next),
    })
}
