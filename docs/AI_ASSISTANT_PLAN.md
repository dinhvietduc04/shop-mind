# ShopMind — Backend Reliability + AI Shopping Assistant (Assist Level) — Master Plan

> Scope: e-commerce backend engineering (cache, rate limit, queue, idempotency, race-condition handling) +
> an AI assistant that chats with the user and performs shopping tasks through **MCP tools with strict limits**.
> Initial trust level = **Assist** (from the attached table): *AI browses, compares, builds cart, creates quote/bill;
> the human reviews the bill and pays on a PSP-hosted page.* Suggestions are **attribute-elicitation based, not ML**.
> Companion doc: `docs/PLAN.md` (M0–M9 done; this plan re-cuts M10+ and records deltas in §9).

Status date: 2026-11-06. Verified against code in `backend/` (note: README says `src/`, actual dirs are `backend/Shop.*`).
> Grill session 2026-11-06: all open decisions settled — recorded in `GLOSSARY.md` + `docs/adr/0001–0007` and folded into the sections below (look for **[GRILLED]** markers).

---

## 1. Where we are (verified baseline)

| Area | State today | Evidence |
|---|---|---|
| V1 shop (auth, catalog, cart, checkout, orders, admin) | ✅ done | `docs/PLAN.md` M0–M8 |
| Redis catalog caching (M9) | ✅ done | `Shop.Application/Caching/ICacheService.cs`, `Shop.Infrastructure/Caching/RedisCacheService.cs`, `X-Cache` header, evictions |
| Queue / outbox / workers (M10–M11) | ✅ done | `OutboxDeliveryTests`, `ConsumerPipelineTests`, `RabbitE2ETests`; `Shop.Worker` |
| MCP / agent (M12–M16) | 🟡 A1–A3 done; A4–A6 pending | `backend/Shop.Mcp` + A1/A2 unit/integration suites |
| Rate limiting | ✅ done (REST + MCP layers) | B3-L1 `RateLimitMiddleware` + Redis counters; A1 MCP sliding-window budget |
| Idempotency | ✅ done | B2 `RedisIdempotencyStore` + replay tests |
| Race safety | ✅ done | B1 `xmin` tokens + guarded updates |
| Payment | ✅ done (quote → FakePsp hosted page → webhook) | B5 / ADR-0003; `scope=agent` cannot confirm |
| Stock model | has `Quantity` + `ReservedQuantity` | `Shop.Domain/Entities/Inventory.cs:15` — good foundation for quote reservations |

### 2. Correctness gaps that drove B1–B5 (historical; closed before A1)

> Historical context: B1–B5 closed these gaps before A1. The text below is the original gap analysis, not current status.

1. **Oversell race in checkout.** `CheckoutService.cs:28-42` reads stock inside a transaction but Postgres default
   READ COMMITTED does not lock the rows; two concurrent checkouts can both pass validation and both decrement.
   No concurrency tokens (`xmin`/RowVersion) configured anywhere (`EntityConfigurations.cs`).
2. **Cart lost update.** `CartService.AddItemAsync` is read-modify-write (`:55-78`); it catches
   `DbUpdateConcurrencyException` (`:85`) but with no token configured the exception can never fire → concurrent
   adds can drop quantities (agent retries will hit this constantly).
3. **No idempotency on money/state-changing POSTs** (checkout, cart add via agent retry, future quote).
4. **No rate limiting** anywhere → an MCP-connected agent (or a raw scraper) can hammer catalog/cart/checkout.
5. **Side effects in request path** (email/analytics would block/fail checkout) — solved by M10/M11 outbox+queue.

---

## 3. Trust model (the picture) — drives the whole architecture

| Level | AI does | Human does | In ShopMind |
|---|---|---|---|
| **Assist** (initial, default) | browse, compare, build cart, **create quote/bill** | reviews bill, **pays on PSP-hosted page** | ✅ build now |
| Delegated (future) | completes checkout within pre-authorized limits (merchant, amount cap, category, expiry) | enrolls credential once; gets notified; steps up for exceptions | 🔒 schema + policy seam only, flag-gated |
| Autonomous | pays anywhere with stored card | nothing until dispute | ❌ **will not be built** (not viable/desirable) |

**Design rule: capability absent, not merely denied.** At Assist level there is *no MCP tool that can charge a card* —
payment happens on a PSP-hosted page that requires the human's authenticated browser session. A bug or jailbreak in
the agent cannot spend money because the capability doesn't exist in its tool surface.

Enforced centrally by an `IToolRiskPolicy` service (risk classes: `READ / WRITE / QUOTE / NEVER`), so flipping
config `Agent:TrustLevel=Delegated` later only *unlocks* a pre-built, separately tested path.

---

## 4. Target architecture

```text
human browser ── frontend (vanilla SPA)
                    │ chat UI + "Review & Pay" button
                    ▼
             Shop.Api ──────────────► PostgreSQL (products, carts, orders, inventories,
                    │                  outbox, payment_sessions, ai_*)
                    │                Redis (catalog cache ✓, rate-limit counters,
                    │                  idempotency store, single-flight)
                    ▼
             AgentLoop (ReAct, budgets)
                    │ MCP client (Streamable HTTP, delegated scoped JWT)
                    ▼
             Shop.Mcp (NEW project)
               ├─ McpAuthContext (JWT: userId, role, scope=agent)
               ├─ MCP-side rate limiter + arg validation (independent of API limits)
               ├─ Tools: search/get/compare/facets/cart/create_quote/get_order...
               └─ NEVER DbContext → calls Shop.Application services only

             Shop.Worker (NEW project)
               ├─ OutboxPublisher → RabbitMQ shop.events
               ├─ Email / Analytics consumers (idempotent by EventId)
               ├─ PaymentWebhookConsumer (dedupe by PSP event id)
               └─ QuoteExpirySweeper (release reservations on TTL); RetentionSweeper (conversations 30d, tool-call ledger 60d)

human pays → PSP-hosted checkout page (Stripe in prod; FakePsp hosted page in dev)
           → webhook → API (verify signature, enqueue) → worker marks Paid, commits stock, clears cart
```

---

## 5. Backend reliability features (B-track)

### B1 — Race-condition handling (Phase 0, ships first)

**Design choices (locked):**
- **Atomic guarded SQL for stock**, not app-level check-then-write:
  - Reserve: `UPDATE inventories SET reserved_quantity = reserved_quantity + @q WHERE product_id=@id AND quantity - reserved_quantity >= @q` → 0 rows = insufficient stock.
  - Commit on payment: single atomic `quantity -= q, reserved_quantity -= q` with the same guard.
  - Release (expire/fail/cancel): `reserved_quantity -= q WHERE reserved_quantity >= q`.
  - Implement as EF `ExecuteUpdateAsync` / raw SQL methods on `Inventory` repository so the check and write are one statement.
- **`xmin` as EF concurrency token** on `Product`, `Inventory`, `Cart`, `CartItem`, `Order` (lightest Postgres option; migration = add `xmin` shadow property, no table change). Retry policy for cart ops: reload + retry ×3 on `DbUpdateConcurrencyException`.
- **Order state transitions are conditional updates**: `UPDATE orders SET status=@new WHERE id=@id AND status=@expected`.
  **[GRILLED — ADR-0005]** 0 rows is *ambiguous*, resolved by an outcome matrix: re-read status → equal to target =
  idempotent success (200); mainline-downstream (`Pending < Paid < Processing < Shipped < Delivered`) = success + WARN,
  never regress; **conflicting terminal** (`Cancelled`/`PaymentFailed` are side branches, not ranked) receiving `Paid` =
  **Payment Anomaly**: no transition, `PaymentAnomalies` row + outbox alert, human refund decision. Transitions still only via `OrderTransitions` rules.
- **Same-user checkout serialization**: quote creation takes a short Redis lock `shop:lock:checkout:{userId}` (SET NX, 10s) *plus* idempotency key (B2) — lock is the belt, key is the braces.
- Keep unique constraints already in place (`Cart.UserId`, `CartItem(CartId,ProductId)`, `Payment.OrderId`, `OrderNumber`) as final backstop.

**Exit criteria:** integration test — 5 units stock, 20 parallel checkouts (Testcontainers) → exactly 5 succeed, `quantity >= 0`, no negative reserved; 10 parallel `add_to_cart(q=1)` same product → final qty = 10 (not lost updates).

### B2 — Idempotency

- Header `Idempotency-Key` (UUIDv4) on `POST /api/checkout/quote` (only money POST after ADR-0003 deletes the legacy endpoint) and all MCP WRITE/QUOTE tools (tool arg `idempotencyKey`, required server-side).
- Store: Redis `shop:idem:{userId}:{routeHash}:{key}` → `{leaseHolderToken, requestHash, state: InProgress|Done, response, createdAt}`, TTL 24h.
  **[GRILLED — ADR-0004]** InProgress is a **renewing lease**: SET NX 30s, holder renews every ~10s via Lua compare-and-renew
  (only the holder may extend); `Done` written via owner-checked CAS so a zombie whose lease was stolen can't overwrite the
  takeover result; abandoned leases expire and the key becomes re-acquirable.
  - Same key + same request hash → replay stored response (`Idempotent-Replay: true`), original status code.
  - Same key, different payload → `409 Conflict`.
  - Still in progress → `409` + `Retry-After`.
- DB backstop where money moves (Redis eviction can't break it): unique `PaymentSessions.SessionToken`, unique `Payment.TransactionId`, unique `OrderNumber`, unique `AiActions.IdempotencyKey`, `ProcessedEvents(EventId PK)` for webhooks/queue consumers, `PaymentAnomalies` (append-only).
- **Webhooks are at-least-once** → dedupe by PSP event id in `ProcessedEvents` before applying.
- **[GRILLED]** Redis down → limiter/idempotency **fail-closed** on money paths (503 + `Retry-After`); small-project SPOF accepted knowingly (ADR-0004).

**Exit criteria:** replay `POST /api/checkout/quote` ×3 with same key → one order, one reservation set, 3 identical responses; different key → new quote (replaces old, see B5); PSP webhook delivered 3× → `Paid` applied once; **holder crashes mid-lease → key re-acquirable after expiry, replay after recovery yields a single quote**.

### B3 — Rate limiting (strict, two layers)

Layer 1 — ASP.NET Core `RateLimiter` middleware (`Program.cs`), Redis-backed partitioner so limits survive multi-instance + shared with MCP server (same Redis):

| Partition | Policy | Notes |
|---|---|---|
| Global per IP | 300/min fixed window | 429 + `Retry-After` + `X-RateLimit-*` headers |
| `POST /api/auth/*` | 10/min per IP | credential-stuffing stop |
| Catalog reads (anon+user) | 120/min per user-or-IP | protects cache-miss stampedes |
| Cart writes | 30/min per user | |
| Checkout/quote create | 6/min per user | matches "human reviews bill" cadence |
| `POST /api/agent/chat` | 10/min per user, 100/day per user | daily counter `shop:rl:agent:day:{userId}:{date}` |
| **`scope=agent` tokens, any REST endpoint** | rejected `403` at the auth edge | **[GRILLED — ADR-0006]** replaces the old "× 0.5 buckets" idea: the token's audience is Shop.Mcp only, so it never reaches a REST bucket at all |

Layer 2 — **MCP server enforces its own budgets independently** (defense in depth: a rogue MCP client connecting directly must not bypass them). Sliding-window counters in Redis per `(userId, riskClass)`:

| Risk class | Budget | Extra guards |
|---|---|---|
| READ tools | 30/min/user | `pageSize<=50`, result JSON ≤ 32 KB (`RESULT_TOO_LARGE` if exceeded), args ≤ 4 KB |
| WRITE (cart) tools | 12/min/user | `idempotencyKey` required; qty ≤ 20/line, ≤ 30 lines/cart |
| QUOTE (`create_quote`) | 4/min/user | 1 Open Quote per user. **[GRILLED]** replace-policy is **origin-scoped** (ADR-0006 glossary term "Origin"): an agent may replace only agent-origin quotes; if a *human-origin* quote is open, `create_quote` fails with typed `QUOTE_HELD_BY_HUMAN` (agent narrates "pay or cancel your existing bill first") — agents can never transitively cancel human money state. Reservation TTL ≥ 10 min apart |
| Loop-level (agent) | ≤ 10 tool calls/turn, ≤ 30 s/turn, ≤ 2 retries/tool | enforced in `AgentLoop` *and* server-side |
| NEVER (pay/refund/admin) | tool does not exist | not "denied" — absent from manifest |

**Exit criteria:** 21st chat in a minute → 429; flood `add_to_cart` through MCP → capped, cart consistent; direct MCP flood never touches Postgres harder than limits allow; human browser path on same user still gets full buckets.

### B4 — Queue + outbox + workers (= existing M10 + M11, extended)

- M10 outbox (unchanged plan): `OutboxMessages` table, same-transaction writes, event catalog v1.
- M11 RabbitMQ + `Shop.Worker`, **plus consumers this plan requires:**
  - `PaymentWebhookConsumer` — applies paid/failed/expired (idempotent via B2).
  - `QuoteExpirySweeper` — every 30s: quotes past `ExpiresAt` → `Cancelled` + release reservations (conditional-update guarded, so racing with a late webhook is safe: first transition wins; a `succeeded` landing after that → PaymentAnomaly (ADR-0005)). Also flips the matched `AiActions` row to `Expired` (A6).
  - Email (log stub) + Analytics (`UserBehaviorEvents` append — collected for *ops*, not ML personalization; M18 stays backlog).
   - **[GRILLED] `RetentionSweeper`** — nightly: hard-delete `AiConversations`+`AiMessages` inactive > 30d (`Ai:ConversationRetentionDays: 30`); `AiToolCalls` ledger kept 60d (`Ai:ToolCallLedgerRetentionDays: 60`). Chat *text* is never readable by admins (audit surface = ledger metadata only; `conversationId` reads are ownership-checked); sweeper logs deleted counts.
- Publisher: `FOR UPDATE SKIP LOCKED` batch claim, `messageId=eventId`, 3× backoff → DLQ `shop.events.dlq`.
- Outbox retention: delete `Sent` > 30 days.

### B5 — Payment re-cut: quote → bill → PSP-hosted page (Assist flow)

**[GRILLED — ADR-0003]** V1 `POST /api/checkout` is **deleted**, not gated — one money path only. Test convenience is replaced by FakePsp's Pay/Cancel page (dev) and **Stripe test mode** (CI/staging, milestone 13). Legacy `Paid`/`Pending` V1 rows remain as read-only history; legacy Pendings have no `ExpiresAt` so they are by definition not "Open Quotes" (excluded from sweeper + replace policy). New checkout shape:

```text
POST /api/checkout/quote        (human REST *or* MCP create_quote — identical service call)
  → validate cart + reprice server-side (rule §2.1 of PLAN.md unchanged)
  → atomic reserve stock (B1) per item
  → Order{Status=Pending, ExpiresAt=+30m}, PaymentSession{SessionToken=URL-safe 128-bit, Provider=FakePsp|Stripe}
  → response: bill {items, subtotal, shipping, total, expiresAt} + hostedPayUrl
  → cart is NOT cleared yet (human may still tweak → invalidates/replaces quote, releases old reservation)

GET  /pay/{sessionToken}        (FakePsp "hosted page" — capability-token model, ADR-0002:
                                 the 128-bit SessionToken authorizes VIEWING the bill + Pay/Cancel buttons
                                 (mirrors Stripe Checkout UX). Token is read-only, single-use, expires with the
                                 quote; /pay/* excluded from request logging (URLs are credentials);
                                 an agent MAY read this invoice view but can never act on it)
POST pay confirm                (requires the HUMAN's bearer JWT with userId == PaymentSession.UserId,
                                 rejects scope=agent; dev: emits a signed fake webhook; prod: Stripe redirect + webhook)

PSP webhook → POST /api/payments/webhook (per-adapter signature verify, event-id dedupe, enqueue → worker)
   **[GRILLED] normalized provider event contract** (so the Stripe swap = new parser, zero consumer change):
   `PaymentProviderEvent { ProviderEventId (dedupe key → ProcessedEvents), SessionToken,
                           Outcome: Captured|Failed|Expired, AmountMinor, Currency, OccurredAt, RawType }`
   succeeded → conditional transition Pending→Paid + atomic commit reservation (qty-=, reserved-=)
               + clear quoted lines from cart + outbox order.paid → email/analytics
   amount/currency mismatch vs PaymentSession, or succeeded into a conflicting terminal state
               → PaymentAnomaly path (ADR-0005): no transition, anomaly row + outbox alert, human decides
   failed    → Pending→PaymentFailed + release reservations + outbox payment.failed
   expired   → same as sweeper path (idempotent with it)
```

- Money never flows through MCP: agent can `create_quote` and *read back the bill + a URL string*; confirm on `/pay` requires the human's own JWT and rejects `scope=agent` (auth-level test).
- `IPaymentProvider` abstraction: `FakePspProvider` (dev default) + `StripeCheckoutProvider` in **test mode** (`sk_test`) for CI/staging — **[GRILLED]** milestone 13 builds the Stripe test-mode provider + webhook signature verification; live-mode keys / prod go-live stay outside this plan. `docs/PLAN.md` deferred Stripe to M20 backlog; keep the seam built in B5, implementation lands at milestone 13.
- Migration additions: `PaymentSessions`, `OrderItem.ReservedQuantity`, `Order.ExpiresAt`, `ProcessedEvents`, `PaymentAnomalies` (append-only: orderId, sessionToken, provider event id, observed vs current status).

**Exit criteria:** scripted e2e (integration test): quote → reservation visible (`ReservedQuantity` moved, `AvailableQuantity` dropped) → human confirm on fake hosted page → Paid + stock committed + cart cleared; expire path → Cancelled + reservation released; webhook replay ×3 → single transition.

### B5+ — Client-facing forward contracts (settled in the frontend architecture review, 2026-11-09)

Drafted from contracts already exercised end-to-end behind the `fe/` mocks — F8 conformance becomes a check, not a redesign.

1. **`payment.outcome`** — client-visible discriminator on `PaymentDto`: `captured | failed | expired | null`, set by the same webhook/sweeper transitions that move OrderStatus. Rule: no client may infer "expired" from a null transaction id or an elapsed `expiresAt`; the Bill, PSP page and order detail read the outcome as truth (closes the `!transactionId` heuristic — review candidate C1).
2. **Canonical quote read shape + discovery** — `{items, subtotal, shipping, total}` is a *view* of the Order; both quote responses return the order-embedded DTO: `POST /api/checkout/quote → 201 {order: OrderDto, hostedPayUrl, expiresAt}`, plus new **`GET /api/checkout/quote`** (human JWT; `scope=agent` 403) → `{order, hostedPayUrl, expiresAt} | null` for the active Open Quote — the honest backend capability behind "the bill survives reload / a second tab". The session token is delivered as its own field (`order.payment.sessionToken` or the URL), and **clients must never decompose `hostedPayUrl`** (prod = cross-origin Stripe; the mock's `/psp/<token>` route is dev-only).
3. **Chat streaming contract (A5 response)** — `POST /api/agent/chat` streams SSE frames `{type:"delta", text}` … `{type:"turn", result}` or `{type:"error", code, message}`; non-stream failures use the standard envelope `{code, message, correlationId, retryable}`. Rate limit = honest HTTP `429 + Retry-After` **and** `code:"RATE_LIMITED"` (client convenience). `AgentTurnResult` carries `turnCap` + `turnsUsed` (server-enforced per-conversation cap) so the UI renders "turn x/N" without hardcoding. `X-Correlation-Id` echoed on the response.
4. **REST spec filter grammar (A3)** — `?specs=<key>:<op>:<value>` clauses joined by `;`; `op ∈ gte|lte|eq|in` (`in` values pipe-separated). Mirrors the MCP `specs: [{key, op, value}]` shape one-to-one. Unregistered key or malformed clause → `400`, **fail-closed** — never silently ignored. Facets DTO: `[{key, label, type: range|enum, unit, values?, min?, max?, count}]` (`count` = additive front-end need).

### B6 — Cross-cutting resilience (small)

- Polly (or manual wrappers) for: PSP HTTP calls (retry ×2 + circuit breaker), AI provider (retry on 429/5xx w/ jitter, 20s timeout), Redis & RabbitMQ (fail-open for cache, fail-safe for limiter = **deny if Redis down for rate/idempotency counters**, log + `/health/ready` red).
- `X-Correlation-Id` middleware (request → logs → outbox payload → MCP tool trace) — pre-existing requirement of M10.
- Cache policy unchanged (M9) + new: `shop:facets:{categoryId}` TTL 10m evicted on product/inventory writes (see A3).

---

## 6. AI assistant + MCP (A-track)

### A1 — MCP foundation (= M12, limits pulled forward)

- New `backend/Shop.Mcp/` on official MCP .NET SDK; transports STDIO (dev) + Streamable HTTP `/mcp` (hosted).
- `McpToolContext{UserId, Role, Scope, CorrelationId, IdempotencyKey?}` from delegated JWT. Anonymous allowed only for `search_products`/`get_product`/`get_product_facets`; everything else 401-shaped `UNAUTHORIZED` error envelope `{code,message,correlationId,retryable}`.
- **[GRILLED — ADR-0006]** `scope=agent` JWTs are minted **only by AgentLoop** server-side (exchange of the incoming human bearer; anonymous chat gets a token with empty `sub` restricted to the 3 public READ tools), TTL 60 s, single turn, never serialized to any client, never logged. **Shop.Api 403s any `scope=agent` token on every REST route** (audience is Shop.Mcp only) — auth-level test in §8.
- **[GRILLED — ADR-0006]** The MCP tool manifest is *generated* from `IToolRiskPolicy` (Shop.Application = single tool↔risk-class source) filtered by `Tools:{name}:Enabled`: `NEVER`/disabled tools are never registered — capability absent mechanically. MCP-side enforcement is authoritative (rogue direct clients); AgentLoop checks are fail-fast only. A tool in the manifest without a policy entry fails the build.
- Architecture test: `Shop.Mcp.csproj` must not reference EF/Npgsql (fails build in CI).
- Compose: add `mcp` service sharing app config; Redis for its counters.

### A2 — Assist toolset (= M13 minus payment)

```text
READ:   search_products(+specs filter)  get_product  get_product_facets  compare_products(2..4)
        get_cart  get_orders  get_order  get_order_status
WRITE:  add_to_cart  update_cart  remove_from_cart  (idempotencyKey required)
QUOTE:  create_quote → bill + hostedPayUrl (for the HUMAN to open; not actionable by agent;
        origin-scoped replace: refuses with QUOTE_HELD_BY_HUMAN when a human-origin quote is open)
NEVER:  pay, refund, cancel_order (cancel stays human-only at Assist), any admin
```

- `compare_products` returns a deterministic side-by-side (price, brand, category, stock, extracted spec rows) — no LLM synthesis inside the tool; the agent narrates.
- Golden scripted scenario (test, not doc): search laptop → 2× get_product → compare → add_to_cart → create_quote → bill totals match REST cart exactly.

### A3 — "Suggestion by asking item properties" (non-ML, replaces M17/M18 as the initial-state finder)

- `Product.Specs jsonb` (e.g. `{"ram_gb":32,"cpu":"Core i7","storage_gb":1000,"use_case_tags":["dev"]}`) + seed values for catalog.
- **[GRILLED — ADR-0007]** Seed-only v1. A **per-category specs registry** (`key, type: range|enum|bool, unit, label`) in code/config is the single source for three things: facet derivation (registry ∩ populated data; unregistered jsonb keys ignored), `search_products` validation (filter on unknown key → 400, so the agent can't hallucinate a filter into empty results), and CI validation of seed specs. Admin Specs editing = backlog.
- `ProductService.GetFacetsAsync(categoryId)`: distinct **registered** keys + value ranges/enum sets across Active products → `[{key,label,type:range|enum,unit,values?,min?,max?}]` cached 10m.
- `search_products` gains `specs: [{key, op: gte|lte|eq|in, value}]` → SQL against jsonb (GIN index on `specs`).
- **Elicitation policy lives in the agent prompt + a config mapping table** `Agent:UseCaseHints` (e.g. "backend dev" → `ram_gb>=16, cpu strong`): when the user's requirement is fuzzy, the agent must call `get_product_facets` first and ask ≤ 3 targeted questions before filtering; ranking = deterministic (price asc / in-stock / facet match count), with explanation built from the filters it used. No behavior events, no embeddings, no vectors — explicitly not a recommendation system.

### A4 — AI provider abstraction (= M14, one OpenAI-compatible adapter)

`IAiProvider` + one `OpenAiCompatibleProvider` + `FakeAiProvider` (deterministic, used by all CI tests). The common adapter speaks OpenAI Chat Completions with `tools`; presets cover `opencode-go` (dev default: `deepseek-v4.1-flash`), `deepseek`, `openrouter`, `openai`, `groq`, and `ollama`, with `Ai:BaseUrl` for anything else. `opencode-go` also sends `x-opencode-session` and a descriptive `User-Agent`. Switching providers is `Ai:Provider` + the key only; each preset supplies a default model, and `Ai:Model` / `Ai:BaseUrl` override it. Env form `Ai__Provider` / `Ai__ApiKey` / `Ai__Model` / `Ai__BaseUrl`. Native APIs (Anthropic Messages, Gemini native, OpenAI Responses) stay behind a future adapter. Key from env only; `AI_NOT_CONFIGURED` never breaks non-AI routes.

### A5 — Agent chat loop (= M15, hardened)

- `POST /api/agent/chat {message, conversationId?}` → `AgentLoop`: system prompt ("never invent price/stock — every number from a tool call in this run"; "you may quote, never pay"; "elicit specs before recommending") + history window — **[GRILLED]** "system + last 10 *turns*" (turn = user msg + all assistant/tool-call/tool-result messages of that turn; trimmed **whole-turn** to `Agent:MaxPromptTokens`, never mid-turn so tool-call/result pairing stays legal and the system prompt stays byte-stable for provider caching) + M13 tool registry via MCP client.
- Budgets from B3 table; `AiConversations`/`AiMessages` persisted (retention: B4 RetentionSweeper); `pendingQuoteId` surfaced in response.
- **[GRILLED]** `POST /api/agent/chat` **requires login** in v1 (cost caps are per-user; anonymous conversations would force a merge/re-key mess). Anonymous browsing stays plain REST + the 3 anonymous MCP READ tools; the §B3 chat buckets are per-user only.

### A6 — Quote review hand-off (= M16 at Assist level, simplified)

- Chat UI renders the bill from `create_quote` result with one human-only action: **"Review & Pay"** → opens `hostedPayUrl` in browser (the agent never navigates it). **[GRILLED]** UI target = the vanilla `frontend/` SPA (milestone 11 "minimal frontend"); `fe/` (Next.js mock app) is explicitly **out of scope** of this plan and not wired to the real API by it — say so in `fe/README`.
- `AiActions` table still created (type `CreateQuote`) for audit + future Delegated mandate reuse. **[GRILLED — Q18] Assist lifecycle:** written `Executed` when `create_quote` returns success; `Rejected` when blocked by policy/rate-limit/`QUOTE_HELD_BY_HUMAN`; `Expired` when its quote expires unpaid (set by the sweeper). `Pending` exists only for Delegated (M22). The row is also the **origin tag** (agent-created ⇒ replaceable by the agent). No confirm-to-execute needed for quotes (quote itself is the human-review artifact), unlike the old M16 where confirm ran checkout.
- Price/stock revalidated at quote creation (server recalcs — rule #1 unchanged); if cart changes after quote → replace quote (release + re-reserve atomically) **respecting origin scope** (B3 table).

### A7 — AI safety & observability (= M19; do not defer past A5)

- `AiToolCalls` ledger per call (tool, masked args, status Ok/Error/Denied, durationMs, tokens), `GET /api/admin/ai/calls` + `GET /api/agent/conversations/mine`.
- Kill switches, hot-reloadable: `Ai:Enabled`, `Agent:Enabled`, `Tools:{name}:Enabled`, `Agent:TrustLevel` (Assist enforced; Delegated rejected unless mandate tables + spend ledger exist).
- Prompt-injection posture: tool outputs are data, never re-instruction; tool results truncated; blocklist check on message input (max 2000 chars); PII stripped from prompts except bill summary (last-4 address style masking).

### Delegated level (future milestone, M22) — seam only, not built now

`AiMandates(UserId, CategoryAllowList, AmountCapMinor, MerchantScope, PaymentMethodRef, ExpiresAt)` + per-mandate spend ledger + notification events + step-up flow when cap exceeded. Explicitly out: Autonomous (stored card anywhere) — will not be implemented.

---

## 7. Milestone order (execution sequence)

| # | Milestone | = old plan | Depends | Exit artifact |
|---|---|---|---|---|
| 1 | **B1** race-safe stock & cart (xmin + atomic guarded updates + transition guards) | *new (fix)* | M9 | ✅ **done 2026-11-09** — xmin tokens on Products/Orders/Inventories/Carts/CartItems + migration; guarded `TryReserve/TryCommit/TryRelease` primitives; checkout decrement is one guarded statement per line; transitions are conditional updates with ADR-0005 replay resolution; cart add is reload-and-retry. Green: `StockReservationTests` (oversell storm, commit/release race — exactly one wins), `CartConcurrencyTests` (10 parallel adds → qty 10), `ConcurrencyTokenTests`, `OrderTransitionConcurrencyTests` (typed loser + idempotent replays), `CheckoutConcurrencyTests` (**exit criterion**: 5 users / 3 units → exactly 3 Paid + 2×409, stock 0), 41 unit + 29 integration |
| 2 | **B2** idempotency store + `Idempotency-Key` on checkout/quote paths | *new* | M9 | ✅ **done 2026-11-09** — `IIdempotencyStore` renewing-lease store in Redis (ADR-0004): SET-NX acquire, holder-only Lua compare-and-renew, owner-checked CAS→Done (stolen-lease zombie cannot overwrite takeover), expired leases re-acquirable, fail-closed `IdempotencyUnavailableException`. `IdempotencyMiddleware` + `[RequireIdempotency]`: UUIDv4 header (400), replay ×3 → one execution + `Idempotent-Replay`, mismatch 409, in-progress 409 + Retry-After, crash releases lease, store-down 503 + Retry-After with zero executions. 19 new tests (`IdempotencyStoreTests`, `IdempotencyHttpTests` + probe; quote itself gets the attribute at B5); `SHOPMIND_TEST_REDIS` seam + per-factory key-prefix purge. Green: 41 unit + 48 integration |
| 3 | **B3-L1** REST rate limiting middleware (Redis partitioner) | *new, was M20* | — | ✅ **done 2026-11-09** — `RateLimitMiddleware` (house pattern after auth, before idempotency) enforcing the plan table verbatim: global 300/min/IP, auth 10/min/IP, catalog 120/min user-or-IP, cart-writes 30/min/user, checkout/quote 6/min/user (`RedisRateLimitCounter` fixed window — INCR+PEXPIRE+PTTL Lua, shared across instances via the same Redis). Contract: 429 + `Retry-After` + `X-RateLimit-Limit/Remaining/Reset` + typed envelope; fail-CLOSED 503 + Retry-After when Redis is down (ADR-0004). `scope=agent` REST rejection stays auth-level (A1/B5); agent-chat 10/min+100/day bucket lands with A5; Layer 2 MCP budgets are A-series. Green: `RateLimitCounterTests` (permit math, partition isolation, window reset, fail-closed throw) + `RateLimitHttpTests` (global burst 429 w/ headers, credential-stuffing 4th login refused while catalog fine, per-user catalog/cart/checkout buckets, reads not counted, dead-Redis 503 zero-handler) — 41 unit + 59 integration |
| 4 | **M10** outbox + events + correlation id | M10 | — | ✅ **done 2026-11-09** — `OutboxMessages` table (jsonb payload, Status smallint Pending/Sent/Failed, `(Status, OccurredAt)` index, migration `AddOutboxMessages`); `IDomainEvent` + locked catalog v1 records (order.created/paid/cancelled/shipped/delivered, payment.failed, product.created/updated); `IEventPublisher.EnqueueAsync` composes payload contract v1 (`eventId` = row id, `eventType`, `occurredAt`, `aggregateType/Id`, `userId?`, `correlationId?`, `data`, `schemaVersion:1`) on the CALLER's DbContext — never a self-save. Wired into durable flows only: `OrderService` cancel + admin transitions (event enqueued only on `Transitioned` — ADR-0005 replays fire nothing; fixed a latent double-stock-restore where a losing concurrent cancel restored on replay) and `ProductService` create/update. `CorrelationIdMiddleware` first in pipeline: reuse/generate `X-Correlation-Id`, response echo, log scope, `CorrelationContext` → outbox payload (end-to-end test). Legacy `/api/checkout` deliberately unwired (deleted at B5 — created/paid/payment.failed move to the quote/webhook flow with their tests). Green: `OutboxPublisherTests` (contract, rollback atomicity, correlation), `OutboxFlowTests` (per-flow emission matrix incl. replay/invalid transitions), `CorrelationIdTests`; 41 unit + 72 integration |
| 5 | **B4** RabbitMQ + `Shop.Worker` + DLQ + dedupe consumers | M11 | 4 | ✅ **done 2026-11-09** (broker e2e deferred to CI by strategy) — `Shop.Worker` Worker Service: `OutboxPublisherWorker` (poll 2s, batch 50, exponential broker-down backoff ≤60s), `EmailConsumerWorker` (log stub) + `AnalyticsConsumerWorker` (`UserBehaviorEvents` append), `OutboxRetentionWorker` (purge `Sent` >30d). Engine in Infrastructure, hostless & testable: `OutboxClaimService` — claim is ONE statement (`UPDATE … WHERE Id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING`, Attempts++ at claim) wrapped in a round transaction held through publish+mark → concurrent publishers provably never double-publish; broker fault keeps rows Pending, `MaxAttempts` exhaustion marks `Failed`. `RabbitMqPublisher` (v7 topic `shop.events`, messageId = eventId, publisher confirms). `ConsumerPipeline`: dedupe INSERT inside the SAME transaction as consumer writes (unique `(EventId, Consumer)` arbiter) → fault rolls back → redelivery reprocesses → 3rd redelivery → DLQ (`x-death` counts, `x-dead-letter-exchange`, manual acks, prefetch 10). Tests: `OutboxDeliveryTests` (6: oldest-first+attempts, skip sent/failed, concurrent rounds disjoint, happy round messageId contract, broker-down→Failed ladder, crash-after-delivery at-least-once), `ConsumerPipelineTests` (7: dedupe ledger, duplicate skip, per-consumer keys, fault→rollback→reprocess, poison→DLQ untouched consumer, analytics exactly-once append, retention only-old-Sent). `RabbitE2ETests` (publish→consume→dedupe + restart-survival) run in CI via Testcontainers.RabbitMq, self-skip without a container runtime — the fake-broker suites cover the same invariants locally. Compose +worker +rabbitmq (4-management) and repaired stale `src/` Dockerfile paths; broker-down guarantee = M10 API path + Pending rows + backoff drain. Green: 41 unit + 87 integration |
| 6 | **B5** quote → reservations → FakePsp hosted page + webhook + sweeper; **delete legacy `/api/checkout`** (ADR-0003) | *new, replaces M5 sync payment* | 1,2,4,5 | ✅ **done 2026-11-10** — schema: `PaymentSessions` (unique 128-bit url-safe capability token, unique OrderId single-use), `Order.ExpiresAt`/`Order.Outcome` (server-stated captured/failed/expired — B5+ "never infer"), `OrderItem.ReservedQuantity`, `PaymentAnomalies` (append-only). `QuoteService` = one money path: reprice + B1 guarded reserve + replace-policy (cancel+release+re-reserve in ONE transaction; legacy V1 Pendings invisible — ADR-0001) + session + `order.created`. `POST /api/checkout/quote` [RequireIdempotency] (B2's deferred replay-×3→one-quote criterion closed) + GET discovery + rate bucket covered by B3; `scope=agent` rejected 403 on quote/confirm (ADR-0006). Pay page: capability-token bill view (anonymous) + human-owner-only confirm that only ISSUES the provider webhook (money state decided by the consumer — ADR-0003); `/pay/*` request-log redaction tested via log sink. Intake: HMAC verify (tamper → 401, nothing enqueued) → `payment.webhook-received` outbox row → `PaymentWebhookQueueWorker` → `PaymentWebhookConsumer` = the ADR-0005 matrix in code: dedupe by (ProviderEventId, "payment-webhook") in the fate transaction; captured→commit+clear quoted lines+Payment+order.paid; failed→release+payment.failed; expired→sweeper core; conflicting-terminal/amount-mismatch→anomaly row + `payment.anomaly` alert (catalog v1 extended by the plan's own ADR), no transition. Sweeper: 30s-safe, conditional, late-capture-wins both directions, plus latent-bug fix: user cancel of a QUOTE now releases reservations (was legacy-only restore). Deleted: CheckoutController/Service/Validator/DTOs/IFakePaymentService + their tests — oversell storm (5 users/3 units → 3 holds + 2 typed 409s) re-cut at the quote path, human path scripted e2e, triple webhook → one transition. Tests: 5 schema + 9 quote service + 5 HTTP idempotency + 1 rate + 6 pay page + 3 intake + 9 consumer matrix + 3 sweeper + 4 e2e + 5 re-cut; 41 unit + 130 integration, suite stable ×3. Stripe test-mode provider + webhook signature remain milestone 13 behind `IPaymentProvider` |
| 7 | **A3** product specs + facets + spec-filtered search (REST) | *new, non-ML* | — | ✅ **done 2026-11-10** — `Product.Specs` `jsonb` + GIN index (migration `AddProductSpecs`; `{}` default for legacy rows). `SpecRegistry` = one 24-key per-category vocabulary (mirrors `fe/src/lib/attributes`); `SeedCatalog` carries the fixture specs and `SeedCatalog.Validate()` gates seed-time + CI (registered key, category membership, type). `SpecFilterCodec` implements the locked `?specs=key:op:value[;...]` grammar (`in` = pipe-separated, range gte/lte/eq, enum eq/in, bool eq, case-insensitive string match) and **fails closed** on unknown key/op/bad value → 400 `InvalidSpecFilter`. `SpecFilter` translates clauses to SQL JSONB predicates (no post-materialization filtering); `ProductDto`/`ProductDetailDto` now carry `specs`. `GET /api/products/facets?category=` = registry ∩ populated Active data (drafts/inactive/unregistered keys ignored, bools never facets), range min/max + enum values + distinct `count`, cached 10 min at `shop:facets:{category}` and evicted by product/category/inventory writes (B6 cache policy). Agent seam built pure: `ElicitationPolicy` (fuzzy request → facet discovery before filtering, ≤3 targeted questions, explicit requirements always preserved, use-case hints only after facet discovery and only when the facet supports them, explanations built solely from applied filters) + deterministic `SpecRanking` (facet-match count → in-stock → price → id). No LLM/embedding/vector/behavior dependency (architecture guard test); A5 wires the policy into the loop. Tests: 26 unit (registry/codec/seed/policy/ranking/scope/cache-json) + 19 integration (13 JSONB search incl. category-scoped 400 matrix + 4 facets + 2 cache/TTL/eviction); full suite green 67 unit + 149 integration on disposable Postgres 17 + Redis. |
| 8 | **A1** MCP foundation (auth, validation, error envelope, **B3-L2 MCP budgets**) | M12 | 3 | ✅ **done 2026-11-10** — new `backend/Shop.Mcp` on the official MCP C# SDK (`ModelContextProtocol.AspNetCore` 2.4.0), one host with two transports: stateless Streamable HTTP `/mcp` + `--transport stdio` (logs to stderr, stdout reserved for the protocol). Manifest is **generated** from `IToolRiskPolicy` (Shop.Application) + `Tools:{name}:Enabled`: A1 registers `search_products`, `get_product`, `get_product_facets`, `get_cart`; `confirm_payment` is `NEVER` and absent; disabled tools absent; tests fail if a tool method lacks a policy entry or `Shop.Mcp` source/direct packages reference EF/Npgsql. `McpToolContext` comes from the delegated JWT (`sub`, role, `scope`, correlation, idempotency) or STDIO dev env; the three public READ tools are anonymous, `get_cart` requires auth; `Shop.Api` now rejects `scope=agent` on every `/api/*` route with `403 AgentScopeForbidden` (auth-level test; anonymous `/pay/{token}` view stays allowed). One `ToolExecutor` guard: authz → strict args (unknown fields rejected) → Redis sliding-window budget (READ 30/min, WRITE 12, QUOTE 4; fail-closed) → Application service → structured `{code,message,correlationId,retryable}` envelope; args ≤4KB, results ≤32KB, pageSize ≤50; logs tool/status/duration/correlation/userIdHash, never JWT or arguments. Compose `mcp` service (5001:8080) + Dockerfile + `backend/Shop.Mcp/README.md` and main README. Tests: 23 unit (policy/context/guard/architecture) + 22 integration (HTTP manifest/parity/ownership/401/unknown args/args limit/budget; real STDIO process; REST agent-scope 403; Redis sliding-window counter; kill-switch manifest); full suite green 90 unit + 171 integration on disposable Postgres 17 + Redis. A2 closed this in milestone 9. |
| 9 | **A2** Assist toolset (cart + compare + `create_quote` via MCP) | M13′ | 6,7,8 | ✅ **done 2026-11-10** — `ToolRiskPolicy` now lists 12 live tools; `confirm_payment`, `cancel_order`, and `refund_payment` stay `NEVER` and absent. READ: `compare_products` (2–4 ids) is a pure `ProductComparisonBuilder` in Application — caller order, registry-ordered spec rows, `null` for an absent spec, unregistered jsonb keys dropped, available stock only (no reserved leak), no LLM; `get_orders`/`get_order`/`get_order_status` are owner-scoped through `IOrderService` with explicit page validation (the service's silent clamp is not exposed); `search_products` gains `specs: [{key,op,value}]`, mapped to the A3 fail-closed codec so an unknown key is `INVALID_ARGS`, never an unfiltered page. WRITE: `add_to_cart`/`update_cart`/`remove_from_cart` reuse `ICartService` and the REST validators, so merge/stock/ownership match HTTP. QUOTE: `create_quote` reuses `IQuoteService` with `QuoteOrigin.Agent`; `Order.Origin` (migration `AddQuoteOrigin`, default `Human` backfill) + typed `QuoteHeldByHumanException` (`QUOTE_HELD_BY_HUMAN`) make the replace policy origin-scoped — an agent may replace only agent-origin quotes, while a human open quote throws before any transition or release; REST passes `Human`, so the human path is unchanged. One `ToolExecutor` guard derives the mandatory `idempotencyKey` from the WRITE/QUOTE risk class, uses the existing fail-closed `IIdempotencyStore` under `mcp:{user}:{tool}:{key}` with a SHA-256 args hash and renewing lease, replays the stored result, maps mismatch/in-progress/outage to `IDEMPOTENCY_MISMATCH`/`IDEMPOTENCY_IN_PROGRESS`/`IDEMPOTENCY_UNAVAILABLE`, and releases the lease on a failed attempt. Tests: 21 new unit (risk policy, guard idempotency matrix, compare shape) + 25 new integration (12-tool manifest, spec filter, compare, two-token cart and order ownership, replay/mismatch, over-stock, validator reuse, agent-vs-human quote matrix incl. an elapsed human quote, scripted search→product→compare→add→quote totals matching the REST cart, dead-Redis write refusal); full suite green **111 unit + 196 integration** on disposable Postgres 17 + Redis. |
| 10 | **A4** one OpenAI-compatible provider (presets incl. OpenCode Go/DeepSeek) + FakeAiProvider | M14 | — | ✅ **done 2026-11-11** — `IAiProvider.GenerateAsync(AiRequest) → AiResponse` in `Shop.Application/Ai`; the closed `AiMessage` hierarchy (system/user/assistant-with-text-and-tool-calls/tool-result-with-call-id) makes illegal turns unrepresentable. `OpenAiCompatibleProvider` keeps every wire DTO private and maps one normalized shape: text or tool calls (`CallId`, `Name`, raw `ArgumentsJson`), `AiUsage` input/output, model, duration, normalized `AiFinishReason` (unknown provider value never aborts the turn). Preset table covers `opencode-go` (default `deepseek-v4.1-flash`, `x-opencode-session` + ShopMind UA), `deepseek`, `openrouter` (referer/title headers), `openai`, `groq`, `ollama`; `Ai:BaseUrl`/`Ai:Model` are overrides only, so switching a provider is `Ai:Provider` + the key. Key from `Ai__ApiKey`; a missing key or unknown preset throws typed `AI_NOT_CONFIGURED` at call time, never at startup or DI resolution. Adapter retries 2× with jitter on 429/5xx/network, 20s timeout, and maps every failure to `AiProviderException` with an honest `Retryable` flag. `FakeAiProvider` is network-free and scriptable (queue, responder, or last-user echo) and is selected by `Ai:Provider=fake`; the integration factory uses it. Tests: 25 contract tests over a stub `HttpMessageHandler` (request mapping incl. tool round-trip, every finish reason, usage, retry/network/timeout, non-2xx, malformed body, missing key, ollama keyless, all presets) + 3 fake + 3 composition/tool-call smoke through the real DI root; 142 unit green. Integration suite not re-run locally (no Redis/container runtime); A4 adds no DB migration and no HTTP route. Live keyed cassette against a real preset stays manual (no key in this environment). |
| 11 | **A5+A6** agent chat loop + bill hand-off UI (minimal, vanilla `frontend/` only) | M15+M16′ | 9,10 | golden chat scenario e2e |
| 12 | **A7** AI ledger, admin audit, kill switches, token budgets | M19 | 11 | trace of demo run |
| 13 | **M20/M21** hardening + deploy (CORS lockdown, JWT refresh, **Stripe Checkout test-mode provider** (sk_test, webhook signature verify) + prod compose with rabbit+mcp+worker; live-mode go-live stays outside this plan) | M20/21 | all | demo deployed on Stripe test mode |
| — | Backlog (explicitly deferred): pgvector/hybrid (M17), ML personalization (M18), Delegated mandates (M22) | | | |

Branch naming: `b1-stock-race`, `b2-idempotency`, … `a5-agent-chat` (follows PLAN.md appendix E style).

---

## 8. Test strategy (per feature, CI-greenable without API keys)

- **Unit:** risk policy (every tool ↔ class), facet extraction from specs jsonb, idempotency key/hash logic, rate-limit window math (fake clock), reservation math on `Inventory` (new methods).
- **Integration (Testcontainers Postgres + Redis + RabbitMQ):**
  - oversell storm (B1), lost-update storm on cart (B1), quote replay ×3 (B2), webhook triple-delivery (B2),
  - expire-vs-pay race (exactly one winner), broker-down outbox recovery, DLQ poison message,
  - rate buckets per table (B3), **agent-scope token: can VIEW `/pay/{token}` bill but cannot CONFIRM it, and is 403'd on every REST route incl. `/api/agent/chat` (B5/A1/ADR-0002+0006 security tests)**, anonymous `POST /api/agent/chat` → 401, idempotency-holder-crash recovery (B2), late-`succeeded`-after-`Cancelled` → PaymentAnomaly row + no transition (ADR-0005), `QUOTE_HELD_BY_HUMAN` typed error (B3/A6), MCP manifest completeness = policy entries (ADR-0006), `/pay/*` absent from request logs (ADR-0002),
  - MCP tool matrix mirroring CartCheckoutTests through MCP client (M12/M13 plan).
  - golden chat scenario with `FakeAiProvider`: fuzzy ask → agent asks ≥1 facet question → filters → compare → add → `create_quote` → order `Pending`, stock reserved, **no PaymentSucceeded row exists** (Assist invariant). **[GRILLED]** Fake scripts are a *transcript tape*: keyed on normalized transcript prefix (latest user msg + multiset of tool names/results so far); unmatched state fails the test loudly; assertions on tool-call sequence + DB end state, not prose.
- **Load smoke (manual, documented):** 100 concurrent catalog reads p95 ≤ 2× single; 10 parallel quotes on same user → single active quote per replace policy; k6 script referenced in README, not committed to CI as flaky gate.

---

## 9. Deltas vs `docs/PLAN.md` (to reconcile in that file when implementation starts)

1. **M5 checkout shape changes**: synchronous fake payment inside request → quote/reserve + human PSP page (B5). `SimulateFailure/SimulateTimeout` move to the FakePsp page, not the quote request.
2. **M16 HITL semantics change at Assist**: confirmation *is the payment act itself, done off-agent*; agent path stops at `create_quote`. `AiActions` kept for audit + future Delegated.
3. **M17/M18 demoted to backlog**: user requires non-ML, attribute-questioning suggestions → replaced by A3 (specs/facets + deterministic filter). `UserBehaviorEvents` table still lands via B4 analytics worker (ops value), but no personalization engine initially.
4. **M19 rate-limiting partially pulled forward** to B3 (needed *before* MCP tools exist, not after agent ships) and split into two layers (API + MCP).
5. **New first-class items**: B1 (race fix — this is a V1 bug), B2 (idempotency), B5 (payment sessions), A3 (facets), M22 (Delegated seam).
6. Tool taxonomy appendix B updated: `cancel_order` moves to human-only at Assist (was SENSITIVE_WRITE for agent); add `get_product_facets` READ; `create_quote` QUOTE class (never executes payment).
7. **[GRILLED 2026-11-06]** V1 `POST /api/checkout` deleted (ADR-0003); pay page = capability token (ADR-0002); Quote = role of Order (ADR-0001); transition outcome matrix + `PaymentAnomalies` (ADR-0005); idempotency renewing lease + fail-closed Redis (ADR-0004); manifest generated from risk policy, agent-scope JWT REST-403, chat requires login (ADR-0006); specs registry, elicitation-not-ML (ADR-0007); retention: conversations 30d / tool-call ledger 60d, admin never reads chat text; `fe/` Next.js app out of scope. Vocabulary canonical in `GLOSSARY.md`.

---

## 10. Config sketch (appsettings + env)

```json
{
  "Redis": { "ConnectionString": "localhost:6379", "Enabled": true, "DefaultTtlSeconds": 90 },
  "RabbitMq": { "Host": "localhost", "User": "shop", "Pass": "<env>" },
  "RateLimit": { "GlobalPerIpPerMin": 300, "AuthPerIpPerMin": 10, "ReadsPerMin": 120,
                 "CartWritesPerMin": 30, "QuotesPerMin": 6, "AgentChatPerMin": 10, "AgentChatPerDay": 100 },
  "Mcp":     { "ReadPerMin": 30, "WritePerMin": 12, "QuotePerMin": 4 },
  "Payments": { "Provider": "FakePsp", "WebhookSecret": "<env>", "QuoteTtlMinutes": 30,
                "StripeTestSecretKey": "<env, milestone 13 — sk_test only; live keys outside this plan>" },
  "Ai":       { "Provider": "opencode-go", "Model": "deepseek-v4.1-flash", "ApiKey": "<env only>", "Enabled": true,
                "ConversationRetentionDays": 30, "ToolCallLedgerRetentionDays": 60 },
  "Agent":    { "TrustLevel": "Assist", "MaxToolCallsPerTurn": 10, "MaxTurnSeconds": 30,
                "MaxRetriesPerTool": 2, "HistoryTurns": 10, "MaxPromptTokens": 8000,
                "Enabled": true, "AgentTokenTtlSeconds": 60 }
}
```

Compose additions on top of current `docker-compose.yml` (postgres/api/redis): `rabbitmq:3-management`, `worker`, `mcp`; prod later: pgvector image swap only when M17 un-defers.

## 11. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Agent retries double-add/double-quote | B2 idempotency keys mandatory on MCP writes + B1 locks + origin-scoped replace-policy (1 Open Quote/user, ADR + Glossary "Open Quote") |
| Jailbroken prompt tries to pay | Payment capability absent from tool manifest; `/pay` rejects `scope=agent` (test-enforced) |
| Stale cache misleads agent answers | Agent must read live numbers via tools each turn (prompt rule + ledger audit: any number in reply traceable to a tool call) |
| Redis outage → rate/idempotency state lost | fail-**closed** for limiter & money writes (503 + `Retry-After`), fail-open only for catalog cache (M9 behavior kept) |
| Reservation hoarding (users never pay) | TTL 30m + sweeper + max 1 active quote per user |
| Webhook spoofing | signature verify before queue, event-id dedupe, totals checked against `PaymentSession` |
| MCP SDK drift | thin adapter layer around SDK surface, pinned version (M12 risk note kept) |
| LLM cost runaway | per-day user cap + per-conversation token budget + warn at 80% (M19) |

## 12. Definition of Done (every milestone)

Code + migration + config + docs; unit + integration green (`dotnet test`, Testcontainers for queue/cache/rate paths);
manual demo steps in PR; `docs/PLAN.md` checkboxes ticked; no secrets committed; `/health/ready` 200; Assist
invariant test suite still green (no MCP tool can move money without a human on the hosted page).
