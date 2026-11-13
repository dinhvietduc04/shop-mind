import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { UserDto } from "@/lib/api/contracts/schemas";

const MEMBER: UserDto = {
  id: "u1",
  email: "customer@shopmind.local",
  firstName: "Demo",
  lastName: "Customer",
  phoneNumber: "+1-555-0100",
  role: "Customer",
};

function fakeStorage(initial: Record<string, string> = {}) {
  const m = new Map(Object.entries(initial));
  return {
    getItem: (k: string) => m.get(k) ?? null,
    setItem: (k: string, v: string) => void m.set(k, String(v)),
    removeItem: (k: string) => void m.delete(k),
  };
}

/** Fresh module graph per test, because the first-settled gate is module-scoped. */
async function freshSession() {
  vi.resetModules();
  return import("@/lib/session");
}

const jsonResponse = (body: unknown, ok = true, status = 200) => ({
  ok,
  status,
  headers: new Headers(),
  json: async () => body,
});

beforeEach(() => {
  vi.stubGlobal("window", {});
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("asMember keeps the settle-ordering protocol in one place", () => {
  it("guest boot: no token, no probe, null membership", async () => {
    vi.stubGlobal("localStorage", fakeStorage());
    const fetchSpy = vi.fn();
    vi.stubGlobal("fetch", fetchSpy);

    const { bootSession, asMember } = await freshSession();
    await bootSession();
    expect(await asMember()).toBeNull();
    expect(fetchSpy).not.toHaveBeenCalled(); // restore must not 401-probe a guest
  });

  it("member restoring: a call made BEFORE restore settles still sees the member", async () => {
    // the regression: UI painted, user clicked instantly, raw getState()
    // read a not-yet-restored null and misread the member as a guest.
    vi.stubGlobal("localStorage", fakeStorage({ "shopmind.token": "mock.customer@shopmind.local" }));
    vi.stubGlobal("fetch", vi.fn(async () => jsonResponse(MEMBER)));

    const { bootSession, asMember } = await freshSession();
    const pending = asMember(); // called while ready === false
    await bootSession();
    expect(await pending).toEqual(MEMBER);
  });

  it("dead token: cleared, and the visitor stays a guest", async () => {
    const storage = fakeStorage({ "shopmind.token": "mock.gone@shopmind.local" });
    vi.stubGlobal("localStorage", storage);
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => jsonResponse({ title: "dead" }, false, 401))
    );

    const { bootSession, asMember } = await freshSession();
    await bootSession();
    expect(await asMember()).toBeNull();
    expect(storage.getItem("shopmind.token")).toBeNull();
  });
});
