"use client";

import { useState } from "react";
import { startMockApi } from "./browser";
import { bootSession } from "@/lib/session";
import { releaseHold } from "@/infrastructure/api";

/**
 * Loaded only from Providers through next/dynamic({ssr:false}). msw v3's
 * "msw/browser" export refuses to resolve in the server bundle condition,
 * so this module and its static imports of msw must never enter the SSR graph.
 *
 * It renders nothing and blocks nothing. It starts the worker during its
 * render pass (window-memoized so it survives HMR), restores any session, then
 * releases the ready gate. The app paints immediately while api() holds real
 * requests at the gate (lib/api/readyGate).
 */
export default function MockBootstrapper() {
  useState(() => {
    startMockApi()
      .then(() => releaseHold()) // worker live: release api() calls, including bootSession's own /auth/me
      .then(() => bootSession())
      .catch(() => {
        /* boot failure: the gate releases anyway, so requests reach Next
           unhandled and the app shows 404 error states, never an infinite spinner */
        releaseHold();
      });
    return true;
  });
  return null;
}
