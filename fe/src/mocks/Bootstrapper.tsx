"use client";

import { useState } from "react";
import { startMockApi } from "./browser";
import { bootSession } from "@/lib/session";
import { releaseHold } from "@/lib/api/readyGate";

/**
 * Loaded ONLY via next/dynamic({ssr:false}) from Providers — msw v3's
 * "msw/browser" export refuses to resolve in the server bundle condition,
 * so this module (and its static imports of msw) must never enter the SSR
 * graph.
 *
 * It renders nothing and blocks nothing: kicks the worker during its render
 * pass (window-memoized so it survives HMR) and restores any session, then
 * releases the ready gate. The app paints immediately; api() parks real
 * requests on the gate meanwhile (lib/api/readyGate) — decoupling first
 * paint from worker startup was worth 1.5–2 s of LCP under throttle.
 */
export default function MockBootstrapper() {
  useState(() => {
    startMockApi()
      .then(() => releaseHold()) // worker live: un-park api() — including bootSession's own /auth/me
      .then(() => bootSession())
      .catch(() => {
        /* boot failure: the gate releases anyway and requests hit Next
           unhandled — honest 404 error states, never an infinite spinner */
        releaseHold();
      });
    return true;
  });
  return null;
}
