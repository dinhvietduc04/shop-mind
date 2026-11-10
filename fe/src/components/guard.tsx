"use client";

import { useEffect } from "react";
import { usePathname, useRouter } from "next/navigation";
import { Skeleton } from "@/components/ui";
import { useSession } from "@/lib/session";
import type { UserDto } from "@/lib/api/types";

/**
 * Client-side route protection (§5.2). The mock API is the actual authority —
 * every protected endpoint 401s without a token — guards exist only so the UI
 * sends guests to the sign-in page with a return path instead of letting them
 * discover empty error states.
 *
 * ONE bounce rule for every protected surface: a settled guest goes to
 * /login?next=…, unless they signed out deliberately within the last 3 s
 * (they chose to leave; don't yank them back mid-intent). RequireAuth and
 * RequireRole previously duplicated this effect verbatim — the grace window
 * was two knobs pretending to be one.
 */
function useGuardState(): { user: UserDto | null; ready: boolean } {
  const router = useRouter();
  const pathname = usePathname();
  const user = useSession((s) => s.user);
  const ready = useSession((s) => s.ready);
  const signedOutAt = useSession((s) => s.signedOutAt);

  useEffect(() => {
    if (ready && !user && Date.now() - signedOutAt > 3000) {
      router.replace(`/login?next=${encodeURIComponent(pathname)}`);
    }
  }, [ready, user, pathname, router, signedOutAt]);

  return { user, ready };
}

function GateFallback() {
  return <Skeleton className="mx-auto mt-6 h-64 max-w-[680px]" />;
}

export function RequireAuth({ children }: { children: React.ReactNode }) {
  const { user, ready } = useGuardState();
  if (!ready) return <GateFallback />;
  if (!user) return null; // mid-redirect or just signed out
  return <>{children}</>;
}

/** Role gate for the admin console (F6): non-admins get an honest 403 card,
 *  guests get redirected like everyone else. */
export function RequireRole({ role, children }: { role: "Admin"; children: React.ReactNode }) {
  const { user, ready } = useGuardState();
  if (!ready) return <GateFallback />;
  if (!user) return null;
  if (user.role !== role) {
    return (
      <main className="mx-auto max-w-[680px] px-6 py-10">
        <div className="rounded-label border-[1.5px] border-ink bg-card p-5">
          <h1 className="text-lg font-extrabold uppercase tracking-[0.06em]">
            403 — {role} only
          </h1>
          <p className="mt-3 text-[14px] leading-relaxed">
            You are signed in as <strong>{user.email}</strong> with role{" "}
            <span className="font-mono text-[12px]">{user.role}</span>. This surface
            reads the admin contract; the demo admin account is
            <span className="font-mono text-[12px]"> admin@shopmind.local</span>.
          </p>
        </div>
      </main>
    );
  }
  return <>{children}</>;
}
