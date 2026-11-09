/**
 * Mock-network boot gate — decouples first paint from MSW startup.
 *
 * The old Bootstrapper rendered NOTHING until the worker was live, so cold
 * loads paid chunk-download + worker-registration before any pixel appeared.
 * Now the app renders immediately and api() holds each request on this gate
 * until the worker starts (or a 5 s failsafe releases it so the UI can never
 * hang). F8 note: the real API never calls beginHold() — whenReady() stays a
 * resolved promise and api() costs one microtask.
 */
let readyPromise: Promise<void> = Promise.resolve();
let resolveReady: (() => void) | null = null;
let failsafe: ReturnType<typeof setTimeout> | null = null;

/** Called once by Providers while mock mode is booting (idempotent). */
export function beginHold(): void {
  if (resolveReady) return;
  readyPromise = new Promise<void>((r) => {
    resolveReady = r;
  });
  failsafe = setTimeout(() => releaseHold(), 5000);
}

/** Called when the MSW worker is confirmed live (or boot failed). */
export function releaseHold(): void {
  resolveReady?.();
  resolveReady = null;
  if (failsafe) {
    clearTimeout(failsafe);
    failsafe = null;
  }
}

export const whenReady = () => readyPromise;
