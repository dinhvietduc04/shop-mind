# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

Next.js (App Router, TypeScript, Tailwind v4) in `fe/`, initialized with create-next-app; TanStack Query, Zustand, React Hook Form + Zod, MSW mocks. Vanilla `frontend/` remains as reference until UI parity, then archived. Backend is ASP.NET Core + Postgres + Redis (`backend/`), wired in a later phase — the frontend is built UI-first against mocked contracts.

## Users

- Primary: prospective employers / engineers evaluating the repo as a portfolio piece — they skim, run the demo, and judge craft and architecture decisions.
- Secondary: demo "shoppers" (the user themselves and anyone running the stack locally) exercising the full browse → cart → quote → pay flow.
- Third: admin role in the demo store (CRUD, order management, AI audit).

## Product Purpose

ShopMind is an electronics e-commerce platform demonstrating production-grade backend engineering (caching, rate limits, queues, idempotency, race-safe stock) plus an AI shopping assistant that acts through MCP tools. Success = the demo runs end-to-end from a fresh clone and visibly proves both halves.

## Positioning

The assistant operates at the **Assist** trust level: the AI browses, compares, builds the cart, and creates a quote/bill — but **only a human can pay**, on a PSP-hosted page. The capability to charge does not exist in the agent's tool surface, and the UI must make that boundary visible (tool-trail chips, traceable numbers, human-only pay actions). Attribute-question suggestions, not ML recommendations.

## Operating Context

- Runs locally via docker-compose (Postgres, Redis, later RabbitMQ/worker/MCP) or against MSW mocks with zero backend.
- Seeded demo users (`admin@shopmind.local`, `customer@shopmind.local`) and a seeded electronics catalog; mock fixtures transcribe the real seed data.
- Demo modes via `NEXT_PUBLIC_MOCK_MODE=slow|ratelimit|paymentfail` show error/limit states on demand.
- Contracts come from `docs/PLAN.md` and `docs/AI_ASSISTANT_PLAN.md`; design quality gated by Impeccable (`detect`) in CI.

## Capabilities and Constraints

- Screens: storefront (home, listing + filters/sort/pagination, detail with specs table, category), cart, quote/bill review with expiry countdown, FakePsp hosted-pay page, result pages, orders list/detail with cancel, auth + account, AI assistant chat (elicitation cards, tool-trail, quote card, limit/error UX), admin (products/categories/inventory/orders CRUD, AI audit, kill-switch UI).
- Hard UI invariant: no pay action may ever render inside chat or via the assistant — only on `/quote/[id]` and `/psp/[token]`, gated to human sessions.
- Prices/stock/total are computed server-side (or by mock handlers); UI never invents numbers.
- Backend API shape is already specified (JWT, REST, quote/reservation model); UI mock layer must mirror it exactly for a base-URL swap later.
- Mobile-first responsive; dark mode from day one. Single locale, single currency (formatting util keeps later i18n possible).

## Brand Commitments

None binding. "ShopMind" spelling is fixed; everything else (mark, palette, type, voice) is to be defined by this project's design system and then preserved by future work.

## Evidence on Hand

- Real seed catalog + category tree: `backend/Shop.Infrastructure/Seed/SeedData.cs` (transcribe to `fe/src/mocks/catalog.json`).
- Existing vanilla storefront as content/flow reference: `frontend/`.
- Written plans: `docs/PLAN.md`, `docs/AI_ASSISTANT_PLAN.md`, `docs/FRONTEND_PLAN.md`.
- Absences future work must not fabricate: no real customers, testimonials, press, revenue figures, reviews, or live payments. FakePsp and seeded users must read as demo infrastructure, not fake social proof.

## Product Principles

1. **The money boundary is visible** — every screen state that touches payment makes clear the AI quotes and the human pays.
2. **Numbers are traceable** — any price/stock shown (including in AI chat) comes from a contract response, never hardcoded or invented.
3. **Every screen ships all five states** — loading, empty, error, partial (limits), and success.
4. **Mocks are contracts, not toys** — the mock layer mirrors the real API exactly so demo truth survives backend swap.
5. **Craft is gated, not promised** — Impeccable detect + Playwright flows must pass before a feature counts as done.

## Accessibility & Inclusion

Keyboard-complete flows (drawer, chat, admin tables), WCAG AA contrast for both themes, visible focus states, reduced-motion respected; screen-reader-labeled status/stock badges. Portfolio reviewers may test-drive it — a11y is part of the demonstration.
