# ShopMind — Master Plan (V1 → Production + AI)

> Single source of truth for where ShopMind is and where it goes next.
> Language: English. Status date: 2026-10-05.

## 0. How to read this document

- **V1 (M0–M8) is DONE** in this repo. Those sections are a retrospective: what was built, where it lives, and what gaps remain. Do not re-plan V1 from scratch.
- **M9–M21 is the forward plan.** Each milestone is a full spec: Goal, Baseline, Architecture, Tasks, Contracts, Rules, Acceptance Criteria, Test Plan, Risks, Deliverable, Dependencies.
- Stack for M9+ is **locked** (see §3). If you want to change it, update §3 first and propagate.
- Execution rule: implement milestones **in order**. Each milestone has an explicit exit criterion. Do not start M12 (MCP) before M9–M11 (caching, events, workers) are done.

## 1. Vision

Build ShopMind in four layers, in this exact order:

```text
┌─────────────────────────┐
│       V1 E-COMMERCE     │  M0–M8   DONE in this repo
│ Shop → Cart → Checkout  │
│      → Payment → Order  │
└────────────┬────────────┘
             ▼
┌─────────────────────────┐
│   BACKEND ENGINEERING   │  M9–M11  NEXT
│ Redis / Outbox / Queue  │
│ Workers / Reliability   │
└────────────┬────────────┘
             ▼
┌─────────────────────────┐
│      AI + MCP           │  M12–M16
│ MCP → Tools → Agent     │
│ Planning → HITL Actions │
└────────────┬────────────┘
             ▼
┌─────────────────────────┐
│   INTELLIGENT SYSTEM    │  M17–M18
│ pgvector / Hybrid       │
│ Search / Recommendation │
└────────────┬────────────┘
             ▼
┌─────────────────────────┐
│   PRODUCTION SYSTEM     │  M19–M21
│ Safety / Observability  │
│ Hardening / Deployment  │
└─────────────────────────┘
```

Demo narrative at the end:

> "I can build a proper backend first (V1), make it production-style (async, caching, workers), then expose it safely to an AI agent via MCP, then make it intelligent (semantic search, recommendations), with observability and safety throughout."

## 2. Non-negotiable principles

These apply to all milestones. Violations are bugs.

1. **Frontend never determines price.** Backend recalculates `subtotal + shipping - discount = total` from current `Product.Price` inside the checkout transaction. Implemented in `src/Shop.Application/Services/CheckoutService.cs:43-49`.
2. **Never trust client `userId`.** User identity comes only from JWT claims via `ClaimsPrincipalExtensions`. Users access only their own cart/orders; admin routes require `Admin` role.
3. **Order transitions are centralized.** Only `Shop.Domain.Rules.OrderTransitions` decides validity. No ad-hoc `if (status == ...)` in services/controllers.
4. **MCP never touches PostgreSQL directly.** MCP calls Application services only (`IAuthService`, `IProductService`, `ICartService`, etc.). No `DbContext` in `Shop.Mcp`.
5. **MCP exposes business capabilities, not raw CRUD.** Example: `find_products_for_requirement`, `prepare_checkout` — not `insert_order_row`.
6. **Sensitive writes require confirmation.** `create_order`, `cancel_order` go through Human-in-the-Loop `AiAction` (M16), never direct agent execution.
7. **Every async consumer is idempotent.** Outbox + RabbitMQ consumers dedupe by `MessageId`/`EventId`. Retries never double-apply.
8. **Don't cache everything.** Cache only proven hot read paths with explicit invalidation (M9).

Order state machine (locked, see `src/Shop.Domain/Rules/OrderTransitions.cs`):

```text
Pending → Paid → Processing → Shipped → Delivered
Pending → PaymentFailed (terminal)
Pending → Cancelled (terminal)
Paid → Cancelled (terminal)
```

## 3. Locked stack (M9+)

| Concern | Choice | Notes |
|---|---|---|
| API | ASP.NET Core (.NET 10) | Current `src/Shop.Api` |
| DB | PostgreSQL 16 + EF Core | Current; add `pgvector` extension in M17 |
| Cache | Redis via `StackExchange.Redis` | New in M9; local via Docker Compose, prod via managed Redis (e.g. Upstash) |
| Queue | RabbitMQ via `RabbitMQ.Client` | New in M11; topic exchange `shop.events` |
| Workers | `Shop.Worker` BackgroundService | New in M11; outbox publisher + event consumers |
| MCP | Official MCP .NET SDK (`Shop.Mcp` project) | New in M12; transports: Streamable HTTP/SSE; no direct DB |
| AI providers | `IAiProvider` → `GeminiProvider` + `OpenRouterProvider` | New in M14; provider selected by config |
| Embeddings | Provider embeddings → `pgvector` | New in M17; dim 768 or 1536 fixed per model, HNSW index |
| Frontend | Vanilla JS SPA in `frontend/` | Keep for V1; no framework migration in this plan |
| Tests | xUnit + Testcontainers (Postgres) | Current; extend per milestone |
| Observability | Serilog + OpenTelemetry + health checks | Hardened in M19–M20 |

Current infra (this repo): `docker-compose.yml` has only `postgres` + `api`. M9/M11/M17 extend it with `redis`, `rabbitmq`, `pgvector` image.

## 4. Repo map — current vs target

Current (verified 2026-10-05):

```text
ShopMind.slnx
src/Shop.Domain/            Entities, Enums, Rules/OrderTransitions, Exceptions, ValueObjects/ShippingAddress
src/Shop.Application/       Interfaces, Dtos, Services/*, Validation/Validators, Common/PagedResult, Persistence/IAppDbContext
src/Shop.Infrastructure/    Persistence/AppDbContext + Configurations + Migrations + Seed, Auth/Jwt+BCrypt, Payments/FakePaymentService, DependencyInjection
src/Shop.Api/               Program.cs, Controllers/*, Middleware/ExceptionHandlingMiddleware, Extensions/ClaimsPrincipalExtensions
tests/Shop.UnitTests/       CartTests, InventoryTests, OrderTransitionsTests, ValidatorTests
tests/Shop.IntegrationTests/ ShopWebFactory, IntegrationTestBase, AuthFlowTests, CartCheckoutTests
frontend/                   index.html, app.js, styles.css (vanilla SPA)
docker-compose.yml          postgres + api only
```

Target by M21 (new projects/files only — no rewrites of V1 layers):

```text
src/Shop.Domain/            + Events/*, AiAction, UserBehaviorEvent, ProductEmbedding, OutboxMessage
src/Shop.Application/       + Caching/ICacheService, Events/IEventPublisher, Agent/*, Personalization/*, Search/HybridSearch
src/Shop.Infrastructure/    + Caching/RedisCacheService, Events/OutboxPublisher, Embeddings/*, AiProviders/*
src/Shop.Api/               + Controllers/AgentController, ActionsController, SearchController, Health
src/Shop.Worker/            NEW: OutboxPublisherWorker, RabbitMq consumers (Email, Analytics, Recommendation, Embedding)
src/Shop.Mcp/               NEW: McpServer, Tools/*, Auth/McpAuthContext, Middleware
tests/                      + per-milestone integration tests (cache, outbox, workers, MCP, agent, search)
docker-compose.yml          postgres(pgvector) + redis + rabbitmq + api + worker + mcp
docker-compose.prod.yml     NEW
docs/PLAN.md                THIS FILE
```

## 5. Milestone overview

| MS | Focus | Status | Depends on |
|---|---|---|---|
| M0 | Project Foundation | ✅ Done | — |
| M1 | Domain & Database | ✅ Done | M0 |
| M2 | Auth & Authorization | ✅ Done | M1 |
| M3 | Product Catalog | ✅ Done | M1–M2 |
| M4 | Shopping Cart | ✅ Done | M2–M3 |
| M5 | Checkout + Fake Payment | ✅ Done | M4 |
| M6 | Order Management | ✅ Done | M5 |
| M7 | Admin System | ✅ Done | M2–M6 |
| M8 | Testing & API Quality | ✅ Done (partial*) | M0–M7 |
| M9 | Redis & Caching | ⬜ Next | M8 |
| M10 | Events + Outbox | ⬜ | M9 |
| M11 | Queue + Workers (RabbitMQ) | ⬜ | M10 |
| M12 | MCP Foundation | ⬜ | M11 |
| M13 | MCP Business Tools | ⬜ | M12 |
| M14 | AI Provider abstraction | ⬜ | M12 |
| M15 | AI Agent (ReAct loop) | ⬜ | M13–M14 |
| M16 | Human-in-the-Loop | ⬜ | M15 |
| M17 | pgvector Semantic + Hybrid Search | ⬜ | M11, M15 |
| M18 | Personalization & Recommendations | ⬜ | M11, M17 |
| M19 | AI Observability & Safety | ⬜ | M15–M16 |
| M20 | Production Hardening + Observability | ⬜ | M9–M11, M19 |
| M21 | Deployment | ⬜ | M20 |

`*M8 partial`: unit tests pass; integration tests require Docker/Testcontainers and are not yet run in CI. See M8 gaps.

---

# PART A — V1 RETROSPECTIVE (M0–M8) — DONE

> Purpose: record what exists so M9+ plans ground correctly. Each item cites the actual file. Checkboxes are `[x]` = verified in code.

## M0 — Project Foundation — ✅ Done

Goal: runnable layered solution with DI, config, Swagger, Postgres, Docker.

- [x] Solution `ShopMind.slnx` with `src/*`, `tests/*`
- [x] Projects: `Shop.Api`, `Shop.Application`, `Shop.Domain`, `Shop.Infrastructure`, `Shop.UnitTests`, `Shop.IntegrationTests`
- [x] DI: `src/Shop.Infrastructure/DependencyInjection.cs` + `src/Shop.Api/Program.cs:17`
- [x] Env config: `ConnectionStrings:Default`, `Jwt:*`, `ApplyMigrations` in `Program.cs:41-45,85`
- [x] Swagger/OpenAPI with JWT bearer in `Program.cs:20-39`
- [x] Global exception middleware `src/Shop.Api/Middleware/ExceptionHandlingMiddleware.cs`
- [x] CORS default policy `Program.cs:66-67` (currently `AllowAnyOrigin` — must lockdown in M20)
- [x] `docker-compose.yml`: `postgres:16-alpine` + `api` build; healthcheck; auto-migrate+seed with retry `Program.cs:84-107`
- [x] `.gitignore` exists but minimal (`bin/ obj/ .vs/ TestResults/`) — extend in M20/M21
- [x] `README.md` quickstart (local + Docker), seeded users, API surface

Gaps carried to M20/M21: CORS lockdown, `.gitignore` (add `appsettings.Development.json`, `.env`, `*.db`), CI workflow, `Dockerfile` for worker/MCP, prod compose.

Deliverable met: `dotnet run --project src/Shop.Api` starts, migrates, seeds, serves `/swagger`.

## M1 — Domain & Database — ✅ Done

Entities (`src/Shop.Domain/Entities/`): `User`, `Category`, `Product`, `ProductImage`, `Inventory`, `Cart`, `CartItem`, `Order`, `OrderItem`, `Payment`.

- [x] Enums `Enums.cs`: `UserRole{Customer,Admin}`, `ProductStatus{Draft,Active,Inactive}`, `OrderStatus{7 states}`, `PaymentStatus{Pending,Succeeded,Failed}`
- [x] Relationships: User→Cart/Orders; Category→Products; Product→Images/Inventory/OrderItems; Cart→CartItems; Order→OrderItems+Payment
- [x] `ValueObjects/ShippingAddress.cs`, `Exceptions/ShopExceptions.cs`, `Rules/OrderTransitions.cs`
- [x] EF config `Infrastructure/Persistence/Configurations/EntityConfigurations.cs`: indexes, unique (`Email`, `SKU`, `Slug`), FKs, `decimal(18,2)` money precision
- [x] Migration `Migrations/20261001071900_InitialCreate.cs` + snapshot; `AppDbContext.cs` (10 `DbSet`s)
- [x] Seed `Seed/SeedData.cs`: `admin@shopmind.local/Admin123!`, `customer@shopmind.local/Customer123!` + electronics catalog

Deliverable met: working Postgres schema for the domain.

## M2 — Auth & Authorization — ✅ Done

- [x] `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me` in `Controllers/AuthController.cs`
- [x] BCrypt hashing `Infrastructure/Auth/BCryptPasswordHasher.cs`, JWT `JwtTokenGenerator.cs`/`JwtOptions.cs`, validation `JwtBearer` in `Program.cs:46-62`
- [x] Validators `Application/Validation/Validators.cs` (email uniqueness, password rules)
- [x] `[Authorize]` + admin policy; `ClaimsPrincipalExtensions` for server-side `userId`; no client `userId` trusted
- [x] Tests: `tests/Shop.IntegrationTests/AuthFlowTests.cs` (register/login/me, uniqueness, unauthorized)

Security debt → M20: short-lived JWT + refresh, password policy hardening, rate-limit login, secrets via env/vault (current dev fallback secret in `Program.cs:42`).

## M3 — Product Catalog — ✅ Done

- [x] `GET /api/products?search&category&brand&minPrice&maxPrice&sort&page&pageSize` + `GET /api/products/{id}` + `GET /api/categories*` in `Controllers/CatalogControllers.cs`
- [x] `ProductService.QueryAsync` + `ProductQueryParams` (`Dtos.cs:43-53`): search, category (slug|id), brand, price band, `InStockOnly`, sort (`price_asc/desc,newest,name_*`), pagination via `PagedResult`
- [x] `CategoryService`, `Mapping.cs`, images + `AvailableStock` in response DTOs
- [x] Inactive products hidden from public (`includeInactive=false` default in `Interfaces.cs:28`)

Deliverable met: customer can list/search/filter/sort/paginate electronics.

## M4 — Shopping Cart — ✅ Done

- [x] `GET /api/cart`, `POST /api/cart/items`, `PATCH /api/cart/items/{id}`, `DELETE /api/cart/items/{id}`, `DELETE /api/cart` in `Controllers/ShopControllers.cs`
- [x] `CartService` (`Application/Services/CartService.cs`): one `CartItem` per product, `quantity>0`, product exists+Active, stock check, subtotal calc
- [x] Tests: `tests/Shop.UnitTests/CartTests.cs` (add, add-twice merges, increase/decrease, remove, insufficient stock, inactive, invalid qty)

## M5 — Checkout & Fake Payment — ✅ Done (most critical V1 flow)

Flow in `Services/CheckoutService.cs`:

```text
Cart → validate items (Active, stock, qty) → recalc prices → Order+OrderItems snapshot
→ persist Order(Pending) → FakePayment → Payment row → Paid+decrement stock+clear cart (commit)
→ on failure: PaymentFailed + commit + throw (no stock change, cart kept)
```

- [x] `POST /api/checkout` with `CheckoutRequest(ShippingAddress, PaymentMethod=fake, SimulateFailure, SimulateTimeout)` (`Dtos.cs:84`)
- [x] Single DB transaction `BeginTransactionAsync` (`CheckoutService.cs:23`); order snapshot (`ProductName/SKU/UnitPrice`); shipping `Free>=500 else 15` (`:13-14,47-49`)
- [x] `IFakePaymentService` (`Interfaces.cs:79-82`) → `Infrastructure/Payments/FakePaymentService.cs`; `IPaymentProvider` split deferred to post-V1 (Stripe lives in M20 backlog, not required for agent)
- [x] Tests: `tests/Shop.IntegrationTests/CartCheckoutTests.cs` (success, failure, insufficient stock)

Rule preserved: frontend never prices; `item.UnitPrice = product.Price` server-side (`:44`).

## M6 — Order Management — ✅ Done

- [x] Customer: `GET /api/orders`, `GET /api/orders/{id}`, `POST /api/orders/{id}/cancel` — ownership enforced (`OrderService.ListMine/GetMineById/CancelMine`)
- [x] Transitions via `OrderTransitions.IsValidTransition` only; illegal `Delivered→Pending` etc. impossible by construction
- [x] Tests: `tests/Shop.UnitTests/OrderTransitionsTests.cs` + integration access-control cases

## M7 — Admin System — ✅ Done

In `Controllers/AdminControllers.cs` + `Services/*`:

- [x] Products `GET/POST/PUT/DELETE /api/admin/products`, Categories `.../api/admin/categories`, Inventory `GET/PATCH /api/admin/inventory`, Orders `GET/GET{id}/PATCH status /api/admin/orders`
- [x] Admin dashboard v1 = these CRUD endpoints (revenue/bestsellers/low-stock deferred to M18/M20 analytics)

Deliverable met: store operable without direct DB access.

## M8 — Testing & API Quality — ✅ Done (partial — see gaps)

- [x] Unit: `CartTests`, `InventoryTests`, `OrderTransitionsTests`, `ValidatorTests` — `dotnet test tests/Shop.UnitTests` green
- [x] Integration: `AuthFlowTests`, `CartCheckoutTests` via `ShopWebFactory` (Testcontainers Postgres) — requires Docker; not runnable without it
- [x] API quality: global errors `{title,status,errors}`, FluentValidation, consistent status codes, Swagger, pagination (`PagedResult`), logging via `ILogger`
- [ ] GAPS → must close before M9: (1) run integration suite once with Docker and record result; (2) add negative cases already listed but verify: order access-control cross-user, admin 403 for customer, checkout empty-cart; (3) add CI (`dotnet test` unit always; integration on label); (4) decide code coverage gate (suggest 70% Application services for V1).

**V1 exit declaration:** V1 is complete for demo purposes once M8 gaps (1)–(2) are closed. M9+ must not start on a red suite.

```text
frontend → ASP.NET Core (Auth/Products/Cart/Checkout/Orders/Admin) → PostgreSQL
```

---

# PART B — FORWARD PLAN (M9–M21)

> Conventions per milestone: Goal · Baseline · Architecture · Tasks · Contracts · Rules · Acceptance Criteria · Test Plan · Risks · Deliverable/Exit · Dependencies.

## M9 — Redis & Caching — ⬜ NEXT (do first after V1)

**Goal.** Cut hot-read latency and DB load for catalog reads without introducing stale-write bugs.

**Baseline.** No cache. Every `GET /api/products*` and `GET /api/categories*` hits Postgres. Admin writes go straight to DB.

**Architecture.**

```text
GET → ICacheService.GetAsync(key)
  ├── Hit → return
  └── Miss → Postgres → SetAsync(key, value, TTL) → return
PUT/POST/DELETE (admin) → DB write → RemoveAsync/RemoveByPrefixAsync
```

**Tasks.**

- [ ] Add `redis` service to `docker-compose.yml` (image `redis:7-alpine`, port 6379, volume, healthcheck).
- [ ] Add `StackExchange.Redis` + `ICacheService` in `Shop.Application/Caching/`:
  `GetAsync<T>`, `SetAsync<T>(ttl)`, `RemoveAsync`, `RemoveByPrefixAsync`, `GetOrCreateAsync` with stampede guard (short lock or single-flight).
- [ ] Implement `RedisCacheService` in `Shop.Infrastructure/Caching/` (JSON serialization, key prefix `shop:{env}:`, resilient: cache failure never fails request — log + fall through to DB).
- [ ] Cache: `ListPublic categories` (TTL 10 min), `GetProductById` (TTL 5 min), `Query products` (TTL 1–2 min, key includes normalized query hash, cap `pageSize<=100`).
- [ ] Invalidation: product create/update/delete → evict `product:{id}` + `products:query:*`; category write → evict `categories:all`; inventory quantity change → evict `product:{id}`.
- [ ] Config: `Redis:ConnectionString`, `Redis:Enabled` (kill-switch), `Redis:DefaultTtlSeconds`; document in README + `appsettings.json`.
- [ ] Admin bypass header/query (`?nocache=1`, Admin only) for debugging.

**Contracts.**

- Keys: `shop:product:{id}`, `shop:products:q:{sha1(search|category|brand|min|max|sort|page|size)}`, `shop:categories:all`.
- No API shape changes. Add response header `X-Cache: HIT|MISS|BYPASSED` on cached endpoints.

**Rules.**

- Never cache: cart, checkout, orders, auth/me, admin order list. Only cache public catalog reads.
- TTLs short; invalidation on write is mandatory, not optional.

**Acceptance Criteria.**

- [ ] Cold `GET /api/products/{id}` = MISS, repeat = HIT (`X-Cache`).
- [ ] Admin `PUT /api/admin/products/{id}` then `GET` reflects new price within 5s (invalidation works).
- [ ] Redis down → API still 200 (fallback), error logged once per minute (no log spam).
- [ ] p95 catalog latency down vs pre-cache baseline (measure 200 req locally, record in PR).

**Test Plan.**

- Unit: key builder, TTL selection, invalidation matrix.
- Integration: Testcontainers Redis or fake `ICacheService`: miss→hit, write→evict, Redis-down fallback.

**Risks.** Stale price/stock shown briefly → mitigate with short TTL + evict on inventory change; document that checkout always revalidates live stock (M5) so stale cache can never oversell.

**Deliverable / Exit.** Cached catalog with invalidation + fallback; README documents when caching helps and when it does not.

**Dependencies.** Requires green M8. Blocks M10–M11 (event payloads assume cache keys exist for eviction hooks).

## M10 — Event-Driven Architecture (Domain Events + Outbox) — ⬜

**Goal.** Decouple side effects (email, analytics, recommendations, embeddings) from request path with reliable events. No direct RabbitMQ calls from controllers/services yet — only Outbox writes.

**Baseline.** Checkout/order/product changes commit business rows only; no event record.

**Architecture.**

```text
Application service (same DB transaction)
  ├── Business tables (Orders, Products, ...)
  └── OutboxMessages (new row, same commit)
OutboxPublisherWorker (M11) → RabbitMQ (M11)
```

**Tasks.**

- [ ] New domain: `IDomainEvent`, events `ProductCreated/Updated`, `OrderCreated/Paid/Cancelled/Shipped/Delivered`, `PaymentFailed`.
- [ ] New table `OutboxMessages`: `Id uuid PK, AggregateType text, AggregateId uuid, Type text, Payload jsonb, OccurredAt timestamptz, PublishedAt timestamptz NULL, Attempts int DEFAULT 0, Status smallint (Pending/Sent/Failed)`. Index `(Status, OccurredAt)`.
- [ ] EF migration + `IEventPublisher.EnqueueAsync` (writes row, does not publish). Wire into: checkout (OrderCreated→OrderPaid or PaymentFailed), `OrderService.CancelMine/UpdateStatusAdmin`, product/category admin writes.
- [ ] Payload contract v1: `{ eventId, eventType, occurredAt, aggregateType, aggregateId, userId?, data:{...minimal IDs + snapshot fields}, schemaVersion:1 }`. Never put secrets/PII beyond IDs + order totals.
- [ ] Add `CorrelationId` (per request, via middleware header `X-Correlation-Id`) propagated into payload.

**Contracts (event catalog v1 — locked).**

```text
order.created      { orderId, userId, totalAmount, itemCount }
order.paid         { orderId, userId, totalAmount, transactionId }
order.cancelled    { orderId, userId, reason? }
order.shipped|delivered { orderId, userId }
payment.failed     { orderId, userId, reason }
product.created|updated { productId, sku, price, status }
```

**Rules.** Outbox write and business write share one transaction; publish happens only via worker (M11). No `PublishAsync` inside HTTP request.

**Acceptance Criteria.**

- [ ] `POST /api/checkout` success creates business rows + exactly one `order.created` + one `order.paid` outbox row (assert in DB).
- [ ] Payment failure creates `payment.failed`, no stock change (existing M5 guarantee preserved).
- [ ] All payloads validate against a JSON schema test; `schemaVersion` present.

**Test Plan.** Unit: event emission matrix per service. Integration: checkout/cancel/status-change assert outbox rows; rollback test (forced failure → no orphan outbox row).

**Risks.** Dual-write temptation → forbid; outbox table growth → M11 publisher + retention policy (archive after 30 days).

**Deliverable / Exit.** Every state-changing V1 flow emits versioned outbox events atomically.

## M11 — Outbox Publisher + RabbitMQ + Workers — ⬜

**Goal.** Reliably deliver M10 events to background consumers with retry, DLQ, and idempotency.

**Baseline.** Outbox rows accumulate; nothing publishes.

**Architecture.**

```text
PostgreSQL Outbox ──OutboxPublisherWorker (poll 2s, batch 50, FOR UPDATE SKIP LOCKED)──▶ RabbitMQ topic shop.events
  routing keys: order.created, order.paid, ... ──┬──▶ EmailWorker (stub log) 
                                                  ├──▶ AnalyticsWorker (UserBehaviorEvent append)
                                                  ├──▶ RecommendationWorker (M18 hook, stub now)
                                                  └──▶ EmbeddingWorker (M17 hook, stub now)
Failures → retry exp backoff (3x) → shop.events.dlq + OutboxMessages.Status=Failed
```

**Tasks.**

- [ ] `docker-compose.yml`: `rabbitmq:3-management` (ports 5672/15672, volumes). Connection `RabbitMq:Host/User/Pass`.
- [ ] New project `src/Shop.Worker/`: `OutboxPublisherWorker`, `RabbitMqConnection`, consumers as `BackgroundService`. Graceful shutdown (`CancellationToken`, `IHostApplicationLifetime`).
- [ ] Publisher: claim batch (`UPDATE ... WHERE Id IN (...) RETURNING`), publish with `messageId=eventId`, confirm, mark `Sent/PublishedAt`; on broker down → backoff, keep `Pending`, alert log.
- [ ] Consumers: manual ack, prefetch 10, per-message `try/catch`; idempotency via `ProcessedEvents(EventId PK, Consumer, ProcessedAt)` — skip duplicates.
- [ ] DLQ: `shop.events.dlq` + headers `x-death`; admin log endpoint or worker log for DLQ depth ( postcode for M20 metrics).
- [ ] Email worker v1 = structured log (`[Email] order.paid → user {id}`); real SMTP deferred.
- [ ] Analytics worker v1 appends `UserBehaviorEvents` (see M18 schema) for `order.*` and `product.viewed` (viewed emitted from API later in M18).
- [ ] Retention: daily cleanup job deletes `Sent` older than 30 days (configurable).

**Contracts.** Exchange `shop.events` (topic, durable); queues `shop.email`, `shop.analytics`, `shop.recommendation`, `shop.embedding` (durable, quorum if available); routing keys = event types with dots.

**Rules.** Consumers never throw to crash host; poison messages → DLQ after 3 attempts; business DB writes in consumers use new transaction + dedupe check first.

**Acceptance Criteria.**

- [ ] Kill RabbitMQ mid-checkout → order still 200/committed, outbox `Pending`; restart → event delivered within 10s.
- [ ] Publish same `eventId` twice → consumer processes once (assert `ProcessedEvents`).
- [ ] Poison payload → 3 retries → DLQ, worker stays healthy (`/health` 200).

**Test Plan.** Integration: Testcontainers Postgres+RabbitMQ: publish→consume, retry→DLQ, duplicate-suppression, publisher crash-recovery (restart worker, no loss/dupe).

**Risks.** Message ordering (only per-aggregate ordering guaranteed; consumers must be order-tolerant). Clock skew → use DB `OccurredAt`.

**Deliverable / Exit.** Reliable async pipeline: HTTP never blocks on side effects; demo `checkout → RabbitMQ management UI shows routing → worker logs`.

## M12 — MCP Foundation — ⬜

**Goal.** Expose ShopMind to AI through a standard MCP server with auth, validation, logging, and zero direct DB access.

**Baseline.** No MCP project; AI cannot call the backend.

**Architecture.**

```text
AI (agent) → MCP Client → Shop.Mcp (STDIO + Streamable HTTP)
  ├── McpAuthContext (JWT → UserId/Role/CorrelationId)
  ├── Tool router → validation → Application service → Domain
  ├── Structured errors + audit log
  └── NEVER → PostgreSQL / DbContext
```

**Tasks.**

- [ ] New project `src/Shop.Mcp/` on official MCP .NET SDK. Transports: STDIO (local dev) + Streamable HTTP (`/mcp`, JWT bearer) for hosted agent.
- [ ] `McpToolContext`: `UserId, Role, CorrelationId, IdempotencyKey?`; built from JWT in HTTP mode, from env/test principal in STDIO dev mode. Reject anonymous for all cart/order/tools except pure public catalog search (decide: public search allowed anonymous, everything else requires JWT — document).
- [ ] Guard layer per tool call: authz check (role + ownership) → FluentValidation of args → service call → map exceptions to MCP error codes (`INVALID_ARGS, UNAUTHORIZED, FORBIDDEN, NOT_FOUND, CONFLICT, UPSTREAM_ERROR`) → structured log (`tool, userIdHash, durationMs, status`).
- [ ] Tool manifest + JSON schemas auto-generated from DTOs; `docs/mcp-tools.md` generated (or section in PLAN appendix — keep in code as `README` of `Shop.Mcp`).
- [ ] Local dev script: `dotnet run --project src/Shop.Mcp -- --transport stdio` + Inspector test.
- [ ] Add `mcp` service to compose (shares API config; no DB connection string needed beyond what Application services use — Mcp hosts services via same DI, not raw SQL).

**Contracts.** Error envelope: `{ code, message, correlationId, retryable:bool }`. All tool args validated; unknown fields rejected (strict).

**Rules.** §2 principles #4–#5 enforced by code review checklist + an architecture test (no `DbContext`/`Npgsql` reference in `Shop.Mcp.csproj` — assert via test scanning assemblies).

**Acceptance Criteria.**

- [ ] MCP Inspector lists tools, calls `search_products` anonymously OK, calls `get_cart` without JWT → `UNAUTHORIZED`.
- [ ] `add_to_cart` as user A cannot touch user B cart (ownership test via two JWTs).
- [ ] Architecture test fails if `Shop.Mcp` references EF/Npgsql.

**Test Plan.** Unit: authz matrix per tool. Integration: boot MCP + API services in-memory, drive tools via MCP client, assert same results as REST.

**Risks.** SDK version drift → pin version, wrap SDK in thin adapter. Token leakage in logs → log `userIdHash` only, never JWT.

**Deliverable / Exit.** Running MCP server exposing at least `search_products`, `get_product`, `get_cart` behind auth with validated schemas.

## M13 — MCP Business Tools — ⬜

**Goal.** Full business-capability toolset so an agent can shop like a human: discover → compare → cart → orders → checkout prep.

**Baseline.** M12 has 3 tools.

**Tasks (read tools first, then write tools).**

Read:

- [ ] `search_products(query, category?, brand?, minPrice?, maxPrice?, sort?, page?, pageSize?)` → same semantics as `GET /api/products` (reuse `ProductService`).
- [ ] `get_product(productId)` → detail + `AvailableStock`.
- [ ] `compare_products(productIds[2..4])` → side-by-side table (price, brand, category, stock, key attrs from description).
- [ ] `get_product_inventory(productId)` → `available` only (no internal `reserved` leak unless Admin).
- [ ] `get_orders`, `get_order(orderId)`, `get_order_status(orderId)` → ownership enforced.

Write (all user-scoped):

- [ ] `get_cart`, `add_to_cart(productId, qty)`, `update_cart(itemId, qty)`, `remove_from_cart(itemId)` → reuse `CartService` rules verbatim.
- [ ] Higher-level: `find_products_for_requirement(requirementText, budget?, categoryHint?)` → structured-filter extraction (price cap, category keywords) + `search_products` + rank by price/stock match with explanation. (No LLM here yet — keyword heuristics; LLM ranking comes in M15.)
- [ ] `get_personalized_recommendations(limit?)` → stub in this milestone (returns bestsellers/newest); real engine in M18 behind same signature.
- [ ] `prepare_checkout` → validates cart, returns price breakdown + `AiAction` draft (does NOT create order; hands to M16 confirmation).

**Contracts.** Publish JSON schemas + examples for every tool; `pageSize<=50` default 20; money as decimal strings with 2dp; IDs as UUID strings.

**Rules.** Tool classification assigned now (enforced in M16): `search/compare/get_*` = READ; `add/update/remove_cart` = WRITE; `cancel_order` = SENSITIVE_WRITE; `create_order` = CRITICAL_WRITE (M16 only via confirmed action).

**Acceptance Criteria.**

- [ ] Scripted scenario via MCP only: search laptop → get 2 details → compare → add best to cart → get cart subtotal matches REST.
- [ ] Cross-user isolation holds for all new tools.
- [ ] Invalid qty / inactive product / insufficient stock return typed errors, never exceptions leaking internals.

**Test Plan.** Tool-level integration matrix mirroring M4/M6 tests but driven through MCP client (add-twice merges, over-stock rejected, cancel eligibility).

**Deliverable / Exit.** Agent-capable toolset; `prepare_checkout` returns a human-readable summary without side effects.

## M14 — AI Provider Abstraction — ⬜

**Goal.** Swap Gemini ↔ OpenRouter without touching agent logic.

**Baseline.** No AI calls.

**Tasks.**

- [ ] `IAiProvider` in `Shop.Application/Ai/`:
  ```csharp
  Task<AiResponse> GenerateAsync(AiRequest req, CancellationToken ct);
  // AiRequest{Messages[Role,Content], Tools[Name,Schema], Temperature, MaxTokens, StopWhen?}
  // AiResponse{Text?, ToolCalls[{Name, ArgumentsJson, CallId}], Usage{InputTokens,OutputTokens}, Model, DurationMs}
  ```
- [ ] `GeminiProvider` (Google Generative Language REST) + `OpenRouterProvider` (OpenAI-compatible `/chat/completions` with `tools`). Config `Ai:Provider (Gemini|OpenRouter)`, `Ai:Model`, `Ai:ApiKey` (env only, never appsettings commit), `Ai:BaseUrl?`, timeout 20s, retry 2x on 429/5xx with jitter.
- [ ] Function-calling adapter: normalize both providers to internal `ToolCalls[]`; argument JSON validated against MCP tool schemas before dispatch.
- [ ] Token/cost guard: `MaxTokens` default 1024, truncate history (keep system + last 10), strip PII (emails, addresses) from prompts unless needed for checkout summary (then only last-4/totals).
- [ ] Dev stub `FakeAiProvider` (deterministic scripted responses) for tests without API keys.

**Acceptance Criteria.**

- [ ] Switch `Ai:Provider` + `Ai:Model` only → same `AiRequest` yields normalized `AiResponse` from both backends (record two cassettes in test).
- [ ] No API key in repo/logs; missing key → clear `AI_NOT_CONFIGURED` error, API still serves non-AI routes.
- [ ] 429 from provider → retried then surfaced as `retryable:true`.

**Test Plan.** Unit with `HttpMessageHandler` mocks per provider (tool-call parsing, usage parsing, error mapping). Integration (manual, keyed): one prompt through each provider, snapshot normalized shape.

**Deliverable / Exit.** Agent code (M15) depends only on `IAiProvider`.

## M15 — AI Agent (ReAct loop) — ⬜

**Goal.** Turn prompts into multi-step tool plans: understand → search → compare → act → summarize.

**Baseline.** Tools + provider exist but unconnected.

**Architecture.**

```text
POST /api/agent/chat { message, conversationId? }
→ AgentLoop (system prompt + history + tools)
  → IAiProvider.Generate → ToolCall? → authorize+validate → Shop.Mcp tool → append observation
  → repeat until final answer or limits
→ { replyText, toolCallsSummary, conversationId, pendingAction? }
```

**Tasks.**

- [ ] New `Shop.Application/Agent/AgentLoop` (or `src/Shop.Agent/` if it grows): system prompt (shop assistant, electronics, budget-aware, never invent price/stock — always call tools), history window, tool registry from M13.
- [ ] Loop policy: `MaxToolCalls=10`, `MaxDuration=30s`, `MaxRetriesPerTool=2`, `CancellationToken` honored; temperature 0.2 for shopping tasks.
- [ ] Persistence (needed for M16/M19): `AiConversations`, `AiMessages` tables now (migration here, audit fields in M19).
- [ ] Endpoint `POST /api/agent/chat` (JWT; anonymous allowed only for pure search intents — enforce: if plan needs cart/order tools, require JWT, else return login nudge).
- [ ] Worked examples as tests, not just docs:
  - "laptop for backend dev under $1200" → `search_products` → `get_product`×2 → `compare_products` → answer with prices + links/ids.
  - "add the best one to my cart" → previous + `add_to_cart` + cart summary.
- [ ] `FakeAiProvider` scripted for deterministic CI; live-provider tests marked `[SkipUnlessKey]`.

**Rules.** Agent never quotes price/stock from its weights — every number must come from a tool observation in the same run (assert in tests by checking tool-call trail).

**Acceptance Criteria.**

- [ ] Under-$1200 laptop prompt completes ≤10 tool calls with ≥1 `search_products` + ≥1 `get_product`/`compare` in trail and final prices matching tool outputs.
- [ ] "Add best to cart" ends with cart containing the compared winner; reply includes subtotal.
- [ ] Timeout (`MaxDuration` exceeded) returns partial answer + ` atmospheres` no half-written cart (tools already atomic; loop just stops).

**Test Plan.** Golden-path tests on `FakeAiProvider`; chaos tests (tool fails once → retried; tool always fails → graceful error); history-truncation test.

**Deliverable / Exit.** `POST /api/agent/chat` demonstrably shops via tools.

## M16 — Human-in-the-Loop (confirm sensitive writes) — ⬜

**Goal.** Never let the agent spend money or destroy state without explicit user confirmation.

**Baseline.** M15 can `add_to_cart` freely; no order creation via agent yet.

**Tasks.**

- [ ] Table `AiActions`: `Id, UserId, Type (PrepareCheckout|CreateOrder|CancelOrder), PayloadJson, Status (Pending/Confirmed/Rejected/Expired/Executed/Failed), IdempotencyKey unique, ExpiresAt, DecidedAt?, ExecutedAt?, Error?`. Index `(UserId, Status)`.
- [ ] Flow: `prepare_checkout` (M13) → creates `AiAction(Pending, 15-min expiry)` + human summary (`items, subtotal, shipping, total, address?`) → `POST /api/agent/actions/{id}/confirm` or `/reject` → on confirm: execute `CheckoutService.CheckoutAsync` once (idempotency key = action Id) → `Executed`; on expiry/reject → terminal.
- [ ] Risk gating: READ auto-allowed; WRITE (`add_to_cart`) allowed but logged + rate-limited; SENSITIVE_WRITE (`cancel_order`) + CRITICAL_WRITE (`create_order`) always require confirmed, unexpired action. Admin tools never exposed to agent.
- [ ] Endpoints: `GET /api/agent/actions/pending`, `POST .../{id}/confirm`, `POST .../{id}/reject`; confirm re-authenticates JWT + revalidates stock/price live (prices may have changed since prepare — show delta and require re-confirm if total changed >0).
- [ ] Frontend minimal: render pending-action card with Confirm/Cancel buttons (`frontend/app.js` small addition).

**Acceptance Criteria.**

- [ ] "Buy the laptop" → agent returns summary + `pendingActionId`, no order created; `GET /api/orders` unchanged.
- [ ] Confirm within TTL → exactly one order (double-confirm → second returns same order, no dupe — idempotency).
- [ ] Expired/rejected action → confirm returns `410 Gone`/`409 Conflict`; no order.
- [ ] Total changed between prepare and confirm → `409 TotalChanged` with new summary, requires fresh confirm.

**Test Plan.** Lifecycle tests (prepare→confirm→executed; confirm-twice; reject; expire via clock skew; total-changed race by editing product price mid-flow).

**Deliverable / Exit.** Auditable, idempotent confirmation for money-moving actions.

## M17 — pgvector Semantic + Hybrid Search — ⬜

**Goal.** Understand meaning ("good for coding + Docker") not just keywords ("laptop"), combined with filters.

**Baseline.** Keyword `ILIKE` search only (`ProductService`).

**Architecture.**

```text
Product write → outbox product.* → EmbeddingWorker → IAiProvider.Embed → pgvector
Query: filters (category/brand/price) + keyword score + vector cosine → fused rank → paged
```

**Tasks.**

- [ ] Postgres → `pgvector/pgvector:pg16` image; `CREATE EXTENSION vector;` migration. Table `ProductEmbeddings(ProductId PK/FK, Embedding vector(1536|768), Model text, UpdatedAt)` + HNSW index (`vector_cosine_ops`). Record chosen dim + model in migration comment + PLAN (immutable once data exists — changing dim requires backfill migration).
- [ ] `IEmbeddingProvider` (via AI provider embeddings API) + `EmbeddingWorker` (M11 consumer): generate on `product.created/updated` (text = `name + brand + category + description`), upsert embedding, delete on product delete.
- [ ] Hybrid query: `search_products` gains `semanticQuery?` + `hybrid(pin?)`; SQL: prefilter by structured filters → `ORDER BY (0.5*ts_score + 0.5*(1-cosine_distance))` or RRF fusion (document choice); keep pagination stable (keyset or offset with deterministic tiebreak `ProductId`).
- [ ] MCP: `semantic_search_products(query, filters)` tool; REST: `GET /api/products/hybrid?q=&...` (or extend existing with `mode=hybrid` — prefer new endpoint to avoid breaking V1 contract).
- [ ] Backfill CLI/worker job: `dotnet run --project src/Shop.Worker -- backfill-embeddings` for existing catalog; progress logged.
- [ ] Cache: hybrid results cached separately (`shop:search:hybrid:{hash}`, TTL 60s) — vector freshness matters less than price; price still live from Products join.

**Acceptance Criteria.**

- [ ] "good for coding and running Docker" returns dev-capable laptops (16GB+ RAM / i7/Ryzen7 class) top-3, while keyword "Docker" alone does not.
- [ ] `minPrice/maxPrice/category` filters still strictly applied in hybrid mode (no out-of-budget leaks).
- [ ] Backfill completes for seed catalog; missing embedding → graceful keyword fallback (no 500).

**Test Plan.** Golden query set (10 semantic queries with expected product families); filter-respect tests; fallback test (embedding null); latency budget (p95 <400ms local top-20).

**Risks.** Embedding cost/latency → batch + queue; dim/model lock-in → version `Model` column, support re-embed job.

**Deliverable / Exit.** Hybrid search live in REST + MCP with documented fusion + backfill story.

## M18 — Personalization & Recommendations — ⬜

**Goal.** "What laptop for me?" answers from behavior, not just catalog.

**Baseline.** `get_personalized_recommendations` stub (M13); analytics events partially collected (M11).

**Tasks.**

- [ ] Table `UserBehaviorEvents`: `Id, UserId NULL(anon w/ sessionId), Type (view/search/add_to_cart/remove_from_cart/checkout_started/order_created/order_cancelled), ProductId NULL, CategoryId NULL, QueryText NULL, MetadataJson, CreatedAt`. Index `(UserId, CreatedAt)`, `(ProductId, CreatedAt)`. Retention 12 months (job).
- [ ] Collectors: API middleware/filter logs `view` (product detail), `search` (query text + filters); cart/order services emit cart/order event types (via outbox → analytics worker append — reuse M11, no new sync writes).
- [ ] Engine v1 (rule-based, ships): candidates = category affinity (top-2 viewed/purchased categories 30d) + price-band affinity (±20% of median viewed) + co-purchase ("bought together" from `OrderItems` pairs) + in-stock + de-dupe owned/recently-cancelled; score = weighted sum, explainable (`because you viewed X`).
- [ ] Engine v2 (AI re-rank, behind flag `Recommendations:UseAiRerank`): top-20 candidates → `IAiProvider` rank with requirement text → final top-5 with reasons. Time-boxed 3s, fallback to v1 on timeout.
- [ ] Surfaces: `GET /api/recommendations/me?limit=` + MCP `get_personalized_recommendations` (real now) + frontend "Recommended for you" slot (minimal).
- [ ] Cold start: anonymous/new users → bestsellers (by `OrderItems` count 30d) + newest Active.

**Acceptance Criteria.**

- [ ] Seeded behavior (view 3 ThinkPad-class + buy 1) → recommendations skew to same category/brand family with at least one explanation string.
- [ ] New user → bestsellers, no 500, no PII leak (other users' emails never surface).
- [ ] AI re-rank off → deterministic v1 order (snapshot test); on + timeout → graceful v1 fallback.

**Test Plan.** Engine unit (affinity, co-purchase, cold-start, cancelled-exclusion); integration (emit behavior → recommendations shift); privacy test (user A behavior never influences user B snapshot beyond global bestsellers).

**Deliverable / Exit.** Personalized slot + tool with explanations and cold-start story.

## M19 — AI Observability & Safety — ⬜

**Goal.** Every agent action traceable, bounded, and rate-limited; abuse contained.

**Baseline.** Conversations/messages persisted (M15), actions persisted (M16), but no tool-call ledger, limits unenforced, no audit UX.

**Tasks.**

- [ ] Table `AiToolCalls`: `Id, ConversationId FK, ToolName, ArgumentsJson, ResultSummary (truncated 2KB), Status (Ok/Error/Denied), DurationMs, TokensIn/Out NULL, CreatedAt`. Index `(ConversationId, CreatedAt)`. Never store full PII payloads — addresses truncated/masked.
- [ ] Agent limits enforced in code (config `Agent:MaxToolCalls=10, MaxExecutionSeconds=30, MaxRetries=3`): exceed → stop + user-facing partial answer + `limit_reached` flag.
- [ ] Rate limiting: `POST /api/agent/chat` 20/min per user, `prepare/confirm` 10/min; `429` with `Retry-After`; admin separate higher bucket. Use `Microsoft.AspNetCore.RateLimiting`.
- [ ] Input validation: max message 2000 chars, blocklist (jailbreak/system-override patterns → `DENIED` + log), tool-arg schema validation already in M12 — add semantic checks (qty ≤ stock, price caps).
- [ ] Audit: `GET /api/admin/ai/calls?conversationId=&tool=&status=` (Admin) + `GET /api/agent/conversations/mine` (Customer) — paged, masked args.
- [ ] Kill-switches: `Ai:Enabled`, `Agent:Enabled`, per-tool `Tools:{name}:Enabled`; flipping requires no redeploy (config reload).
- [ ] Token accounting: per-conversation + per-day-per-user totals from `AiResponse.Usage`; log warn at 80% of daily budget.

**Acceptance Criteria.**

- [ ] Full trace for demo run viewable: conversation → messages → tool calls with durations; all within 30s/10-call budget.
- [ ] 21st chat request in a minute → `429`; flood of `add_to_cart` via agent → throttled, cart correct (no dupes).
- [ ] Disabled tool (`add_to_cart:Enabled=false`) → agent gracefully offers alternative (no crash).

**Test Plan.** Ledger-write tests, limit-trip tests, rate-limit tests (fake clock), redaction tests (address never fully persisted), kill-switch tests.

**Deliverable / Exit.** Agent is debuggable and bounded; admin can answer "what did the AI do and why?".

## M20 — Production Hardening + Observability — �lellow (completes V1 debts too)

**Goal.** Close V1 security/perf gaps and make the whole system (API, workers, AI) observable.

Security:

- [ ] JWT: 15-min access + 7-day rotating refresh (`RefreshTokens` table, reuse-detection logout-all), strong secret validation at boot (fail if <32 chars in Production).
- [ ] Password policy: min 8 + upper/lower/digit/symbol (update `Validators.cs`), HaveIBeenPwned check optional/behind flag.
- [ ] Rate limit auth endpoints (login 10/min/IP), global 200/min/IP; CORS allowlist (remove `AllowAnyOrigin`, configure `Cors:Origins`); security headers (`HSTS, X-Content-Type-Options, Referrer-Policy, CSP` for API minimal).
- [ ] Validation review: `pageSize<=100`, `search<=200 chars`; authorization re-audit (ownership tests for every `Mine` endpoint); secrets via env/UserSecrets/KeyVault — never `appsettings.json`.
- [ ] `.gitignore` extend: `.env`, `appsettings.Development.json`, `*.pfx`, `TestResults/`, `.idea/`.

Performance/Reliability:

- [ ] Indexes audit (`EXPLAIN` hot queries: product query, order-by-user, outbox poll, behavior insert); query optimization (no N+1 — `Include` review); pagination caps everywhere.
- [ ] Redis connection pooling + timeouts; RabbitMQ confirm + consumer prefetch tuning; Postgres pooling (`MaxPoolSize`) documented.
- [ ] Retry policies (Polly or manual) for provider/RabbitMQ/Redis; idempotency keys on checkout/agent confirm already — verify.
- [ ] Health: `/health/live` (process), `/health/ready` (db+redis+rabbitmq checks), `/health` UI JSON; graceful shutdown (drain workers 10s).

Observability:

- [ ] Serilog structured JSON (`CorrelationId, UserIdHash, ToolName, DurationMs`), request logging middleware already → enrich.
- [ ] OpenTelemetry: traces (`ASPNETCORE, EFCore, Redis, RabbitMQ, HttpClient-AI`), metrics (http latency histogram, db latency, queue depth/lag, worker failures, payment failures, AI tool latency/errors, token usage), OTLP exporter behind `OTEL_EXPORTER_OTLP_ENDPOINT`.
- [ ] Dashboards/alerts checklist (no vendor lock in plan): p95 latency, 5xx rate, queue lag >60s, worker restarts, payment failure spike, AI error rate, DLQ depth.

**Acceptance Criteria.**

- [ ] `zap`-lite / manual header check: security headers present, CORS blocks unlisted origin, login throttled.
- [ ] `/health/ready` fails correctly when Postgres stopped (compose stop test), recovers without restart.
- [ ] Load smoke: 100 concurrent catalog reads p95 within 2x of single-req; checkout success rate 100% at 10 concurrent distinct-user checkouts (no oversell — assert stock).

**Test Plan.** Auth hardening tests, CORS tests, health tests (dependency down), load smoke script (`k6` or `wrk` one-liner documented, not committed as flaky CI).

**Deliverable / Exit.** System is secure-by-default, observable, and survives dependency blips.

## M21 — Deployment — ⬜ FINAL

**Goal.** Public demo with reproducible Docker + managed services.

Target:

```text
Internet → Frontend (Vercel/static) → ASP.NET Core API (Render/Fly/Azure)
  ├── PostgreSQL (Neon, +pgvector) ──▶ MCP Server (same host or sidecar) ──▶ AI Provider
  ├── Redis (Upstash)                ▶ Workers (same image, --worker profile)
  └── RabbitMQ (CloudAMQP/self-host)
```

**Tasks.**

- [ ] Dockerfiles: `src/Shop.Api/Dockerfile` (exists — verify multistage, non-root, `ASPNETCORE_URLS=8080`), new `src/Shop.Worker/Dockerfile`, `src/Shop.Mcp/Dockerfile` (or combined `--project` targets). `.dockerignore` (bin/obj/.git).
- [ ] `docker-compose.prod.yml`: api+worker+mcp profiles, env-only secrets, pgvector image for self-host option, resource limits.
- [ ] Migrations strategy: run `dotnet ef database update` via init container/job on deploy (never auto-seed prod data; seed only demo catalog behind `SEED_DEMO=true`).
- [ ] Env matrix documented: `ConnectionStrings__Default, Redis__ConnectionString, RabbitMq__*, Jwt__Secret/Issuer/Audience, Ai__Provider/Model/ApiKey, OTEL_EXPORTER_OTLP_ENDPOINT, SEED_DEMO, ApplyMigrations`.
- [ ] Deploy: Neon Postgres (enable `vector`), Upstash Redis, CloudAMQP RabbitMQ (or single VM), API host, static frontend host with `API_BASE` configured. Custom domain + HTTPS (host-provided).
- [ ] Smoke suite post-deploy: register→login→search→cart→prepare→confirm→order visible; agent chat demo; `/health/ready` 200; rollback plan (previous image tag + migration-down note — prefer forward-fix migrations only).

**Acceptance Criteria.**

- [ ] Fresh clone → `docker compose up --build` serves API+worker+MCP locally (documented ports).
- [ ] Public URLs live: frontend, `/swagger` (or `/health`), demo video script runnable end-to-end.
- [ ] No secrets in image history or repo; `docker scout`/manual env audit passes.

**Test Plan.** Compose-up smoke test; prod-config validation test (required envs enumerated at boot with clear errors).

**Deliverable / Exit.** Anyone can run or view the full system: local compose + public demo + README deploy section.

---

# Appendices

## A. Event catalog (locked v1, see M10–M11)

| Event | Producer | Consumers (M11+) |
|---|---|---|
| `order.created` | CheckoutService | email(log), analytics(behavior), recommendation(future) |
| `order.paid` | CheckoutService | email(log), analytics |
| `payment.failed` | CheckoutService | analytics, agent-safe error surface |
| `order.cancelled/shipped/delivered` | OrderService | email(log), analytics |
| `product.created/updated` | ProductService (M10 wiring) | cache evict, embedding worker (M17) |

## B. Tool risk taxonomy (locked, enforced M16)

```text
READ: search_products, get_product, compare_products, get_product_inventory, get_cart, get_orders, get_order, get_order_status
WRITE: add_to_cart, update_cart, remove_from_cart
SENSITIVE_WRITE: cancel_order
CRITICAL_WRITE: create_order (via confirmed AiAction only)
NEVER_EXPOSED_TO_AGENT: admin products/categories/inventory/order-status, refresh tokens, secrets
```

## C. Cache policy summary (M9)

Cache: `categories:all` 10m, `product:{id}` 5m, `products:q:*` 1–2m, `hybrid:*` 60s. Evict on corresponding writes. Never cache cart/orders/auth/checkout. `X-Cache` header required.

## D. Definition of Done (every milestone)

- [ ] Code + migration (if any) + config + docs updated
- [ ] Unit + integration tests added and green (`dotnet test`)
- [ ] Manual demo steps in PR description with request/response or screenshots
- [ ] PLAN.md checkboxes ticked in same PR (keep this file live)
- [ ] No secrets committed; health still green

## E. Suggested branch order

`m9-redis` → `m10-outbox` → `m11-workers` → `m12-mcp` → `m13-tools` → `m14-provider` → `m15-agent` → `m16-hitl` → `m17-vector` → `m18-reco` → `m19-ai-safety` → `m20-hardening` → `m21-deploy`

## F. Open questions (resolve when entering milestone, not now)

- M14: Gemini vs OpenRouter default model + embedding dim (locks M17 dim) — decide at M14 kickoff.
- M11 prod RabbitMQ: managed vs self-hosted on same VM (cost vs ops).
- M18: behavior retention 12m vs shorter for GDPR posture.
- M20: refresh-token vs sliding JWT-only (decide before implementation).

## G. Glossary

Outbox, DLQ, HITL, pgvector/HNSW, RRF/hybrid fusion, MCP tool, ReAct, idempotency key, correlation ID — all used in standard senses; see milestone sections for repo-specific meanings.
