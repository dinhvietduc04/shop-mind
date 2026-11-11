# ShopMind frontend (`fe/`)

The Next.js storefront for ShopMind, an e-commerce demo where the shopping
assistant runs at the **Assist trust level**. The AI browses, compares, adds to
cart, and opens quotes through tools, but a person pays. No chat page, tool, or
endpoint can charge a card. That invariant is the point of the project.

The app is built against a complete mock backend (MSW) and a local design
system. See `../docs/FRONTEND_PLAN.md` for the build plan and `../DESIGN.md`
for the "Resistor Code" visual system.

## Run it

```bash
npm install
npm run dev        # http://localhost:3000, boots client-side with MSW
npm test           # vitest, module tests in plain Node, no browser
```

`npm test` covers the modules that need no DOM. That includes the quote
lifecycle state machine, the tool registry, the spec codec, the chat frame
decoder, session ordering, the bill outcome decode, and the quote ledger's
reserve-with-rollback, idempotent settle, expiry release, and
agent-versus-human origin rules.

No database and no API keys. The mock server persists in `localStorage`
(reset with **Reset demo data** on `/dev/ui` or `POST /api/_mock/reset`). The
app boots as a guest. Catalog and spec sheets are public. Cart, bill, orders,
and chat require sign-in. Demo identities:
`customer@shopmind.local / Customer123!` and
`admin@shopmind.local / Admin123!`.

## Where to look

| Route | What it demonstrates |
|---|---|
| `/` | bin wall with server-driven search, filter, sort, pagination, and the assistant drawer |
| `/product/[slug]` | spec sheet, decoded price bands, honest stock copy |
| `/category/[slug]` | category landings sharing the catalog engine |
| `/assistant` | the chat: SSE streaming, tool-trail chips, attribute interview, compare tables, quote cards |
| `/quote` | the bill, from cart through quote and reservation countdown to the hosted-pay hand-off |
| `/psp/[token]` | FakePsp, the chromeless hosted payment page, and the only place Pay exists |
| `/orders` | ledger with status timelines and cancel-when-legal |
| `/dev/ui` | component gallery and demo controls |

## Mock modes

`NEXT_PUBLIC_MOCK_MODE` is read at build time. Restart `npm run dev` after
changing it.

| value | effect |
|---|---|
| _unset_ | normal demo |
| `slow` | adds 650 ms to reads so loading skeletons stay visible |
| `ratelimit` | every third catalog read and chat turn returns 429 with a Retry-After countdown |
| `paymentfail` | the hosted page declines the card, the reservation survives, then expires |
| `agentdown` | `AI_NOT_CONFIGURED`. Chat degrades on its own and the rest of the shop keeps working |

## Scope

This app is the UI lab. The agent is the deterministic FakeAiProvider, a rules
engine over the same fixtures the REST mocks use, so every number in a reply
traces to a tool chip in the same turn. The contract types in
`src/lib/api/types.ts` mirror `Shop.Api`, and swapping MSW for the real
endpoints is the planned integration. The ASP.NET milestone plan targets the
vanilla `frontend/` SPA; `fe/` is not wired to the real API by it.

## Guardrails enforced in code

- **Assist invariant.** The tool registry has no `pay` capability. Chat refuses
  `pay`-class messages with a policy code. The Pay control renders only on
  `/psp/[token]`; chat quote cards link to the bill and render no pay control.
- **Origin-scoped quotes.** The agent can replace a quote it created but cannot
  touch a human-opened quote (`QUOTE_HELD_BY_HUMAN`).
- **Turn caps.** 8 turns per conversation (`TURN_CAP` in `src/mocks/agent.ts`),
  after which the partial-answer banner appears.
