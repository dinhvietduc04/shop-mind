/**
 * Mock-network boot gate. `api()` parks each request here until MSW is live.
 * A 5 s failsafe releases the gate, so a failed worker cannot hang the UI.
 * The gate is never armed against the real API, where `whenReady()` is already
 * resolved.
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
