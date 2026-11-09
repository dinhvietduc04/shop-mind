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

- `src/lib/api/types.ts` — DTOs mirroring backend plan contracts: `ProductDto(+specs)`, `CartDto`, `QuoteDto{order: OrderDto, hostedPayUrl, expiresAt}` (**order-embedded is canonical** — AI_ASSISTANT_PLAN §B5+; the flat `{items, subtotal, shipping, total}` bill is a view of Order fields), `OrderDto(+payment.outcome: captured|failed|expired|null)`, `FacetDto(+unit, count)`, `ChatTurn{replyText, toolCalls[], pendingQuoteId?, turnCap, turnsUsed}`, admin DTOs.
- MSW handlers implement the whole REST surface from README §"API surface" **plus** the new quote/pay/agent/facets endpoints using realistic fixtures (reuse seed catalog from `backend/Shop.Infrastructure/Seed/SeedData.cs` — transcribe to `fe/src/mocks/catalog.json`).
- Include deliberately flaky/limit scenarios behind env flag `NEXT_PUBLIC_MOCK_MODE=slow|ratelimit|paymentfail|agentdown` so all 5 states + 429 + expired-quote flows + provider-unavailable fallback are demoable without backend.
- API base swap later: `NEXT_PUBLIC_API_URL` + drop MSW → real `Shop.Api`. Auth: store JWT, `Authorization` header pipeline; mock login matches seeded users.

## 7. Execution phases (UI-first order)

| # | Phase | Deliverable | Exit |
|---|---|---|---|
| F0 | Init + Impeccable | §2 steps done, `/impeccable init` captured, tokens/primitives, shell layout, dark mode | `detect` clean on scaffold; Storybook-less primitive gallery route `/dev/ui` |
| F1 | Mock layer | types + MSW fixtures + flaky modes (§6) | ✅ all handlers typed, catalog parity with seed |
| F2 | Storefront | home, listing+server filters (sort/in-stock/facet chips)+pagination, detail w/ specs table, category landings | ✅ Playwright: search→filter→detail→add-2 flow green; 404s render instantly (4xx no-retry) |
| F3 | Cart → Quote → FakePsp → Orders | §5.3 + §5.4 incl. expiry countdown & result pages | ✅ Playwright: quote→pay→Paid and quote→expire→released flows green (also: abandon, ledger cancel w/ state-machine legality, reload-recovery via sessionStorage pointer, MSW HMR survivor guard) |
| F4 | AI assistant UI | §5.5 all cards, SSE against mock, tool-trail, elicitation, quote card | ✅ golden "laptop under $1200 for backend dev" → tension-honest interview → relax → results → add → quote → bill hand-off; assist invariants enforced (no pay tool exists; human-owned quote undeletable by agent); agentdown + ratelimit modes green |
| F5 | Auth + account | §5.2: /login /register (RHF+Zod, server-error surface, one-click demo ids), /account (profile + shipping preset feeding quotes), RequireAuth/RequireRole, guest-by-default boot, admin role gating | ✅ unauth/guest paths green (public browse; add/quote/orders/chat bounce to turnstile with return-path; 403 for wrong role; sign-out respected; 409 duplicate + inline validation surfaced) |
| F6 | Admin | §5.6 all CRUD + status machine dropdowns + AI audit mock | ✅ admin runs store on UI alone — Playwright: create product (with Specs-JSON validation + band preview) → storefront sees it live → set Inactive → hides; category create + FK-refuse-on-delete; inventory +25 reflected instantly, client guard AND server both refuse writes below reserved; paid order transitioned Paid→Processing→Shipped→Delivered (legal-only dropdown, Delivered shows terminal card, illegal transition 400s server-side); AI ledger populates from real chat turns (per-tool + llm_turn rows, tokens/durations), conversations rollup, kill switch (master off → 503 fallback UI; per-tool off → agent degrades honestly). Mock catalog made mutable (single source of truth for storefront+admin+agent). Also fixed drawer-contrast bug: in-drawer light card surfaces (product tile/elicit/quote/provider) inherit the dark island's `text-drawer-ink` → forced `text-ink` (provider 12.9:1, elicit 7.7:1 AA); relax budget slider step 50→1 so max (1799) is reachable — was stuck at 1750 — and its stale header relabelled static "Change the price range" |
| F7 | Polish + gates | a11y pass (keyboard nav, aria on drawer/chat), responsive sweep, empty/error states everywhere, Lighthouse ≥ 90 perf on storefront, `impeccable detect` zero on app+URL, `docs/PLAN.md` cross-ref updated | ✅ keyboard: skip-to-content link is first tab-stop and moves focus to `#shop-content`; admin AI kill-switch toggles are `role=switch` (aria-checked + describedby) and fire from keyboard (Enter flips Ai:Enabled → chat 503s); chat composer reachable + Enter-sends. **Boot decoupled from paint**: pages render immediately, `api()` parks requests on a `readyGate` until MSW worker is live (and `whenSessionReady()` gates auth decisions) — fixes a real regression where a fast click misread a logged-in user as guest; Lighthouse storefront **PERF 93** (FCP 0.8 / LCP 0.8 / CLS .04 / TBT 330), down from 50→72→93. Responsive: 0px horizontal overflow at 320/390/768/1280 across 11 routes (masthead wraps, admin search shrinks). Empty/error states on every admin grid + inventory filter. `impeccable detect` [] on app+URL (cream-palette waived as the documented paper/kraft/ink world). Playwright golden runs end-to-end on the optimized build, console clean |
| F8 | **Backend wiring (later)** | swap MSW → real API milestone-by-milestone (B1–B5, A1–A7 in backend plan), contract tests | each swap keeps Playwright suite green against real API |

### Architecture deepening waves (settled 2026-11-09, pre-F8)

Nine candidates from the architecture review (`architecture-review-20261109-1605.html`); all decisions grilled and accepted. Behavior-preserving by rule — any user-visible delta (fail-open→fail-closed filters, outcome copy) was separately approved. Wire-shape decisions are recorded as drafts in `AI_ASSISTANT_PLAN.md` §B5+.

| Wave | Candidates | Scope (settled shape) | Done when |
|---|---|---|---|
| W1 seams | C3 turn transport · C2 credentials | `lib/chat/transport.ts` owns SSE codec, code-keyed errors, auth header, readyGate; `turnCap/turnsUsed` server-delivered. Credentials = sole token reader + header builder; `asMember()` absorbs the settle-order ritual; one guard module. Vitest installed; interface tests per module | ✅ **done** — 13 vitest specs green (transport codec/taxonomy + `asMember` ordering, including the F7 guest-race regression pinned as a test). Playwright golden on dev: guest paint, guest chat nudge, elicitation→pick→add→quote→PSP→paid→order, kill switch now reaching the UI through `code`-first mapping (`AI_NOT_CONFIGURED`→provider), guest add-to-cart bounce with return path, turn label `new session` → `TURN 2/8` from the server envelope. `tsc`/`eslint`/`next build` 0, `detect` [], console clean |
| W2 domain knowledge | C5 state machine → C1 Quote intake → C4 tool registry → C6 invalidation recipes | One Quote-lifecycle module (legality, terminality, band, revenue-eligibility, **stock fate per move**) shared by mock guard and admin UI. Quote intake owns the session pointer + reads `payment.outcome`. Registry = full §A2 set, honest disabled switches, NEVER-as-absent. Named recipes over `qk` prefixes; transport stops name-sniffing | ✅ **done** — `lib/orders/lifecycle.ts` (cs-parity tripwire test), `lib/quotes/intake.ts` (pointer+reconciliation+`quoteOutcome`; `hostedPayUrl.split` and `!transactionId` deleted; §B5+ `sessionToken`/`payment.outcome` implemented in mock+DTO), `lib/tools/registry.ts` (16 tools, NEVER non-switchable, admin console derived, `turnTouches` replaces the sniff), `lib/api/keys.ts`+`invalidation.ts` (7 named recipes, zero raw-prefix callers left). 34 vitest specs; golden: human quote→paid, expiry card via outcome decode, agent quote discovered by bill (`useHeldQuote`), dropdown `Processing,Cancelled` only, illegal+bad-enum PATCH 400/400, inventory 40→65, admin/ai shows planned-disabled + never-section. Band unification shipped: admin tables now Delivered=blue, Processing=green, Cancelled=grey (the two maps had drifted; lifecycle is canonical). GLOSSARY += **Payment Outcome**. Console clean |
| W3 fake-server restructure | C8 attribute registry + spec codec → C7 db.ts split → C9 product intake | Colon grammar `key:op:value` mirroring MCP specs shape, **fail-closed** 400; one scan behind facets + Elicitation; `FacetDto +unit`. Domain modules pure-in-memory, handlers call persist at the seam, deferred init (Node-testable settle/oversell/origin policy). Product intake owns the two-endpoint sequence + camelCase problem-map parity with `ExceptionHandlingMiddleware` | ✅ **done** — `lib/attributes/{codec,registry}` (UI chips + agent + mock share one grammar; unregistered/malformed → 400 — **the chip filters, which had silently no-op'd under the old fail-open parser, now actually filter**; registry labels/units everywhere the `_`-mangle was hand-typed). `mocks/server/{snapshot,identity,inventory,catalog,cart,quotes,ai}` — 714-line db.ts → 7 domain modules + compatibility barrel; persist called ONLY at the HTTP seam (+ chat turn + lazy-settling reads); snapshot registry enumerates stores once (save/load/reset). **57 vitest specs incl. `quote-ledger.test.ts`: reserve-rollback, settle idempotency, expiry release + outcome stamp, QUOTE_HELD_BY_HUMAN origin scope, oversell guard — the product's riskiest invariants now Node-testable.** `lib/admin/{product-schema,product-intake}` — one field description enforced by form + mock server, camelCase problem map (`400 keys=name,sku,price` on the wire), partial-failure stock PATCH owned by intake + `catalogWriteStale` recipe. Browser: reload mid-quote keeps the Pending bill AND its reservation (avail 12→11 across reload); reserved-floor write refused 400; form inline validation via unified messages; chat turn through the split server; tsc/eslint/build/detector clean, console clean |

Gates every wave: `tsc --noEmit`, `eslint --max-warnings=0`, `impeccable detect` [], Playwright golden (chat→quote→PSP→paid, admin CRUD, expiry), responsive 0-overflow spot checks.

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
