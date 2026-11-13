import type { AiConversationDto, AiLedgerEntryDto, AiSettingsDto } from "@/infrastructure/api";
import { AI_TOOLS, LLM_TURN } from "@/lib/tools/registry";
import { registerStore, uuid } from "./snapshot";

/** AI settings and audit ledger domain. The tool vocabulary is not duplicated
 *  here. Defaults derive from the shared tool registry. */

const aiLedger = new Map<string, AiLedgerEntryDto>();
const defaultSettings = (): AiSettingsDto => ({
  enabled: true,
  tools: Object.fromEntries(AI_TOOLS.map((t) => [t, true])),
});
let aiSettings: AiSettingsDto = defaultSettings();

registerStore({
  name: "aiLedger",
  dump: () => [...aiLedger.entries()],
  restore: (v) => (v as [string, AiLedgerEntryDto][]).forEach(([k, e]) => aiLedger.set(k, e)),
  clear: () => aiLedger.clear(),
});

registerStore({
  name: "aiSettings",
  dump: () => aiSettings,
  restore: (v) => {
    const s = v as AiSettingsDto;
    aiSettings = { enabled: s.enabled, tools: { ...aiSettings.tools, ...s.tools } };
  },
  clear: () => {
    aiSettings = defaultSettings();
  },
});

export function aiKillSwitchOn(): boolean {
  return aiSettings.enabled;
}

export function toolEnabled(tool: string): boolean {
  return aiSettings.enabled && aiSettings.tools[tool] !== false;
}

export function getAiSettings(): AiSettingsDto {
  return { enabled: aiSettings.enabled, tools: { ...aiSettings.tools } };
}

export function setAiSettings(patch: Partial<AiSettingsDto>): AiSettingsDto {
  if (typeof patch.enabled === "boolean") aiSettings.enabled = patch.enabled;
  if (patch.tools) aiSettings.tools = { ...aiSettings.tools, ...patch.tools };
  return getAiSettings();
}

/** Audit stand-in: one row per tool call the turn made (or per LLM call). */
export function recordAiCalls(
  conversationId: string,
  rows: Omit<AiLedgerEntryDto, "id" | "conversationId">[]
): void {
  for (const r of rows) {
    const id = `ai-${uuid()}`;
    aiLedger.set(id, { ...r, id, conversationId });
  }
  // bounded like a real audit table with a retention job
  if (aiLedger.size > 400) {
    const stale = [...aiLedger.entries()].sort((a, b) => a[1].ts.localeCompare(b[1].ts)).slice(0, aiLedger.size - 400);
    stale.forEach(([k]) => aiLedger.delete(k));
  }
}

export function aiLedgerList(limit = 200): AiLedgerEntryDto[] {
  return [...aiLedger.values()].sort((a, b) => b.ts.localeCompare(a.ts)).slice(0, limit);
}

export function aiConversationsList(): AiConversationDto[] {
  const byConv = new Map<string, AiLedgerEntryDto[]>();
  for (const e of aiLedger.values()) {
    const l = byConv.get(e.conversationId) ?? [];
    l.push(e);
    byConv.set(e.conversationId, l);
  }
  return [...byConv.entries()]
    .map(([conversationId, rows]) => ({
      conversationId,
      turns: rows.filter((r) => r.tool === LLM_TURN).length,
      toolCalls: rows.filter((r) => r.tool !== LLM_TURN).length,
      firstTs: rows.reduce((m, r) => (r.ts < m ? r.ts : m), rows[0].ts),
      lastTs: rows.reduce((m, r) => (r.ts > m ? r.ts : m), rows[0].ts),
    }))
    .sort((a, b) => b.lastTs.localeCompare(a.lastTs));
}
