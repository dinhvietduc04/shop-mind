# Idempotency as a Redis renewing lease; money paths fail-closed when Redis is down

Agent retries demand dedupe of state-changing requests. We decided the idempotency record is a Redis SET-NX lease (30s) that the holder renews every ~10s, with owner-checked compare-and-set transitions to `Done`; an abandoned lease expires and the key becomes re-acquirable. A fixed hard lease was rejected because a slow quote-create can outlive it, turning a legitimate same-key retry into a *second* quote that the DB constraints won't stop (two quote rows are each legal).

Consequence we accepted knowingly: Redis is a single point of failure on money paths — rate limiting and idempotency **fail-closed** (503 + Retry-After) when Redis is down, while catalog caching stays fail-open. Fine for a small project; DB unique constraints remain the final backstop.
