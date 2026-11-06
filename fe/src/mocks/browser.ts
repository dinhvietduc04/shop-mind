import { setupWorker } from "msw/browser";
import { handlers } from "./handlers";

/**
 * Started from Providers when NEXT_PUBLIC_API_MODE=mock (the default for F0–F7).
 * setupWorker is called lazily inside startMockApi — importing msw/browser is
 * fine in a client component, but instantiating a worker during SSR would throw.
 * `onUnhandledRequest: "bypass"` lets future /api/agent/* routes 404 cleanly
 * until F4 handlers land.
 */
let started: Promise<void> | null = null;

export async function startMockApi() {
  if (typeof window === "undefined") return;
  // StrictMode double-invokes effects; msw v3's interceptor setup is not
  // reentrant ("WebSocket already replaced"), so start exactly once per page.
  started ??= (async () => {
    const worker = setupWorker(...handlers);
    await worker.start({
      onUnhandledFrame: "bypass", // msw v3: frames with no handler pass through silently
      quiet: true,
      serviceWorker: { url: "/mockServiceWorker.js" },
    });
  })().catch((err) => {
    started = null;
    throw err;
  });
  return started;
}
