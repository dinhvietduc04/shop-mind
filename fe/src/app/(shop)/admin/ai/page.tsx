"use client";

import { AdminTable, Panel, Pill, statusBand, td, th, Toggle } from "@/components/admin/widgets";
import { Skeleton } from "@/components/ui";
import { useAiConversations, useAiLedger, useAiSettings, useSaveAiSettings } from "@/lib/api/hooks";
import { LLM_TURN, NEVER_TOOLS, SWITCHABLE_TOOLS } from "@/lib/tools/registry";
import type { AiSettingsDto } from "@/lib/api/types";

/** Transparency page: every tool call the assistant made, plus the kill
 *  switches a human can pull. Chat keeps working while this page is closed.
 *  The switch list comes from the shared tool registry, so hints, order, and
 *  defaults live in one place. Planned tools show as disabled. NEVER tools are
 *  absent by policy (ADR-0006), not denied capabilities. */
export default function AdminAiPage() {
  const { data: settings } = useAiSettings();
  const { data: conversations, isPending: convPending } = useAiConversations();
  const { data: ledger, isPending: ledgerPending } = useAiLedger();
  const save = useSaveAiSettings();

  return (
    <div className="mt-5 space-y-5">
      <div className="grid gap-5 lg:grid-cols-[340px_1fr]">
        <Panel title="Kill switch">
          {settings ? (
            <>
              <Toggle
                label="Ai:Enabled"
                hint={
                  settings.enabled
                    ? "Assistant is live. Chat turns run against the same mock tools the plan specs."
                    : "Chat answers 503 AI_NOT_CONFIGURED. The storefront, bins and bill are untouched."
                }
                checked={settings.enabled}
                danger
                onChange={(enabled) => save.mutate({ enabled })}
              />
              <div className="mt-3 border-t-[1.5px] border-dashed border-line pt-2">
                <p className="font-mono text-[11px] uppercase tracking-[0.18em] text-ink-soft">
                  per-tool · console
                </p>
                {SWITCHABLE_TOOLS.map((tool) => (
                  <Toggle
                    key={tool.name}
                    label={tool.name}
                    hint={tool.implemented ? tool.hint : `planned, ${tool.hint.toLowerCase()}`}
                    checked={Boolean(settings.tools[tool.name])}
                    danger={!settings.enabled ? undefined : true}
                    disabled={!settings.enabled || !tool.implemented}
                    onChange={(next) =>
                      save.mutate({ tools: { [tool.name]: next } } as Partial<AiSettingsDto>)
                    }
                  />
                ))}
              </div>
              <div className="mt-3 border-t-[1.5px] border-dashed border-line pt-2">
                <p className="font-mono text-[11px] uppercase tracking-[0.18em] text-ink-soft">
                  never in the manifest
                </p>
                <p className="mt-1 text-[11px] leading-relaxed text-ink-soft">
                  {NEVER_TOOLS.map((t) => t.name).join(" · ")}. Capability absent by
                  construction (ADR-0006). There is no switch to flip, because
                  nothing can register them. Money moves only on the hosted page,
                  by a human.
                </p>
              </div>
            </>
          ) : (
            <Skeleton className="h-64" />
          )}
        </Panel>

        <Panel title={`Conversations · ${conversations?.length ?? "…"}`}>
          {convPending ? (
            <Skeleton className="h-40" />
          ) : !conversations?.length ? (
            <p className="text-[13px] leading-relaxed text-ink-soft">
              No tool has been called yet. Open the assistant, ask it something.
              Every call lands here within seconds, because the engine and this
              page read the same audit log.
            </p>
          ) : (
            <AdminTable
              head={
                <>
                  <th className={th}>Conversation</th>
                  <th className={th}>Turns</th>
                  <th className={th}>Tool calls</th>
                  <th className={th}>Last activity</th>
                </>
              }
            >
              {conversations.map((c) => (
                <tr key={c.conversationId} className="border-b border-dashed border-line">
                  <td className={`${td} font-mono text-[11px]`}>{c.conversationId}</td>
                  <td className={`${td} font-mono text-[12px]`}>{c.turns}</td>
                  <td className={`${td} font-mono text-[12px]`}>{c.toolCalls}</td>
                  <td className={`${td} font-mono text-[11px] text-ink-soft`}>
                    {new Date(c.lastTs).toLocaleTimeString()}
                  </td>
                </tr>
              ))}
            </AdminTable>
          )}
        </Panel>
      </div>

      <Panel title="Tool-call ledger">
        {ledgerPending ? (
          <Skeleton className="h-64" />
        ) : !ledger?.length ? (
          <p className="text-[13px] text-ink-soft">Empty log. No calls recorded yet.</p>
        ) : (
          <AdminTable
            head={
              <>
                <th className={th}>Time</th>
                <th className={th}>Conversation</th>
                <th className={th}>Tool / call</th>
                <th className={th}>Status</th>
                <th className={th}>Duration</th>
                <th className={th}>Tokens</th>
              </>
            }
          >
            {ledger.slice(0, 60).map((e) => (
              <tr key={e.id} className="border-b border-dashed border-line">
                <td className={`${td} font-mono text-[11px]`}>{new Date(e.ts).toLocaleTimeString()}</td>
                <td className={`${td} font-mono text-[11px] text-ink-soft`}>{e.conversationId.slice(0, 14)}</td>
                <td className={`${td} font-mono text-[12px] font-bold`}>
                  {e.tool === LLM_TURN ? "llm · model turn" : `mcp · ${e.tool}`}
                </td>
                <td className={td}>
                  <Pill band={statusBand(e.status)} label={e.status} />
                </td>
                <td className={`${td} font-mono text-[12px]`}>{e.durationMs} ms</td>
                <td className={`${td} font-mono text-[12px]`}>{e.tokens || "—"}</td>
              </tr>
            ))}
          </AdminTable>
        )}
        <p className="mt-3 text-[11px] leading-relaxed text-ink-soft">
          Every price and stock number the assistant quotes traces to one of
          these rows. That is the transparency rule the chat UI uses
          (each tool chip maps to a call against the same database this table
          reads). The engine here is the deterministic FakeAiProvider; the real
          provider fills the same ledger shape.
        </p>
      </Panel>
    </div>
  );
}
