import { setupWorker } from "msw/browser";
import { handlers } from "./handlers";
import { initServer } from "./db";

/**
 * Started from Providers when NEXT_PUBLIC_API_MODE=mock (the default for F0–F7).
 * setupWorker is called lazily inside startMockApi — importing msw/browser is
 * fine in a client component, but instantiating a worker during SSR would throw.
 * `onUnhandledFrame: "bypass"` lets future /api/agent/* routes 404 cleanly
 * until F4 handlers land.
 */
type MswWindow = Window & { __shopmindMsw?: Promise<void> };

export async function startMockApi() {
  if (typeof window === "undefined") return;
  const w = window as MswWindow;
  // The memo lives on window, not module scope: Fast Refresh re-instantiates
  // this module but the interceptor globals patched by the previous instance
  // persist for the whole page. msw v3's setup is not reentrant — starting a
  // second worker throws "WebSocket already replaced".
  w.__shopmindMsw ??= (async () => {
    initServer(); // deferred snapshot load (C7) — domain modules never read storage at import
    const worker = setupWorker(...handlers);
    try {
      await worker.start({
        onUnhandledFrame: "bypass", // msw v3: frames with no handler pass through silently
        quiet: true,
        serviceWorker: { url: "/mockServiceWorker.js" },
      });
    } catch (err) {
      // A survivor interceptor from a pre-HMR module instance means the mock
      // network is already live — that is success for us, not a failure.
      if (err instanceof Error && /already replaced/i.test(err.message)) return;
      throw err;
    }
  })().catch((err) => {
    (w as MswWindow).__shopmindMsw = undefined; // don't poison later attempts with a rejection
    throw err;
  });
  return w.__shopmindMsw;
}
