# ShopMind

E-commerce backend with an AI shopping assistant operating at a defined trust level: the agent browses, compares, builds carts and creates quotes; only a human moves money.

## Language

**Quote**:
An Order playing the role of *awaiting payment* — carrying live stock reservations and an attached PaymentSession. A role of Order, not a separate entity.
_Avoid_: proposal, draft order, checkout

**Open Quote**:
A Quote whose expiry has not elapsed and whose payment outcome has not been decided. At most one Open Quote per user at a time, whoever created it.
_Avoid_: pending order (ambiguous — V1 legacy orders were also "Pending")

**Bill**:
The priced breakdown shown to a human for review from a Quote — lines, subtotal, shipping, total, expiry. What the human reads before paying; not itself a payment artifact.
_Avoid_: invoice, receipt

**Reservation / Commit / Release**:
The three fates of stock set aside for a Quote: *reserve* at quote creation, *commit* on payment success (consumes physical stock), *release* on expiry, payment failure, or cancel (stock returns).

**Payment Session**:
The link between a Quote and one payment attempt on the hosted pay page, identified by a session token. Single-use; dies with the Quote.

**Hosted Pay Page**:
The PSP-served page where money actually moves — FakePsp in dev, Stripe test mode in CI/staging. Opened by the human only; an agent may view the Bill there but cannot act on it.

**Origin**:
Who created a Quote — *human* (via the shop UI) or *agent*. An agent may replace only agent-origin Quotes; a human's Open Quote blocks agent quote creation rather than being cancelled by it.

**Trust Level**:
The stage of the agent's spending authority: **Assist** (may quote, never pay), **Delegated** (may pay within a human's Mandate), **Autonomous** (not offered, by policy).

**Mandate**:
A pre-authorization a human enrolls at Delegated level — merchant scope, categories, amount cap, expiry. Schema seam only in v1; never enforced.

**Risk Class**:
A tool's authority tier: `READ`, `WRITE`, `QUOTE`, `NEVER`. A `NEVER` tool is absent from the manifest entirely — capability missing, not capability denied.

**Agent Scope**:
The authority of a `scope=agent` token: MCP tools for one user only. Rejected at every REST endpoint, and cannot confirm payment anywhere.

**Elicitation**:
The assistant asking a few targeted attribute questions before filtering or recommending. Attribute-driven, deterministic, explicitly not a recommendation system.

**Spec**:
A canonical product attribute defined by the per-category specs registry (e.g. `ram_gb`, `cpu`). Specs outside the registry do not exist for facets or filtered search.

**Payment Anomaly**:
A provider event that conflicts with an Order's terminal state (e.g. a late *succeeded* after *Cancelled*). Never applied automatically; recorded and escalated for a human refund/cancel decision.
