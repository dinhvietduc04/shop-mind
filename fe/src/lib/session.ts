"use client";

import { create } from "zustand";
import { api, setToken, hasToken } from "@/infrastructure/api";
import { queryClient } from "@/infrastructure/query/queryClient";
import type { AuthResponse, LoginRequest, RegisterRequest, UserDto } from "@/lib/api/contracts/schemas";

/**
 * Session truth is the bearer token in localStorage; this store mirrors the
 * server's view (/auth/me) for the UI. Boot never signs anybody in.
 * Guests browse the public REST endpoints (catalog, facets, product sheets)
 * and hit the login wall exactly where the real API returns 401 (cart, quote,
 * orders, chat). The /login page carries one-click demo identities.
 */
interface SessionState {
  user: UserDto | null;
  ready: boolean;
  /** epoch ms of the last deliberate sign-out, so guards do not send the user
   *  back to /login right after they chose to leave. */
  signedOutAt: number;
  login: (email: string, password: string) => Promise<void>;
  register: (input: RegisterRequest) => Promise<void>;
  logout: () => void;
  restore: () => Promise<void>;
}

/* Boot decoupling means pages paint before restore() resolves, so any
   auth-dependent DECISION (chat guest nudge, add-to-cart redirect) must wait
   for the first settled session rather than read a not-yet-restored null.
   Resolves once on the first ready transition; never re-arms. */
let resolveFirstReady!: () => void;
const firstReady = new Promise<void>((r) => (resolveFirstReady = r));
export const whenSessionReady = () => firstReady;
let readySeen = false;
function markReady() {
  if (!readySeen) {
    readySeen = true;
    resolveFirstReady();
  }
}

export const useSession = create<SessionState>((set) => ({
  user: null,
  ready: false,
  signedOutAt: 0,

  login: async (email, password) => {
    const res = await api<AuthResponse>("/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password } satisfies LoginRequest),
    });
    setToken(res.token);
    queryClient.clear(); // every cached read was made as the previous identity
    set({ user: res.user, ready: true });
    markReady();
  },

  register: async (input) => {
    const res = await api<AuthResponse>("/auth/register", {
      method: "POST",
      body: JSON.stringify(input),
      idempotencyKey: crypto.randomUUID?.(),
    });
    setToken(res.token);
    queryClient.clear();
    set({ user: res.user, ready: true });
    markReady();
  },

  logout: () => {
    setToken(null);
    queryClient.clear();
    set({ user: null, signedOutAt: Date.now() });
  },

  restore: async () => {
    try {
      if (!hasToken()) {
        // no token, nothing to probe. Don't fire a 401 just to learn "guest".
        set({ user: null, ready: true });
        return;
      }
      const user = await api<UserDto>("/auth/me");
      set({ user, ready: true });
    } catch {
      setToken(null); // dead token from an older demo. Clean it and stay guest.
      set({ user: null, ready: true });
    }
  },
}));

/** Boot: restore an existing session if there is one. Never auto-login.
 *  markReady() runs in the finally so the gate opens even if restore throws
 *  before its own try (e.g. storage unavailable). */
export async function bootSession() {
  try {
    await useSession.getState().restore();
  } finally {
    markReady();
  }
}

/**
 * The one way to answer "is this visitor a member, and which one?". It waits
 * for the first settled session, then returns the user. Callers redirect or
 * prompt on null.
 */
export async function asMember(): Promise<UserDto | null> {
  if (!useSession.getState().ready) await whenSessionReady();
  return useSession.getState().user;
}
