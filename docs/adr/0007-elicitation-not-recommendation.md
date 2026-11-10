# Elicitation instead of a recommendation system

The original plan had pgvector semantic search (M17) and ML personalization (M18). We decided v1 suggestions are attribute *elicitation*: the agent asks ≤ 3 targeted questions about registry-defined Specs, then filters and ranks deterministically (price, stock, facet-match count) with the explanation built from the filters it used. No embeddings, no behavior-driven personalization — behavior events land via the analytics worker for ops only. The requirement is explainable, demo-able suggestions with no training loop; ML remains explicit backlog, not a silent gap.

Related governance: `Product.Specs` is validated against a per-category specs registry (key, type, unit, label) — the same registry drives facet derivation, rejects filtered-search on unknown keys, and gates seed-data CI validation.
