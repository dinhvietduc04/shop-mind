import { setupWorker } from "msw/browser";
import { handlers } from "./handlers";
import { initServer } from "./server/snapshot";

/**
 * Started from Providers when NEXT_PUBLIC_API_MODE=mock.
 * setupWorker is called lazily inside startMockApi. Importing msw/browser is
 * fine in a client component, but instantiating a worker during SSR would throw.
 * `onUnhandledFrame: "bypass"` lets unhandled /api routes fall through instead
 * of failing the worker.
 */
type MswWindow = Window & { __shopmindMsw?: Promise<void> };

export async function startMockApi() {
  if (typeof window === "undefined") return;
  const w = window as MswWindow;
  // The memo is stored on window, not module scope: Fast Refresh re-instantiates
  // this module but the interceptor globals patched by the previous instance
  // persist for the whole page. msw v3's setup is not reentrant, so starting a
  // second worker throws "WebSocket already replaced".
  w.__shopmindMsw ??= (async () => {
    initServer(); // deferred snapshot load. Domain modules never read storage at import
    const worker = setupWorker(...handlers);
    try {
      await worker.start({
        onUnhandledFrame: "bypass", // msw v3: frames with no handler pass through silently
        quiet: true,
        serviceWorker: { url: "/mockServiceWorker.js" },
      });
    } catch (err) {
      // A survivor interceptor from a pre-HMR module instance means the mock
      // network is already live. That is success, not a failure.
      if (err instanceof Error && /already replaced/i.test(err.message)) return;
      throw err;
    }
  })().catch((err) => {
    (w as MswWindow).__shopmindMsw = undefined; // so a rejection doesn't block later attempts
    throw err;
  });
  return w.__shopmindMsw;
}
