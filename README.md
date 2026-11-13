# ShopMind V1 — Electronics E-commerce Platform

> Layered ASP.NET Core + PostgreSQL + JWT + Fake Payment. Minimal vanilla-JS frontend included.

## Architecture

```
frontend (vanilla SPA)
   │
   ▼
Shop.Api → Shop.Application → Shop.Domain
              ▲                    ▲
              │                    │
     Shop.Infrastructure ──────────┘
              ▼
          PostgreSQL
```

## Quickstart (local, without Docker)

Requires .NET 10 SDK + local PostgreSQL.

```bash
# 1. Create DB + set connection string if needed (default: Host=localhost;Port=5432;Database=shopmind;Username=postgres;Password=postgres)
# appsettings.json → ConnectionStrings:Default

# 2. Run API (auto-migrates + seeds)
dotnet run --project src/Shop.Api

# API: http://localhost:5121/swagger
# Frontend: open frontend/index.html via static server, set API base to http://localhost:5121
cd frontend && npx serve .
```

Seeded users:
- `admin@shopmind.local` / `Admin123!`
- `customer@shopmind.local` / `Customer123!`

## Quickstart (Podman Compose)

Requires the Podman machine running (`podman machine start`).

```bash
podman compose up --build
# API: http://localhost:5000/swagger (via container)
# MCP (A1): http://localhost:5001/mcp
# Postgres: localhost:15432 (5432 is the host's own PostgreSQL, left alone)
# Redis: localhost:6379   RabbitMQ: localhost:5672, UI on :15672
```

On Windows, `win-sshproxy` exposes that API as `npipe:////./pipe/docker_engine`
and `npipe:////./pipe/podman-machine-default`, which `podman compose` and
Podman Desktop both use — no `DOCKER_HOST` needed. If Podman Desktop shows no
containers after a machine start, a stale `%TEMP%\podman\*.api.sock` is making
`win-sshproxy` exit before it creates those pipes: delete it and run
`podman machine restart`.

## API surface

- `POST /api/auth/register, POST /api/auth/login, GET /api/auth/me`
- `GET /api/products?search=&category=&brand=&minPrice=&maxPrice=&specs=&sort=&page=&pageSize=`
- `GET /api/products/facets?category=` (A3: registry-driven range/enum facets, 10 min cache)
- `GET /api/products/{id}`, `GET /api/categories`
- `GET /api/cart, POST /api/cart/items, PATCH /api/cart/items/{id}, DELETE /api/cart/items/{id}, DELETE /api/cart`
- `POST /api/checkout`
- `GET /api/orders, GET /api/orders/{id}, POST /api/orders/{id}/cancel`
- `GET/POST/GET/PUT/DELETE /api/admin/products…`, `/api/admin/categories…`, `/api/admin/inventory…`, `/api/admin/orders…`
- `POST /mcp` — MCP Streamable HTTP (A1; tools, auth, budgets in `backend/Shop.Mcp/README.md`)

See Swagger for full docs. Validation errors return `{ title, status, errors }`.

## MCP server (A1)

`backend/Shop.Mcp` exposes the A1 read tools (`search_products`, `get_product`,
`get_product_facets`, `get_cart`) over Streamable HTTP and STDIO. Run it locally:

```bash
dotnet run --project backend/Shop.Mcp
# Streamable HTTP: http://localhost:5122/mcp
# Health:          http://localhost:5122/health

dotnet run --project backend/Shop.Mcp -- --transport stdio
```

The tool manifest is generated from `Shop.Application.Mcp.IToolRiskPolicy` and
the `Tools:{name}:Enabled` kill switches. Anonymous callers get the three public
catalog reads; `get_cart` needs a delegated agent JWT (`aud=Shop.Mcp`,
`scope=agent`). `Shop.Api` rejects `scope=agent` tokens on every `/api/*` route
with `403 AgentScopeForbidden`. Redis sliding-window budgets and the strict error
envelope are covered in `backend/Shop.Mcp/README.md`.

## AI provider (M10 — A4)

Every agent call goes through `IAiProvider.GenerateAsync`. One
`OpenAiCompatibleProvider` speaks OpenAI Chat Completions with `tools`, so a
provider switch is configuration only:

| `Ai:Provider` | Default base URL | Default model | Key |
|---|---|---|---|
| `opencode-go` (dev default) | `https://opencode.ai/inference/go/openai/v1` | `deepseek-v4.1-flash` | required |
| `deepseek` | `https://api.deepseek.com/v1` | `deepseek-chat` | required |
| `openrouter` | `https://openrouter.ai/api/v1` | `deepseek/deepseek-chat` | required |
| `openai` | `https://api.openai.com/v1` | `gpt-4o-mini` | required |
| `groq` | `https://api.groq.com/openai/v1` | `llama-3.3-70b-versatile` | required |
| `ollama` | `http://localhost:11434/v1` | `llama3.1` | not needed |
| `fake` | none | deterministic | not needed |

```bash
Ai__Provider=deepseek Ai__ApiKey=sk-... dotnet run --project backend/Shop.Api

# Overrides, only when the preset is not enough:
Ai__BaseUrl=https://my-gateway/v1 Ai__Model=my-model
```

`Ai:Model` and `Ai:BaseUrl` are overrides, so leaving them unset keeps the
provider-plus-key switch. `opencode-go` sends `x-opencode-session` and a
ShopMind `User-Agent`. The adapter has a 20s timeout and 2 retries with jitter
on 429/5xx/network faults. Failures are `AiProviderException` with code
`AI_NOT_CONFIGURED` or `AI_PROVIDER_ERROR` and a `Retryable` flag; a missing key
never blocks startup or non-AI routes. `Ai:Provider=fake` is deterministic,
needs no key, and is what the integration test factory uses.

## Caching (M9 — Redis)

Hot catalog reads are cached in Redis via `ICacheService`
(`src/Shop.Application/Caching/`) backed by `RedisCacheService`
(`src/Shop.Infrastructure/Caching/`):

| Cached read | Key | TTL |
|---|---|---|
| `GET /api/products?...` | `shop:products:q:{sha1(search\|category\|brand\|min\|max\|specs\|sort\|page\|size)}` | `Redis:DefaultTtlSeconds` (default 90s) |
| `GET /api/products/facets?category=` | `shop:facets:{category-or-all}` | 10 min |
| `GET /api/products/{id}` (public only) | `shop:product:{id}` | 5 min |
| `GET /api/categories` (public list) | `shop:categories:all` | 10 min |

### Spec filters and facets (A3)

`specs` is a fail-closed filter grammar: `key:op:value` clauses joined by `;`,
`in` values joined by `|` (e.g. `ram_gb:gte:16;panel:in:IPS|VA`). Ops by
registry type: range `gte|lte|eq`, enum `eq|in`, bool `eq`. An unregistered key,
invalid operator, or non-coercible value returns `400 InvalidSpecFilter`.
Specs live in `Product.Specs` (`jsonb`, GIN-indexed) and are validated against
`SpecRegistry` (the same vocabulary drives seed validation and facets). Facets
only include registered keys populated by Active products; bool specs never
appear.

- Cached responses carry `X-Cache: HIT | MISS`. Admins can bypass with
  `?nocache=1` (or `X-Bypass-Cache: 1` header) → `X-Cache: BYPASSED`.
- Invalidation (mandatory, best-effort): product create/update/delete →
  `product:{id}` + `products:q:*` + `facets:*`; category write → `categories:all`
  (+ `products:q:*`, `facets:*`); inventory quantity change → `product:{id}` +
  `products:q:*` + `facets:*`.
- Never cached: cart, checkout, orders, auth/me, admin lists.
- Resilience: Redis down or `Redis:Enabled=false` → API still serves 200
  from Postgres (MISS), warning logged at most once/minute.
- Checkout always revalidates live stock/prices, so a briefly stale cache
  can never oversell.

Config (`appsettings.json`, overridable via env):

```json
"Redis": { "ConnectionString": "localhost:6379", "Enabled": true, "DefaultTtlSeconds": 90 }
```

Compose runs `redis:7-alpine` (API uses `Redis__ConnectionString:
redis:6379`). Local dev outside Compose: run Redis locally or set
`Redis:Enabled=false` to silence connection warnings.

## Projects

```
src/Shop.Domain, src/Shop.Application, src/Shop.Infrastructure, src/Shop.Api
tests/Shop.UnitTests, tests/Shop.IntegrationTests
frontend/ (vanilla SPA)
```

## Tests

```bash
dotnet test tests/Shop.UnitTests
# Integration needs a container runtime (Testcontainers + PostgreSQL);
# Podman works through the same docker_engine pipe:
dotnet test tests/Shop.IntegrationTests
```

## V1 Definition of Done — status

- Backend: layered arch ✓, EF migrations ✓, Postgres ✓, JWT auth ✓, admin/customer authz ✓, validation ✓, global errors ✓, checkout transaction ✓, unit tests ✓, integration tests ✓ (need Docker), Swagger ✓
- Frontend minimal: listing ✓, detail ✓, auth ✓, cart ✓, checkout ✓, orders ✓, admin ✓
