# ShopMind — Frontend Plan (Next.js + Impeccable) — UI-First

> Scope: build the **entire UI feature set first** in a new Next.js app at `fe/`, driven by mocks/fixtures,
> then wire it to the backend later (`docs/PLAN.md` M10+ / `docs/AI_ASSISTANT_PLAN.md`).
> Design quality is enforced with [Impeccable](https://impeccable.style) (`npx impeccable install` + `detect`).
> Old vanilla `frontend/` stays untouched as reference until parity, then is archived.

Status date: 2026-11-06. Trust level in scope = **Assist** (AI browses/compares/carts/creates quote; **human** reviews bill and pays on PSP-hosted page).

---

## 1. Stack (locked)

| Concern | Choice | Notes |
|---|---|---|
| Framework | **Next.js (App Router, TypeScript)** | `create-next-app`, `src/` dir, `@/*` alias |
| Styling | Tailwind CSS (via create-next-app default) | + design tokens (§4) |
| Components | Hand-rolled primitives in `src/components/ui` | shadcn-style, no heavyweight lib until needed |
| Server state | TanStack Query | caching/mutations/optimistic cart |
| Client state | Zustand | session, cart drawer, chat UI state |
| Mock data | Fixtures + `MSW` (Mock Service Worker) handlers | mirror real REST contract (§6) so swap = change base URL |
| Forms/validation | React Hook Form + Zod | zod schemas shared with mock handlers |
| Testing | Playwright (UI flows) + Vitest (components) | CI: `fe` unit + `impeccable detect` gate |
| Design QA | **Impeccable skill** + `npx impeccable detect` | see §3 |
| Chat streaming | SSE (`/api/agent/chat` contract mocked first) | typed tool-trail rendering (§5.5) |

## 2. Bootstrap (exact steps, run once)

```powershell
cd C:\Users\dvduc\shop-mind
npx create-next-app@latest fe --typescript --tailwind --eslint --app --src-dir --import-alias "@/*" --use-npm
cd fe
npx impeccable install        # choose: scope=project, provider = your harness (opencode/claude/codex)
```

Then inside the AI harness on this repo: run `/impeccable init` once to capture project design context
(aesthetics doc / anti-patterns tuned to ShopMind). Commit whatever `.impeccable/` / harness skill dirs
the installer creates (project scope = repo-local, intentional).

Git: add `fe/` to the solution's workspace; extend root `.gitignore` later only for genuine noise
(`fe/.next`, `fe/next-env.d.ts` handled by create-next-app's own `.gitignore`).

## 3. Impeccable workflow (how design quality stays enforced)

1. **Per feature**: while building screens, follow the installed Impeccable skill (spacing, typography,
   contrast, states: hover/focus/disabled/loading/empty/error — every screen ships all 5 states).
2. **Per PR**: `npx impeccable detect src/app src/components` must be clean (or waived with a comment).
3. **CI**: add `fe-design` job → `npx impeccable detect --github src/` (annotations) + `npm run lint` + `vitest`.
4. **Live check before demo**: `npx impeccable detect http://localhost:3000` on the running app.

## 4. Design system (F0 deliverable)

- Tokens: brand color scale, semantic (`bg/surface/text/muted/accent/danger/success`), radius, spacing, type ramp
  — Tailwind theme extension in `tailwind.config.ts` + `globals.css` vars (dark mode from day 1, `next-themes`).
- Primitives: Button (variants incl. loading), Input/Select/Checkbox, Badge, Card, Dialog, Drawer (cart),
  Toast, Skeleton, EmptyState, Pagination, Table, Tabs, Avatar, Tooltip, Spinner.
- Patterns: price display (tabular numerals, currency util), stock badge (`In stock / Low / Out`),
  facet filter panel, product card, quote/bill card (§5.4), chat message bubbles with tool-trail chip.
- Layout shell: header (search, category nav, cart drawer trigger, account, **Ask AI** button),
  footer, breadcrumbs. Mobile-first; sidebar only in admin.

## 5. Screen/feature inventory (all UI, mock-first)

### 5.1 Storefront
| Route | Screen | Key states |
|---|---|---|
| `/` | Home: hero, categories grid, featured/new arrivals, low-stock-safe | loading skeletons, empty seed |
| `/products` | Listing: search box, filters (category, brand, price range, in-stock), sort, pagination | no-results w/ suggestions, filter chips, URL-synced query |
| `/products/[slug]` | Detail: gallery, price, stock, description, **Specs table** (jsonb facets — feeds AI questioning), add-to-cart qty stepper | out-of-stock disabled CTA, image fallback |
| `/category/[slug]` | Category landing = preset `/products` filter | — |
| `/search?q=` | alias of listing | — |

### 5.2 Auth & account
| Route | Screen |
|---|---|
| `/login`, `/register` | forms, validation inline, error surface, "continue as guest" |
| `/account` | profile, my addresses (shipping presets used at quote) |

### 5.3 Cart & checkout (Assist flow — the money boundary lives here)
| Route / UI | Screen |
|---|---|
| Cart drawer (global) + `/cart` | line items, qty edit, remove, subtotal, free-shipping progress bar (`>=500`), **Get Quote** CTA |
| `/quote/[id]` | **Bill review page**: items snapshot, subtotal/shipping/total, quote expiry countdown, statuses (`Pending`), big human-only CTA **"Review & Pay →"** (opens hosted PSP page), "Abandon quote" |
| `/checkout/success?session=…` / `/checkout/cancel` | result landing pages from PSP return URL |
| Mock `FakePsp` hosted page `/psp/[token]` | standalone chromeless page simulating PSP-hosted payment: bill read-only, card form (fake), Pay / Cancel → fires mock webhook event in MSW |

> UI invariant (Assist level): the **Pay action exists only on the bill/PSP pages reachable by a human session**.
> No chat message, card, or component ever renders a button that pays — chat only links to `/quote/[id]`.

### 5.4 Orders
| Route | Screen |
|---|---|
| `/orders` | paged list, status timeline badge (`Pending→Paid→Processing→Shipped→Delivered`, `Cancelled/PaymentFailed`) |
| `/orders/[id]` | detail + items + payment info + **Cancel** (only when legal per state machine, client mirrors rules) |

### 5.5 AI Assistant chat (the twist)
| Route / UI | Feature |
|---|---|
| `/assistant` full page + global side drawer | chat with agent |
| Message stream | user/assistant bubbles, streaming SSE with typing indicator |
| **Tool-trail chip** | each turn shows which tools ran (`search_products ×2, compare_products …`) — every price/stock in AI text must trace to a chip (transparency per backend plan §A7) |
| **Elicitation cards** | when agent asks attribute questions, render as structured UI: budget slider, category chips, spec pickers (from `get_product_facets` mock) → answers compose back as message |
| **Product result carousel** | compact cards from `search_products`/`compare_products` (compare = side-by-side table view) |
| **Quote card** | after `create_quote`: bill summary + expiry + "Review & Pay" → routes human to `/quote/[id]` (never pays inline) + "Edit cart instead" |
| Rate-limit/limit UX | 429 → friendly "slow down" + Retry-After countdown; turn cap → partial answer banner `limit_reached` |
| Auth nudge | anonymous intent involving cart/quote → inline login prompt card |
| Error states | `AI_NOT_CONFIGURED`, provider down → "assistant unavailable, browse manually" graceful fallback |

### 5.6 Admin (`/admin/*`, role-gated)
| Route | Screen |
|---|---|
| `/admin` dashboard | KPI tiles (orders, revenue, low stock, queue/DLQ depth placeholder, AI calls/day placeholder) |
| `/admin/products` (+`/new`, `/[id]/edit`) | table + CRUD form (name, SKU, price, category, status, images, **Specs JSON editor** w/ validation) |
| `/admin/categories` | CRUD, slugs |
| `/admin/inventory` | list + set quantity / adjust |
| `/admin/orders` | list, filter by status, detail, status transition dropdown (only legal targets — client mirrors `OrderTransitions`) |
| `/admin/ai` | conversations list, tool-call ledger table (tool/status/duration/tokens), kill-switch toggles UI (`Ai:Enabled`, per-tool) — mock now, real later |

## 6. Mock/data layer (F1 deliverable, unblocks everything)

- `src/lib/api/types.ts` — DTOs mirroring backend plan contracts: `ProductDto(+specs)`, `CartDto`, `QuoteDto{items, subtotal, shipping, total, expiresAt, hostedPayUrl}`, `OrderDto`, `FacetDto`, `ChatTurn{replyText, toolCalls[], pendingQuoteId?}`, admin DTOs.
- MSW handlers implement the whole REST surface from README §"API surface" **plus** the new quote/pay/agent/facets endpoints using realistic fixtures (reuse seed catalog from `backend/Shop.Infrastructure/Seed/SeedData.cs` — transcribe to `fe/src/mocks/catalog.json`).
- Include deliberately flaky/limit scenarios behind env flag `NEXT_PUBLIC_MOCK_MODE=slow|ratelimit|paymentfail` so all 5 states + 429 + expired-quote flows are demoable without backend.
- API base swap later: `NEXT_PUBLIC_API_URL` + drop MSW → real `Shop.Api`. Auth: store JWT, `Authorization` header pipeline; mock login matches seeded users.

## 7. Execution phases (UI-first order)

| # | Phase | Deliverable | Exit |
|---|---|---|---|
| F0 | Init + Impeccable | §2 steps done, `/impeccable init` captured, tokens/primitives, shell layout, dark mode | `detect` clean on scaffold; Storybook-less primitive gallery route `/dev/ui` |
| F1 | Mock layer | types + MSW fixtures + flaky modes (§6) | all handlers typed, catalog parity with seed |
| F2 | Storefront | home, listing+filters, detail w/ specs table, category | Playwright: search→filter→detail flow green |
| F3 | Cart → Quote → FakePsp → Orders | §5.3 + §5.4 incl. expiry countdown & result pages | Playwright: quote→pay→Paid and quote→expire→released flows green |
| F4 | AI assistant UI | §5.5 all cards, SSE against mock, tool-trail, elicitation, quote card | golden mock scenario "laptop under $1200 for backend dev" renders interview→results→quote→(human) pay |
| F5 | Auth + account | §5.2, route protection, admin role gating | unauth/guest paths green |
| F6 | Admin | §5.6 all CRUD + status machine dropdowns + AI audit mock | admin can run store on UI alone |
| F7 | Polish + gates | a11y pass (keyboard nav, aria on drawer/chat), responsive sweep, empty/error states everywhere, Lighthouse ≥ 90 perf on storefront, `impeccable detect` zero on app+URL, `docs/PLAN.md` cross-ref updated | demo script runs end-to-end on mocks |
| F8 | **Backend wiring (later)** | swap MSW → real API milestone-by-milestone (B1–B5, A1–A7 in backend plan), contract tests | each swap keeps Playwright suite green against real API |

## 8. Repo layout after init (target)

```text
fe/
  src/app            (routes per §5, (shop)/(admin)/(auth) route groups)
  src/components     ui/, product/, cart/, quote/, chat/, admin/
  src/lib            api/ (client+types), query/, auth/, mocks/ (msw+fixtures)
  src/stores         cart, session, chat
  .impeccable/       (project-scope skill/hook output — committed)
  e2e/               playwright specs per phase
  .github stays at repo root → add fe jobs to ci.yml in F0
```

## 9. Non-goals (explicit)

- No SSR data-fetching against backend yet (mocks + client components first; convert hot routes to RSC when F8 lands).
- No real Stripe SDK in UI — hosted page is just a URL hand-off.
- No ML/recommendation UI surfaces beyond attribute-question cards (per backend plan §A3).
- No i18n/multi-currency (single locale; price formatting util designed so it's later-possible).
- Do not delete `frontend/` until F8 parity is proven.

## 10. Risks

| Risk | Mitigation |
|---|---|
| Mock contracts drift from future real API | contracts copied verbatim from `docs/AI_ASSISTANT_PLAN.md` §5/§6; F8 adds contract test comparing real responses to fixture schemas |
| Chat UI over-promises (inline pay) | §5.3 UI invariant + Playwright test: no element with pay action exists in chat DOM |
| Impeccable installer behavior changes / silent-install bug | pin `impeccable` version in repo notes; verify skill files exist after install; fallback = submodule `npx impeccable link` per upstream docs |
| Big-bang admin later | F6 builds admin against same MSW shapes as today's REST admin endpoints (already real) — least mock drift |
| Feature-flag sprawl for mock modes | single `NEXT_PUBLIC_MOCK_MODE` enum, documented in `fe/README.md` |
