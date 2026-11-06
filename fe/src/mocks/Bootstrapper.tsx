"use client";

import { useEffect, useState } from "react";
import { startMockApi } from "./browser";
import { ensureDemoSession } from "@/lib/session";

/**
 * Loaded ONLY via next/dynamic({ssr:false}) from Providers — msw v3's
 * "msw/browser" export refuses to resolve in the server bundle condition,
 * so this module (and its static imports of msw) must never enter the SSR
 * graph. Renders its children only after the worker is live and the demo
 * session exists, so the first react-query fetch can't miss the worker.
 */
export default function MockBootstrapper({
  children,
}: {
  children: React.ReactNode;
}) {
  const [ready, setReady] = useState(false);

  useEffect(() => {
    let alive = true;
    (async () => {
      await startMockApi();
      await ensureDemoSession();
      if (alive) setReady(true);
    })();
    return () => {
      alive = false;
    };
  }, []);

  if (!ready) return null;
  return <>{children}</>;
}
