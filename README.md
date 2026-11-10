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

## Quickstart (Docker Compose)

Requires Docker Desktop running.

```bash
docker compose up --build
# API: http://localhost:5000/swagger (via container)
# Postgres: localhost:5432
```

## API surface

- `POST /api/auth/register, POST /api/auth/login, GET /api/auth/me`
- `GET /api/products?search=&category=&brand=&minPrice=&maxPrice=&specs=&sort=&page=&pageSize=`
- `GET /api/products/facets?category=` (A3: registry-driven range/enum facets, 10 min cache)
- `GET /api/products/{id}`, `GET /api/categories`
- `GET /api/cart, POST /api/cart/items, PATCH /api/cart/items/{id}, DELETE /api/cart/items/{id}, DELETE /api/cart`
- `POST /api/checkout`
- `GET /api/orders, GET /api/orders/{id}, POST /api/orders/{id}/cancel`
- `GET/POST/GET/PUT/DELETE /api/admin/products…`, `/api/admin/categories…`, `/api/admin/inventory…`, `/api/admin/orders…`

See Swagger for full docs. Validation errors return `{ title, status, errors }`.

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

Docker Compose runs `redis:7-alpine` (API uses `Redis__ConnectionString:
redis:6379`). Local dev without Docker: run Redis locally or set
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
# Integration needs Docker (Testcontainers + PostgreSQL):
dotnet test tests/Shop.IntegrationTests
```

## V1 Definition of Done — status

- Backend: layered arch ✓, EF migrations ✓, Postgres ✓, JWT auth ✓, admin/customer authz ✓, validation ✓, global errors ✓, checkout transaction ✓, unit tests ✓, integration tests ✓ (need Docker), Swagger ✓
- Frontend minimal: listing ✓, detail ✓, auth ✓, cart ✓, checkout ✓, orders ✓, admin ✓
