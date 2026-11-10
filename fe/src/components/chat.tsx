"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { ArrowRight, Ban, Hourglass, MessageSquareText, RefreshCw, Send, Wrench } from "lucide-react";
import { Button, MagnitudeTick, StockDots } from "@/components/ui";
import { formatUsd, type ChatProductRef, type ElicitField, type ToolTrailItem } from "@/lib/api/types";
import { specLabel } from "@/lib/attributes/registry";
import { useChat, type ChatMessage } from "@/lib/chat/store";
import { cn } from "@/lib/cn";

/* -------------------------------- tool trail ------------------------------- */

function ToolTrail({ items }: { items: ToolTrailItem[] }) {
  if (!items.length) return null;
  return (
    <div className="mt-3 flex flex-wrap gap-1.5" aria-label={`Tools run this turn: ${items.map((t) => t.tool).join(", ")}`}>
      {items.map((t, i) => (
        <span
          key={i}
          title={t.detail}
          className="inline-flex items-center gap-1.5 rounded-[3px] border-[1.5px] border-line bg-kraft px-2 py-0.5 font-mono text-[11px] uppercase tracking-[0.12em] text-ink-soft"
        >
          <Wrench size={9} aria-hidden />
          {t.tool}
          {t.detail && <span className="normal-case tracking-normal opacity-70">({t.detail})</span>}
        </span>
      ))}
    </div>
  );
}

/* ------------------------------ product cards ------------------------------ */

function ProductTile({ p, disabled }: { p: ChatProductRef; disabled: boolean }) {
  const send = useChat((s) => s.send);
  return (
    <div className="w-[230px] shrink-0 rounded-label border-[1.5px] border-ink bg-card p-3.5 text-ink">
      <div className="flex items-baseline justify-between gap-2">
        <span className="font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">{p.sku}</span>
        <StockDots qty={p.availableStock} withLabel={false} />
      </div>
      <Link href={`/product/${p.id}`} className="mt-1.5 block text-[13px] font-extrabold uppercase leading-snug tracking-[0.02em] hover:underline decoration-[1.5px] underline-offset-4">
        {p.name}
      </Link>
      <div className="mt-2 flex items-center justify-between gap-2">
        <span className="flex items-center gap-2">
          <MagnitudeTick value={p.price} className="h-4" />
          <span className="font-mono text-[15px] font-bold tabular-nums">{formatUsd(p.price)}</span>
        </span>
      </div>
      {p.matchReason && (
        <p className="mt-2 border-t border-dashed border-line pt-2 font-mono text-[9.5px] uppercase leading-relaxed tracking-[0.1em] text-ink-soft">
          {p.matchReason}
        </p>
      )}
      <Button
        variant="ghost"
        className="mt-3 w-full justify-center"
        disabled={disabled || p.availableStock <= 0}
        onClick={() => send(`add ${p.name} to cart`)}
      >
        {p.availableStock <= 0 ? "bin empty" : "have it add this"}
      </Button>
    </div>
  );
}

/* -------------------------------- compare --------------------------------- */

function CompareTable({ items, specKeys }: { items: ChatProductRef[]; specKeys: string[] }) {
  return (
    <table className="mt-3 w-full border-collapse font-mono text-[11px]">
      <thead>
        <tr className="border-y-[1.5px] border-ink">
          <th className="bg-kraft px-2 py-1.5 text-left text-[11px] font-bold uppercase tracking-[0.18em] text-ink-soft">
            attribute
          </th>
          {items.map((i) => (
            <th key={i.id} className="px-2 py-1.5 text-left">
              {i.name}
              <div className="mt-1 flex items-center gap-2 font-bold tabular-nums">
                {formatUsd(i.price)} <MagnitudeTick value={i.price} className="h-3.5" />
              </div>
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        <tr className="border-b border-line/50">
          <td className="bg-kraft/60 px-2 py-1 text-[11px] font-bold uppercase tracking-[0.18em] text-ink-soft">stock</td>
          {items.map((i) => (
            <td key={i.id} className="px-2 py-1">{i.availableStock} in bin</td>
          ))}
        </tr>
        {specKeys.map((k) => {
          const vals = items.map((i) => String(i.specs?.[k] ?? "—"));
          const differs = new Set(vals).size > 1;
          return (
            <tr key={k} className="border-b border-line/50 last:border-b-0">
              <td className="bg-kraft/60 px-2 py-1 text-[11px] font-bold uppercase tracking-[0.18em] text-ink-soft">
                {specLabel(k)}
              </td>
              {vals.map((v, j) => (
                <td key={j} className={cn("px-2 py-1", differs && "font-bold")}>{v}</td>
              ))}
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

/* ------------------------------- elicitation ------------------------------- */

function ElicitCard({ fields, disabled }: { fields: ElicitField[]; disabled: boolean }) {
  const send = useChat((s) => s.send);
  const [answers, setAnswers] = useState<Record<string, string | number>>({});
  const [done, setDone] = useState(false);

  const budget = fields.find((f): f is Extract<ElicitField, { type: "range" }> => f.key === "budget" && f.type === "range");
  const rest = fields.filter((f) => f.key !== "budget");
  const budgetValue = Number(answers.budget ?? (budget ? Math.round((budget.min + budget.max) / 2) : 0));

  const toggle = (key: string, v: string | number) =>
    setAnswers((a) => ({ ...a, [key]: String(a[key]) === String(v) ? undefined! : v }));

  const ready = Object.entries(answers).some(([, v]) => v !== undefined);

  const submit = () => {
    const parts: string[] = [];
    if (budget) parts.push(`budget $${budgetValue}`);
    for (const [k, v] of Object.entries(answers)) {
      if (v === undefined) continue;
      if (k === "budget") continue;
      parts.push(k.startsWith("drop:") ? (String(v) === "drop" ? `relax ${k.slice(5)}` : `keep ${k.slice(5)}`) : `${specLabel(k)} ${String(v).toUpperCase()}`);
    }
    setDone(true);
    send(
      parts.join(", "),
      { visible: parts.join(" · ") || "just show me the bin", answers }
    );
  };

  return (
    <div className="mt-3 rounded-label border-[1.5px] border-ink bg-kraft/70 p-4 text-ink">
      <h3 className="flex items-center gap-2 font-mono text-[11px] font-bold uppercase tracking-[0.22em] text-ink-soft">
        <MessageSquareText size={11} aria-hidden /> interview · {fields.length} question{fields.length === 1 ? "" : "s"}
      </h3>

      {budget && (
        <label className="mt-3 block">
          <span className="font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">{budget.label}</span>
          <div className="mt-1 flex items-center gap-3">
            <input
              type="range"
              min={budget.min}
              max={budget.max}
              step={1}
              value={budgetValue}
              disabled={disabled || done}
              onChange={(e) => setAnswers((a) => ({ ...a, budget: Number(e.target.value) }))}
              className="h-1.5 flex-1 accent-[#1a1a1a]"
              aria-label="Budget ceiling"
            />
            <span className="w-[74px] text-right font-mono text-[13px] font-bold tabular-nums">
              {formatUsd(budgetValue)}
            </span>
          </div>
        </label>
      )}

      {rest.map((f) =>
        f.type === "enum" ? (
          <div key={f.key} className="mt-3">
            <span className="font-mono text-[11px] uppercase tracking-[0.16em] text-ink-soft">{f.label}</span>
            <div className="mt-1.5 flex flex-wrap gap-1.5">
              {f.options.map((o) => {
                const on = String(answers[f.key]) === String(o);
                return (
                  <button
                    key={String(o)}
                    disabled={disabled || done}
                    onClick={() => toggle(f.key, o)}
                    aria-pressed={on}
                    className={cn(
                      "rounded-[4px] border-[1.5px] px-2 py-0.5 font-mono text-[11px]",
                      on ? "border-ink bg-ink text-paper" : "border-line bg-card hover:border-ink disabled:opacity-40"
                    )}
                  >
                    {String(o)}
                  </button>
                );
              })}
            </div>
          </div>
        ) : null
      )}

      <div className="mt-4 flex items-center gap-3">
        <Button variant="human" disabled={disabled || done || !ready} onClick={submit}>
          {done ? "answers sent" : "send answers"} <ArrowRight size={13} aria-hidden />
        </Button>
        <span className="font-mono text-[11px] uppercase tracking-[0.12em] text-ink-soft">
          answers join the filter set · nothing is stored beyond this conversation
        </span>
      </div>
    </div>
  );
}

/* -------------------------------- quote card ------------------------------- */

function ChatQuoteCard({ orderNumber, totalAmount, itemCount, expiresAt }: { orderNumber: string; totalAmount: number; itemCount: number; expiresAt: string }) {
  return (
    <div className="mt-3 overflow-hidden rounded-label border-[1.5px] border-ink">
      <div className="flex items-center justify-between border-b-[1.5px] border-ink bg-ink px-4 py-2 font-mono text-[11px] uppercase tracking-[0.2em] text-paper">
        <span>quote {orderNumber}</span>
        <span className="flex items-center gap-1.5">
          <Hourglass size={10} aria-hidden /> reserved until {new Date(expiresAt).toLocaleTimeString()}
        </span>
      </div>
      <div className="bg-card px-4 py-3 text-ink">
        <div className="flex items-center justify-between font-mono text-[15px] font-bold tabular-nums">
          <span>{itemCount} line{itemCount === 1 ? "" : "s"} · stock held</span>
          <span className="flex items-center gap-2"><MagnitudeTick value={totalAmount} className="h-4" />{formatUsd(totalAmount)}</span>
        </div>
        {/* Assist invariant: chat never pays — every action routes to the bill */}
        <div className="mt-3 flex flex-wrap gap-2">
          <Link
            href="/quote"
            className="inline-flex items-center gap-2 rounded-[4px] border-[1.5px] border-[#1a1a1a] bg-band-yellow px-4 py-2 text-[12px] font-extrabold uppercase tracking-[0.14em] text-[#1a1a1a] hover:brightness-110"
          >
            Review &amp; Pay on the bill <ArrowRight size={13} aria-hidden />
          </Link>
          <Link href="/quote">
            <Button variant="ghost">Edit cart instead</Button>
          </Link>
        </div>
        <p className="mt-2 font-mono text-[11px] uppercase leading-relaxed tracking-[0.12em] text-ink-soft">
          there is no pay control in this panel — the money boundary is the bill page
        </p>
      </div>
    </div>
  );
}

/* --------------------------------- failures -------------------------------- */

function RateLimitNote({ message, retryAfter }: { message: string; retryAfter?: number }) {
  const [left, setLeft] = useState(retryAfter ?? 0);
  const retry = useChat((s) => s.retry);
  useEffect(() => {
    if (left <= 0) return;
    const t = setInterval(() => setLeft((l) => Math.max(0, l - 1)), 1000);
    return () => clearInterval(t);
  }, [left]);
  return (
    <div className="mt-2 rounded-[4px] border-[1.5px] border-ink bg-band-yellow/25 px-3 py-2.5 text-[12px]">
      <strong>Slow down.</strong> {message}
      {left > 0 ? (
        <span className="ml-2 font-mono tabular-nums">retry in {left}s</span>
      ) : (
        <Button variant="ghost" className="ml-2" onClick={retry}>
          <RefreshCw size={12} aria-hidden /> retry now
        </Button>
      )}
    </div>
  );
}

function ErrorTurn({ msg }: { msg: ChatMessage }) {
  const e = msg.error!;
  if (e.kind === "rate") return <RateLimitNote message={e.message} retryAfter={e.retryAfter} />;
  if (e.kind === "auth")
    return (
      <div className="mt-2 rounded-[4px] border-[1.5px] border-ink bg-card px-3 py-2.5 text-[12px] text-ink">
        <strong>Sign in to chat.</strong> Conversation quotas are per-user (§A5) —
        the bins and spec sheets stay open to guests.{" "}
        <Link
          href={`/login?next=${encodeURIComponent("/assistant")}`}
          className="font-bold uppercase tracking-[0.1em] underline underline-offset-4"
        >
          Turnstile →
        </Link>
        <span className="ml-2 rounded-[3px] border border-ink px-1.5 py-0.5 font-mono text-[11px] uppercase">
          {e.code}
        </span>
      </div>
    );
  if (e.kind === "provider")
    return (
      <div className="mt-2 rounded-[4px] border-[1.5px] border-line bg-kraft px-3 py-2.5 text-[12px] text-ink">
        <strong>The assistant is unavailable</strong> — {e.message.replace(/\.\s*$/, "")}. Everything else in the shop keeps
        working: <Link href="/" className="underline underline-offset-4">browse the bins manually</Link>.
        <span className="ml-2 rounded-[3px] border border-ink px-1.5 py-0.5 font-mono text-[11px] uppercase">{e.code}</span>
      </div>
    );
  return (
    <div className="mt-2 rounded-[4px] border-[1.5px] border-band-red bg-band-red/10 px-3 py-2.5 font-mono text-[11px] uppercase tracking-[0.1em] text-band-red">
      {e.code && <Ban size={11} aria-hidden className="mr-1.5 inline" />}
      {e.message}
    </div>
  );
}

/* ---------------------------------- panel ---------------------------------- */

const DEMO_PROMPTS = [
  "a laptop under $1200 for backend dev",
  "help me pick headphones",
  "what's in my cart?",
];

export function ChatPanel({ compact = false }: { compact?: boolean }) {
  const { messages, busy, send, newSession } = useChat();
  const [draft, setDraft] = useState("");
  const bottomRef = useRef<HTMLDivElement>(null);
  /* The cap is server truth, carried in each turn's envelope (§B5+ 3). Until
     the first finished turn there is no number to show — so show none, not a
     guessed 8 that can drift from the engine's TURN_CAP. */
  const turnMeta = [...messages].reverse().find((m) => m.result?.turnCap != null)?.result;
  /* Screen readers get each COMPLETED agent turn once (streaming word-by-word
     into aria-live would flood the queue). */
  const lastSettled = [...messages].reverse().find((m) => m.role === "agent" && !m.streaming && (m.text || m.error));

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [messages]);

  const submit = () => {
    const text = draft.trim();
    if (!text || busy) return;
    setDraft("");
    void send(text);
  };

  const limitHit = messages.some((m) => m.result?.limitReached);

  return (
    <section
      className={cn(
        "flex flex-col overflow-hidden rounded-label border-[1.5px] border-ink bg-drawer text-drawer-ink",
        compact ? "h-[560px]" : "h-[70vh] min-h-[520px]"
      )}
      aria-label="Assistant chat"
    >
      <div className="sr-only" role="status" aria-live="polite">
        {lastSettled ? lastSettled.error?.message ?? lastSettled.text : ""}
      </div>
      <header className="flex items-center justify-between border-b-[1.5px] border-drawer-ink/25 bg-ink px-4 py-2.5 text-paper">
        <h2 className="flex items-center gap-2 text-[12px] font-extrabold uppercase tracking-[0.22em]">
          <MessageSquareText size={14} aria-hidden /> The assistant
        </h2>
        <span className="flex items-center gap-3 font-mono text-[11px] uppercase tracking-[0.16em] opacity-80">
          {turnMeta?.turnCap
            ? `turn ${Math.min((turnMeta.turnsUsed ?? 0) + 1, turnMeta.turnCap)}/${turnMeta.turnCap}`
            : "new session"}
          <button onClick={newSession} className="rounded-[3px] border border-paper/50 px-2 py-0.5 hover:border-paper">
            new
          </button>
        </span>
      </header>

      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-4 py-4">
        {messages.length === 0 && (
          <div>
            <p className="text-[13px] leading-relaxed opacity-80">
              Ask for something. I search the bins, read live stock, and ask up to three
              pointed questions before I filter — every price or count I say came from a
              tool call this turn. I can quote; <strong>paying is yours</strong>, on the
              bill page.
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              {DEMO_PROMPTS.map((p) => (
                <button
                  key={p}
                  disabled={busy}
                  onClick={() => void send(p)}
                  className="rounded-[4px] border-[1.5px] border-drawer-ink/40 px-2.5 py-1 font-mono text-[11px] hover:border-drawer-ink disabled:opacity-40"
                >
                  “{p}”
                </button>
              ))}
            </div>
          </div>
        )}

        {messages.map((m) =>
          m.role === "user" ? (
            <div key={m.id} className="flex justify-end">
              <p className="max-w-[85%] rounded-label border-[1.5px] border-drawer-ink bg-paper px-3 py-2 text-[13px] leading-snug text-ink">
                {m.text}
              </p>
            </div>
          ) : (
            <div key={m.id} className="max-w-[96%]">
              <p className="text-[13px] leading-relaxed whitespace-pre-line">
                {m.text}
                {m.streaming && (
                  <span className="ml-1 inline-flex gap-[3px] align-middle" aria-label="typing">
                    {[0, 1, 2].map((i) => (
                      <span
                        key={i}
                        className="inline-block h-1.5 w-1.5 animate-pulse rounded-full bg-drawer-ink/70"
                        style={{ animationDelay: `${i * 150}ms` }}
                      />
                    ))}
                  </span>
                )}
              </p>

              {m.result && <ToolTrail items={m.result.toolTrail} />}
              {m.result?.products?.length ? (
                <div className="mt-3 flex gap-3 overflow-x-auto pb-1">
                  {m.result.products.map((p) => (
                    <ProductTile key={p.id} p={p} disabled={busy} />
                  ))}
                </div>
              ) : null}
              {m.result?.compare && <CompareTable items={m.result.compare.items} specKeys={m.result.compare.specKeys} />}
              {m.result?.elicit && <ElicitCard fields={m.result.elicit} disabled={busy} />}
              {m.result?.quote && (
                <ChatQuoteCard
                  orderNumber={m.result.quote.orderNumber}
                  totalAmount={m.result.quote.totalAmount}
                  itemCount={m.result.quote.itemCount}
                  expiresAt={m.result.quote.expiresAt}
                />
              )}
              {m.error && <ErrorTurn msg={m} />}
            </div>
          )
        )}
        <div ref={bottomRef} />
      </div>

      {limitHit && (
        <div className="border-t-[1.5px] border-ink bg-band-yellow/30 px-4 py-2.5 text-[12px]">
          <strong>Turn cap reached</strong> — this conversation is partial by design
          (rate limits are part of the product). Your cart and any open quote are
          untouched.
          <Button variant="ghost" className="ml-3" onClick={newSession}>
            New conversation
          </Button>
        </div>
      )}

      <div className="flex items-center gap-2 border-t-[1.5px] border-drawer-ink/25 px-3 py-3">
        <input
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && submit()}
          placeholder="what do you need? (try a budget, a spec, a use case)"
          aria-label="Message the assistant"
          className="min-w-0 flex-1 rounded-[4px] border-[1.5px] border-drawer-ink/40 bg-transparent px-3 py-2 font-mono text-[12px] placeholder:opacity-50 focus:border-drawer-ink"
        />
        <Button variant="ghost" onClick={submit} disabled={busy || !draft.trim()}>
          <Send size={13} aria-hidden /> {busy ? "thinking…" : "send"}
        </Button>
      </div>
    </section>
  );
}
