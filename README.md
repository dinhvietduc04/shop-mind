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
- `GET /api/products?search=&category=&brand=&minPrice=&maxPrice=&sort=&page=&pageSize=`
- `GET /api/products/{id}`, `GET /api/categories`
- `GET /api/cart, POST /api/cart/items, PATCH /api/cart/items/{id}, DELETE /api/cart/items/{id}, DELETE /api/cart`
- `POST /api/checkout`
- `GET /api/orders, GET /api/orders/{id}, POST /api/orders/{id}/cancel`
- `GET/POST/GET/PUT/DELETE /api/admin/products…`, `/api/admin/categories…`, `/api/admin/inventory…`, `/api/admin/orders…`

See Swagger for full docs. Validation errors return `{ title, status, errors }`.

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
