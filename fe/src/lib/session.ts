"use client";

import { create } from "zustand";
import { api, setToken } from "./api/client";
import type { UserDto } from "./api/types";

interface SessionState {
  user: UserDto | null;
  ready: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
  restore: () => Promise<void>;
}

export const useSession = create<SessionState>((set) => ({
  user: null,
  ready: false,
  login: async (email, password) => {
    const res = await api<{ token: string; user: UserDto }>("/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    });
    setToken(res.token);
    set({ user: res.user, ready: true });
  },
  logout: () => {
    setToken(null);
    set({ user: null });
  },
  restore: async () => {
    try {
      const user = await api<UserDto>("/auth/me");
      set({ user, ready: true });
    } catch {
      set({ user: null, ready: true });
    }
  },
}));

/** Mock-demo convenience: auto-login the seeded customer when no session. */
export async function ensureDemoSession() {
  const t = typeof window !== "undefined" ? localStorage.getItem("shopmind.token") : null;
  if (t) {
    await useSession.getState().restore();
    return;
  }
  await useSession.getState().login("customer@shopmind.local", "Customer123!");
}
