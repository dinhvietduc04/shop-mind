"use client";

import { useEffect } from "react";
import { usePathname, useRouter } from "next/navigation";
import { Skeleton } from "@/components/ui";
import { useSession } from "@/lib/session";
import type { UserDto } from "@/lib/api/contracts/schemas";

/**
 * Client-side route protection. The mock API is the actual authority. Every
 * protected endpoint returns 401 without a token, so guards only choose the
 * UX: a settled guest goes to /login?next=…, except within 3 s of a deliberate
 * sign-out. The mock server never sees a request from a guarded page until the
 * session is settled.
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

/** Role gate for the admin console. Non-admins get an honest 403 card.
 *  Guests get redirected like everyone else. */
export function RequireRole({ role, children }: { role: "Admin"; children: React.ReactNode }) {
  const { user, ready } = useGuardState();
  if (!ready) return <GateFallback />;
  if (!user) return null;
  if (user.role !== role) {
    return (
      <main className="mx-auto max-w-[680px] px-6 py-10">
        <div className="rounded-label border-[1.5px] border-ink bg-card p-5">
          <h1 className="text-lg font-extrabold uppercase tracking-[0.06em]">
            403, {role} only
          </h1>
          <p className="mt-3 text-[14px] leading-relaxed">
            You are signed in as <strong>{user.email}</strong> with role{" "}
            <span className="font-mono text-[12px]">{user.role}</span>. This page
            reads the admin contract; the demo admin account is
            <span className="font-mono text-[12px]"> admin@shopmind.local</span>.
          </p>
        </div>
      </main>
    );
  }
  return <>{children}</>;
}
