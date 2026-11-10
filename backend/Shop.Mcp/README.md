# Shop.Mcp (A1)

MCP server for ShopMind's Assist-level shopping agent. It exposes a small,
policy-generated read toolset and calls the Application services — never a
database context or raw SQL.

## Tools

| Tool | Risk | Anonymous | What it does |
|---|---|---|---|
| `search_products` | READ | yes | Search active products by text, category, brand, price, stock, sort, page |
| `get_product` | READ | yes | Product detail + available stock |
| `get_product_facets` | READ | yes | Category specification filters (registry ∩ active data) |
| `get_cart` | READ | no | The authenticated user's cart |

`confirm_payment` is documented as `NEVER` in the policy and is absent from the
manifest. A2 adds the remaining cart/order/quote tools.

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
| `Jwt:Secret` / `Jwt:Issuer` | dev values | Must match the API |
| `Mcp:Audience` | `Shop.Mcp` | Audience required on delegated agent JWTs |
| `Mcp:KeyPrefix` | `shop:mcp:rl:` | Redis namespace for budgets |
| `Mcp:ReadPerMin` | 30 | READ tools per user per minute |
| `Mcp:WritePerMin` | 12 | Reserved for A2 |
| `Mcp:QuotePerMin` | 4 | Reserved for A2 |
| `Mcp:WindowSeconds` | 60 | Sliding-window size |
| `Mcp:MaxArgsBytes` | 4096 | Reject larger argument payloads |
| `Mcp:MaxResultBytes` | 32768 | Reject larger tool results |
| `Mcp:MaxPageSize` | 50 | `search_products` page-size cap |
| `Tools:{name}:Enabled` | `true` | Per-tool kill switch; disabled tools are absent |

In .NET environment form use double underscores, for example
`Mcp__Audience`, `Mcp__ReadPerMin`, `Tools__get_product__Enabled`.

## Auth and error contract

- Anonymous callers may use the three public READ tools.
- `get_cart` requires a delegated agent JWT: audience `Shop.Mcp`, `scope=agent`.
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
`UPSTREAM_ERROR`, `RATE_LIMITED`, `RESULT_TOO_LARGE`, `TOOL_NOT_FOUND`.

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
switch, anonymous catalog reads, cart ownership through two agent JWTs,
`UNAUTHORIZED` without a token, strict argument validation, the READ budget,
and token-free logs. It uses the same disposable Postgres/Redis seam as the API
tests (`SHOPMIND_TEST_POSTGRES`, `SHOPMIND_TEST_REDIS`).
