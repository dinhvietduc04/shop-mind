# Quote is a role of Order, not a separate aggregate

The Assist flow needs an order awaiting human payment with stock reserved. We decided a Quote is an `Order` with `Status=Pending` plus expiry and an attached `PaymentSession` — no `Quote` table. The lifecycle fits natively into the existing `OrderTransitions` rules, and the "one Open Quote per user" invariant is a single-aggregate query; a separate aggregate would duplicate money-adjacent state and give us two idempotency/audit surfaces.

Consequences: legacy V1 Pending orders have no expiry, so they are *not* Open Quotes — the sweeper and replace-policy exclude them and they remain read-only history.
