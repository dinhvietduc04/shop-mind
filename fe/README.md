# ShopMind — `fe/`

The Next.js storefront for ShopMind: an e-commerce demo where the shopping
assistant operates at the **Assist trust level** — an AI browses, compares,
adds to cart and opens quotes through tools, but **payment is structurally
human-only**: no chat surface, tool, or endpoint can charge a card. That
invariant is the point of the whole project.

Built UI-first against a complete mock backend (MSW), design system and all —
see `../docs/FRONTEND_PLAN.md` for phases and `../DESIGN.md` for the
"Resistor Code" visual system.

## Run it

```bash
npm install
npm run dev        # http://localhost:3000 — boots fully client-side with MSW
npm test           # vitest — deep-module interface tests in plain node (no browser)
```

`npm test` covers the modules that need no DOM: the Quote lifecycle state
machine, the tool registry, the Spec codec, the chat transport's frame
decoder, session ordering, the Bill's outcome decode, and
`src/mocks/server/quote-ledger.test.ts` — the quote ledger's riskiest
invariants (reserve-with-rollback, idempotent settle, expiry release,
agent-vs-human origin scope) asserted directly, since the fake server's
domain modules are pure in-memory code.

No database, no API keys: the mock server persists in `localStorage`
(reset via the **Reset demo data** button on `/dev/ui` or
`POST /api/_mock/reset`). The app boots as a **guest** — catalog and spec
sheets are public; cart, bill, orders and chat route through the turnstile.
One-click demo identities: `customer@shopmind.local / Customer123!` and
`admin@shopmind.local / Admin123!`.

## Where to look

| Route | What it demonstrates |
|---|---|
| `/` | bin wall — server-driven search/filter/sort/pagination + assistant drawer |
| `/product/[slug]` | spec sheet, decoded price bands, honest stock copy |
| `/category/[slug]` | category landings sharing the catalog engine |
| `/assistant` | the chat: SSE streaming, tool-trail chips, attribute-elicitation interview, compare tables, quote cards |
| `/quote` | the bill — cart → quote → reservation countdown → hosted-pay hand-off |
| `/psp/[token]` | FakePsp: the chromeless hosted payment page (only place Pay exists) |
| `/orders` | ledger with status timelines, cancel-when-legal |
| `/dev/ui` | component gallery + demo controls |

## Mock modes

`NEXT_PUBLIC_MOCK_MODE` (build-time env, restart `npm run dev` after changing):

| value | effect |
|---|---|
| _unset_ | normal demo |
| `slow` | +650 ms on reads — see skeleton discipline |
| `ratelimit` | every 3rd catalog read and chat turn → 429 with Retry-After countdown |
| `paymentfail` | the hosted page declines the card (reservation survives, then expires) |
| `agentdown` | `AI_NOT_CONFIGURED` — chat degrades gracefully, the rest of the shop is untouched |

## Scope note (mirrors `../docs/AI_ASSISTANT_PLAN.md` §A6)

This app is the **design/UI lab**. The agent here is the deterministic
FakeAiProvider (a rules engine against the same fixtures the REST mocks use —
every number in a reply traces to a tool chip in the same turn). When the real
backend lands (`../docs/AI_ASSISTANT_PLAN.md` milestones B/A), the contract
types in `src/lib/api/types.ts` are already transcribed from `Shop.Api`, and
swapping MSW for the real endpoints is the only planned change. The ASP.NET
milestone plan targets the vanilla `frontend/` SPA; `fe/` is not wired to the
real API by it.

## Guardrails that are load-bearing in code

- **Assist invariant**: no `pay` tool exists in `src/mocks/agent.ts`; `pay`-class
  messages are refused with the policy code; the Pay control renders only on
  `/psp/[token]` (checked by the Playwright flow: no `/pay/i` control exists in
  the chat DOM).
- **Origin-scoped quotes**: an agent-created quote can be replaced by the
  agent; a human-opened quote cannot be touched by the agent
  (`QUOTE_HELD_BY_HUMAN`).
- **Turn caps**: 8 turns per conversation → partial-answer banner by design.
