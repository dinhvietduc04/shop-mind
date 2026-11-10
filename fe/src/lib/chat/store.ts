"use client";

import { create } from "zustand";
import { activeQuoteStale, cartStale, stockWallStale } from "@/lib/api/invalidation";
import { asMember } from "@/lib/session";
import { turnTouches } from "@/lib/tools/registry";
import type { AgentTurnResult, ChatError } from "@/lib/api/types";
import { streamTurn } from "./transport";

export interface ChatMessage {
  id: string;
  role: "user" | "agent";
  text: string;
  /** structured payload of a finished agent turn (cards, trail, quote…) */
  result?: AgentTurnResult;
  streaming?: boolean;
  /** surfaced transport/policy failure; retryAfter ticks down in the UI. */
  error?: ChatError;
}

interface ChatState {
  messages: ChatMessage[];
  busy: boolean;
  conversationId: string;
  /** last message that failed in a retryable way, for the retry button. */
  lastFailed: { text: string; answers?: Record<string, string | number | boolean> } | null;
  send: (text: string, opts?: { answers?: Record<string, string | number | boolean>; visible?: string }) => Promise<void>;
  retry: () => Promise<void>;
  newSession: () => void;
}

const LS_CID = "shopmind.chat.cid";
const rid = () => Math.random().toString(36).slice(2, 10);

/* Conversation id survives reloads so the transcript's shape is stable, even
   though the mock engine's turn counter resets with the page (documented). */
const initialCid =
  (typeof window !== "undefined" && (sessionStorage.getItem(LS_CID) ?? (sessionStorage.setItem(LS_CID, `conv-${rid()}`), sessionStorage.getItem(LS_CID)))) ||
  `conv-${rid()}`;

export const useChat = create<ChatState>((set, get) => ({
  messages: [],
  busy: false,
  conversationId: initialCid,
  lastFailed: null,

  async send(text, opts) {
    const state = get();
    if (state.busy || !text.trim()) return;
    const visible = opts?.visible ?? text;
    const userMsg: ChatMessage = { id: rid(), role: "user", text: visible };
    const agentId = rid();
    set({
      messages: [...state.messages, userMsg, { id: agentId, role: "agent", text: "", streaming: true }],
      busy: true,
      lastFailed: null,
    });

    const patch = (fn: (m: ChatMessage) => ChatMessage) =>
      set((s) => ({ messages: s.messages.map((m) => (m.id === agentId ? fn(m) : m)) }));

    /* §A5: chat requires login (per-user cost caps). Nudge at the boundary —
       the guest's own message stands in the transcript, the reply is the ask.
       asMember() absorbs the settle-ordering protocol (boot paints before
       restore resolves — a just-restored member is never misread as guest). */
    if (!(await asMember())) {
      patch((m) => ({
        ...m,
        streaming: false,
        text: "",
        error: { kind: "auth", message: "Sign in to open a conversation — chat quotas are per-user.", code: "UNAUTHORIZED" },
      }));
      set({ busy: false });
      return;
    }

    try {
      const out = await streamTurn(
        { message: text, conversationId: state.conversationId, answers: opts?.answers },
        (chunk) => patch((m) => ({ ...m, text: m.text + chunk }))
      );
      if (!out.ok) {
        patch((m) => ({ ...m, streaming: false, error: out.error }));
        if (out.error.kind === "rate") set({ lastFailed: { text, answers: opts?.answers } });
        return;
      }

      const final = out.result;
      patch((m) => ({ ...m, text: final.text, result: final, streaming: false }));

      /* tool writes moved live state — pages must re-read it. The registry
         answers "did this turn touch the cart or the stock wall" (§A2 Risk
         Classes), so the transport no longer name-sniffs agent internals. */
      const touch = turnTouches(final.toolTrail.map((t) => t.tool));
      if (touch.cart) cartStale();
      if (touch.stock) stockWallStale();
      if (final.quote) activeQuoteStale();
    } finally {
      set({ busy: false });
    }
  },

  async retry() {
    const f = get().lastFailed;
    if (!f || get().busy) return;
    await get().send(f.text, { answers: f.answers, visible: `(retry) ${f.text}` });
  },

  newSession() {
    set({ messages: [], busy: false, conversationId: `conv-${rid()}`, lastFailed: null });
    if (typeof window !== "undefined") sessionStorage.setItem(LS_CID, get().conversationId);
  },
}));
