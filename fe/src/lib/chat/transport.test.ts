import { describe, expect, it } from "vitest";
import { decodeFrames, mapTurnError } from "./transport";
import type { AgentTurnResult } from "@/lib/api/contracts/schemas";

/* These two pure functions are the transport's contract knowledge: the wire
   grammar and the failure taxonomy. streamTurn() is their orchestration. The
   cache-work question lives in lib/tools/registry.test.ts. */

describe("decodeFrames parses the SSE frame grammar (ChatSseFrame)", () => {
  it("splits complete frames and carries the partial remainder", () => {
    const buf =
      'data: {"type":"delta","text":"hi "}\n\n' +
      'data: {"type":"delta","te';
    const { frames, rest } = decodeFrames(buf);
    expect(frames).toEqual([{ type: "delta", text: "hi " }]);
    expect(rest).toBe('data: {"type":"delta","te');
  });

  it("decodes all three frame types from one buffer", () => {
    const result = { text: "x", toolTrail: [] } as AgentTurnResult;
    const buf =
      `data: ${JSON.stringify({ type: "delta", text: "a" })}\n\n` +
      `data: ${JSON.stringify({ type: "error", code: "RATE_LIMITED", message: "slow down" })}\n\n` +
      `data: ${JSON.stringify({ type: "turn", result })}\n\n`;
    const { frames, rest } = decodeFrames(buf);
    expect(frames.map((f) => f.type)).toEqual(["delta", "error", "turn"]);
    expect(rest).toBe("");
  });

  it("tolerates 'data:' without a space and ignores comments/heartbeats", () => {
    const { frames } = decodeFrames(
      ": ping\n\n" + 'data:{"type":"delta","text":"z"}\n\n'
    );
    expect(frames).toEqual([{ type: "delta", text: "z" }]);
  });

  it("drops malformed JSON without poisoning the stream", () => {
    const { frames } = decodeFrames(
      'data: {not json\n\ndata: {"type":"delta","text":"ok"}\n\n'
    );
    expect(frames).toEqual([{ type: "delta", text: "ok" }]);
  });

  it("ignores frames whose type is not in the vocabulary", () => {
    const { frames } = decodeFrames('data: {"type":"mystery"}\n\n');
    expect(frames).toEqual([]);
  });
});

describe("mapTurnError reads the code first and the status as fallback", () => {
  it("trusts the server code over the status number", () => {
    // a provider failure wrapped in a 500 still reads as provider-off
    expect(mapTurnError(500, { code: "AI_NOT_CONFIGURED", message: "no key" }).kind).toBe("provider");
  });

  it("falls back to status arithmetic for envelope-less errors", () => {
    expect(mapTurnError(429, { title: "Too many" }, "20")).toEqual({
      kind: "rate",
      message: "Too many",
      code: undefined,
      retryAfter: 20,
    });
    expect(mapTurnError(401, null).kind).toBe("auth");
    expect(mapTurnError(409, { code: "QUOTE_HELD_BY_HUMAN", message: "yours is open" }).kind).toBe("policy");
  });

  it("keeps the error code for the UI chip", () => {
    expect(mapTurnError(503, { code: "AI_NOT_CONFIGURED", message: "off" }).code).toBe("AI_NOT_CONFIGURED");
  });
});
