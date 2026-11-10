/**
 * Chat turn transport — the ONLY consumer-side knowledge of the assistant's
 * streaming wire contract (AI_ASSISTANT_PLAN §B5+ item 3). The producer
 * (mocks/handlers.ts SSE) and this module share the frame vocabulary declared
 * in lib/api/types.ts (ChatSseFrame); nothing else parses `data:` lines.
 *
 * Owns, in one place:
 *  - the readyGate await — chat used to fetch raw and silently bypass the
 *    mock-boot park that api() imposes (candidate C3's leak);
 *  - auth via the sole header builder in api/client (authHeaders);
 *  - the failure taxonomy: map on the server's `code` FIRST, falling back to
 *    HTTP status arithmetic only for errors that don't speak the envelope.
 */
import { authHeaders } from "@/lib/api/client";
import { whenReady } from "@/lib/api/readyGate";
import type {
  AgentTurnResult,
  ChatError,
  ChatRequest,
  ChatSseFrame,
} from "@/lib/api/types";

const NETWORK_LOSS: ChatError = {
  kind: "network",
  message: "Connection to the assistant was lost — nothing in your cart changed.",
};

/** Pure: split accumulated SSE text into complete frames + the remainder. */
export function decodeFrames(buffer: string): {
  frames: ChatSseFrame[];
  rest: string;
} {
  const frames: ChatSseFrame[] = [];
  let rest = buffer;
  for (;;) {
    const idx = rest.indexOf("\n\n");
    if (idx === -1) break;
    const raw = rest.slice(0, idx);
    rest = rest.slice(idx + 2);
    for (const line of raw.split("\n")) {
      if (!line.startsWith("data:")) continue; // comments/heartbeats carry no frame
      const data = line.slice(5).trim();
      if (!data) continue;
      try {
        const ev = JSON.parse(data) as ChatSseFrame;
        if (ev.type === "delta" || ev.type === "turn" || ev.type === "error") {
          frames.push(ev);
        }
      } catch {
        /* malformed frame — drop it; the turn frame is the authority anyway */
      }
    }
  }
  return { frames, rest };
}

/** Pure: failure taxonomy. Server codes win; status is the fallback. */
export function mapTurnError(
  status: number,
  body: { code?: string; message?: string; title?: string } | null,
  retryAfter?: string | null
): ChatError {
  const message = body?.message ?? body?.title ?? "The assistant did not answer.";
  const retry = Number(retryAfter ?? 0) || undefined;
  const code = body?.code;
  let kind: ChatError["kind"];
  if (code === "RATE_LIMITED" || status === 429) kind = "rate";
  else if (code === "UNAUTHORIZED" || status === 401) kind = "auth";
  else if (code === "AI_NOT_CONFIGURED" || status === 503) kind = "provider";
  else kind = "policy";
  return { kind, message, code, retryAfter: retry };
}

export type TurnOutcome =
  | { ok: true; result: AgentTurnResult }
  | { ok: false; error: ChatError };

/**
 * POST /api/agent/chat: stream word deltas through onDelta, resolve with the
 * finished structured turn — or a ChatError. Never throws.
 */
export async function streamTurn(
  req: ChatRequest,
  onDelta: (word: string) => void
): Promise<TurnOutcome> {
  await whenReady(); // mock-boot park, same contract every REST call honors
  let res: Response;
  try {
    res = await fetch("/api/agent/chat", {
      method: "POST",
      headers: authHeaders({ "Content-Type": "application/json" }),
      body: JSON.stringify(req),
    });
  } catch {
    return { ok: false, error: NETWORK_LOSS };
  }

  if (!res.ok) {
    const body = (await res.json().catch(() => null)) as {
      code?: string;
      message?: string;
      title?: string;
    } | null;
    return {
      ok: false,
      error: mapTurnError(res.status, body, res.headers.get("Retry-After")),
    };
  }

  const reader = res.body?.getReader();
  if (!reader) return { ok: false, error: NETWORK_LOSS };
  const decoder = new TextDecoder();
  let buffer = "";
  let final: AgentTurnResult | null = null;
  let streamError: ChatError | null = null;

  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const { frames, rest } = decodeFrames(buffer);
    buffer = rest;
    for (const f of frames) {
      if (f.type === "delta") onDelta(f.text);
      else if (f.type === "turn") final = f.result;
      else streamError = mapTurnError(0, { code: f.code, message: f.message });
    }
  }

  if (streamError) return { ok: false, error: streamError };
  if (!final) return { ok: false, error: NETWORK_LOSS };
  return { ok: true, result: final };
}
