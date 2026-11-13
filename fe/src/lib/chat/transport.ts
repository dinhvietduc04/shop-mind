/**
 * Chat turn transport. This is the only consumer-side parser of the streaming
 * wire contract (`ChatSseFrame`). The producer is the SSE handler in
 * `mocks/handlers.ts`. Failure taxonomy: map on the server's `code` first and
 * fall back to HTTP status only for errors without the envelope.
 */
import { authHeaders } from "@/infrastructure/api";
import { whenReady } from "@/infrastructure/api";
import type { ChatError } from "@/infrastructure/api";
import type {
  AgentTurnResult,
  ChatRequest,
  ChatSseFrame,
} from "@/lib/api/contracts/schemas";

const NETWORK_LOSS: ChatError = {
  kind: "network",
  message: "Connection to the assistant was lost. Nothing in your cart changed.",
};

/** Pure: split accumulated SSE text into complete frames and the remainder. */
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
        /* Drop a malformed frame. The turn frame is the authority anyway. */
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
 * finished structured turn or a ChatError. Never throws.
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
