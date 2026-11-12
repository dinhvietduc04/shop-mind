# Shop.Mcp (A1 + A2 + M13)

MCP server for ShopMind's Assist-level shopping agent. It exposes a
policy-generated toolset and calls the Application services — never a database
context or raw SQL.

## Tools

| Tool | Risk | Anonymous | What it does |
|---|---|---|---|
| `search_products` | READ | yes | Search active products by text, category, brand, price, stock, specs, sort, page |
| `get_product` | READ | yes | Product detail + available stock |
| `get_product_facets` | READ | yes | Category specification filters (registry ∩ active data) |
| `compare_products` | READ | yes | Deterministic 2–4 product table: header columns + aligned spec rows |
| `find_products_for_requirement` | READ | yes | Deterministic budget/category/facet extraction → ranked matches + applied filters + elicitation questions |
| `get_cart` | READ | no | The authenticated user's cart |
| `get_orders` | READ | no | The user's orders and quotes, newest first |
| `get_order` | READ | no | One owner-scoped order |
| `get_order_status` | READ | no | Status projection of one owner-scoped order |
| `prepare_checkout` | READ | no | Side-effect-free reprice of the cart: line issues + subtotal/shipping/total; creates nothing |
| `add_to_cart` | WRITE | no | Add a product; merges quantities; requires `idempotencyKey` |
| `update_cart` | WRITE | no | Set an owned cart line's absolute quantity; requires `idempotencyKey` |
| `remove_from_cart` | WRITE | no | Remove an owned cart line; requires `idempotencyKey` |
| `create_quote` | QUOTE | no | Bill from the cart for the human to pay; requires `idempotencyKey` |

`confirm_payment`, `cancel_order`, and `refund_payment` are documented as
`NEVER` in the policy and are absent from the manifest. Payment and cancellation
stay human-only.

## Agent shopping rules

- `compare_products` takes 2 to 4 product ids in caller order. It returns a
  header per product (name, SKU, brand, category, price, available stock, first
  image) and registry-ordered spec rows. A spec a product does not carry is
  `null`, not `false`. Unregistered jsonb keys are dropped; reserved stock never
  appears.
- `search_products.specs` is `[{key, op, value}]`, where `op` is
  `gte | lte | eq | in`. It maps to the same fail-closed grammar as REST: an
  unregistered key or malformed clause is `INVALID_ARGS`, never a silently
  unfiltered page.
- `find_products_for_requirement` is the deterministic discovery tool. It reads
  a price cap and category from `requirementText` (explicit `budget` and
  `categoryHint` win), discovers the category facets, and adds only the A3
  use-case hints the facets support (for example "backend dev" → `ram_gb >= 16`).
  It searches once through the same filtered-search path as `search_products`
  and returns ranked matches with the applied filters, up to three questions
  when the request is fuzzy, and a per-item "why". It is not an LLM or a
  recommendation engine; an unknown `categoryHint` is `INVALID_ARGS`.
- `prepare_checkout` is a dry run of the bill. It revalidates the authenticated
  user's cart with the same server prices and shipping rule as `create_quote`,
  reports per-line issues, and sets `canQuote=false` when something is wrong.
  It creates no order, payment session, or stock reservation — the human still
  reviews and pays via `create_quote`/the hosted page.
- Cart tools reuse `ICartService` and the REST validators, so merge, stock
  check, repricing, and ownership rules are identical to HTTP.
- `create_quote` reuses `IQuoteService` with `QuoteOrigin.Agent`. It may replace
  only another agent-origin open quote. A human-origin open quote blocks it with
  `QUOTE_HELD_BY_HUMAN`; the agent narrates "pay or cancel your existing bill
  first". The result contains the bill and a hosted pay URL for the human; the
  agent never opens or confirms it.
- Money never moves through MCP. The quote response includes the capability
  token only so the human's page can use it.

## Idempotency

Every WRITE and QUOTE tool requires a tool argument
`idempotencyKey` (1 to 128 characters). The guard derives the requirement from
the policy risk class, so a future write tool cannot forget it. Keys are scoped
per user and tool under the Redis idempotency store:

- same key + same arguments → replay the stored result, do not execute again;
- same key + different arguments → `IDEMPOTENCY_MISMATCH`;
- same key still running → `IDEMPOTENCY_IN_PROGRESS` (retryable);
- store unavailable → `IDEMPOTENCY_UNAVAILABLE` (retryable), and the tool does
  not run.

A failed attempt releases its owner-checked lease so a retry can execute. This
is the same `IIdempotencyStore` and fail-closed policy the REST money path uses
(ADR-0004).

## Run it

HTTP (local):

```bash
dotnet run --project backend/Shop.Mcp
# Streamable HTTP: http://localhost:5122/mcp
# Health:          http://localhost:5122/health
```

STDIO (local agent/Inspector):

```bash
dotnet run --project backend/Shop.Mcp -- --transport stdio
```

STDIO mode writes logs to stderr; stdout is reserved for the MCP protocol.

Docker Compose:

```bash
docker compose up --build
# MCP: http://localhost:5001/mcp
```

The API owns migrations and seed data; start it before relying on catalog tools.

## Configuration

| Setting | Default | Notes |
|---|---|---|
| `ConnectionStrings:Default` | local Postgres | Same database as the API |
| `Redis:ConnectionString` | `localhost:6379` | MCP budgets use Redis |
| `Idempotency:ConnectionString` | `localhost:6379` | Fail-closed idempotency store |
| `Idempotency:KeyPrefix` | `shop:idem:` | Redis namespace for idempotency keys |
| `Jwt:Secret` / `Jwt:Issuer` | dev values | Must match the API |
| `Mcp:Audience` | `Shop.Mcp` | Audience required on delegated agent JWTs |
| `Mcp:KeyPrefix` | `shop:mcp:rl:` | Redis namespace for budgets |
| `Mcp:ReadPerMin` | 30 | READ tools per user per minute |
| `Mcp:WritePerMin` | 12 | WRITE tools per user per minute |
| `Mcp:QuotePerMin` | 4 | `create_quote` per user per minute |
| `Mcp:WindowSeconds` | 60 | Sliding-window size |
| `Mcp:MaxArgsBytes` | 4096 | Reject larger argument payloads |
| `Mcp:MaxResultBytes` | 32768 | Reject larger tool results |
| `Mcp:MaxPageSize` | 50 | `search_products` page-size cap |
| `Tools:{name}:Enabled` | `true` | Per-tool kill switch; disabled tools are absent |

In .NET environment form use double underscores, for example
`Mcp__Audience`, `Mcp__ReadPerMin`, `Tools__get_product__Enabled`.

## Auth and error contract

- Anonymous callers may use the five public READ tools.
- Every cart, order, and quote tool requires a delegated agent JWT: audience
  `Shop.Mcp`, `scope=agent`.
- `Shop.Api` rejects `scope=agent` tokens on every `/api/*` route with `403
  AgentScopeForbidden`; the anonymous hosted pay page remains viewable.
- STDIO dev mode builds the context from `MCP_DEV_USER_ID`,
  `MCP_DEV_ROLE`, and `MCP_DEV_SCOPE`.
- Tool failures return `CallToolResult.IsError = true` with:

```json
{
  "code": "UNAUTHORIZED",
  "message": "A valid user token is required for this tool.",
  "correlationId": "0f8c...",
  "retryable": false
}
```

Codes: `INVALID_ARGS`, `UNAUTHORIZED`, `FORBIDDEN`, `NOT_FOUND`, `CONFLICT`,
`UPSTREAM_ERROR`, `RATE_LIMITED`, `RESULT_TOO_LARGE`, `TOOL_NOT_FOUND`,
`QUOTE_HELD_BY_HUMAN`, `IDEMPOTENCY_IN_PROGRESS`, `IDEMPOTENCY_MISMATCH`,
`IDEMPOTENCY_UNAVAILABLE`.

Logs record the tool, status, duration, correlation id, and a hashed user id —
never the JWT or tool arguments.

## Manifest and policy

`Shop.Application.Mcp.IToolRiskPolicy` is the single tool-to-risk mapping. The
MCP manifest is generated from it, filtered by `Tools:{name}:Enabled`.
`NEVER` and disabled tools are removed before `tools/list`, so the capability is
absent rather than merely denied. A tool method without a policy entry fails the
architecture test.

## Tests

```powershell
dotnet test tests/Shop.UnitTests --filter "FullyQualifiedName~Mcp"
dotnet test tests/Shop.IntegrationTests --filter "FullyQualifiedName~Mcp"
```

The integration suite covers HTTP and STDIO transports, the manifest/kill
switch, anonymous catalog reads, spec-filtered search, compare shape, cart and
order ownership through two agent JWTs, add/update/remove with idempotent
replay and mismatch, over-stock rejection, quote origin scope
(`QUOTE_HELD_BY_HUMAN`), quote replay, the scripted shopping scenario,
deterministic requirement search (budget + RAM hint, fail-closed unknown
category), the side-effect-free checkout preview (no order, no reservation),
the fail-closed idempotency outage, budgets, and token-free logs. It uses the
same disposable Postgres/Redis seam as the API tests (`SHOPMIND_TEST_POSTGRES`,
`SHOPMIND_TEST_REDIS`).
