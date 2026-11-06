# The hosted pay page is authenticated by a capability token, not a session

The pay page must be openable from a link an agent produced, but ShopMind has no cookie/session infrastructure (JWT bearer only). We decided the 128-bit session token itself authorizes *viewing* the Bill (read-only, single-use, expires with the Quote); the *confirm* endpoint additionally requires the human's own bearer token to match the Payment Session's owner, and rejects `scope=agent` tokens. Building cookie sessions (CSRF, session store, logout semantics) for one page was rejected — and Stripe's real hosted page wouldn't carry our cookies anyway.

Consequences: pay URLs are credentials — exclude `/pay/*` from request logging so tokens don't leak into logs, and treat an agent reading the bill page as acceptable (it's an invoice view).
