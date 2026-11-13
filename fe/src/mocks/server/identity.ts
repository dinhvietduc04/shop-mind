import type { ShippingAddressDto } from "@/infrastructure/api";
import type { UserDto } from "@/lib/api/contracts/schemas";
import { MOCK_USERS } from "../fixtures";
import { registerStore, uuid } from "./snapshot";

/** Identity domain: demo fixture users + accounts registered during the demo.
 *  Registered accounts persist with everything else, so a reload doesn't
 *  orphan their token. */

export interface UserRecord {
  password: string;
  user: UserDto;
}

const customUsers = new Map<string, UserRecord>();
/** Shipping presets, per user. Mock-only until the real API grows an addresses
 *  resource. */
const addresses = new Map<string, ShippingAddressDto>();

registerStore({
  name: "customUsers",
  dump: () => [...customUsers.entries()],
  restore: (v) => (v as [string, UserRecord][]).forEach(([k, rec]) => customUsers.set(k, rec)),
  clear: () => customUsers.clear(), // a reset clears registered accounts too
});

registerStore({
  name: "addresses",
  dump: () => [...addresses.entries()],
  restore: (v) => (v as [string, ShippingAddressDto][]).forEach(([k, a]) => addresses.set(k, a)),
  clear: () => addresses.clear(),
});

export function findUserRecord(email: string): UserRecord | undefined {
  return MOCK_USERS[email] ?? customUsers.get(email);
}

export function registerUser(input: {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string | null;
}): { ok: true; rec: UserRecord } | { ok: false; error: string } {
  const email = input.email.toLowerCase().trim();
  if (findUserRecord(email)) return { ok: false, error: "Email already registered." };
  const rec: UserRecord = {
    password: input.password,
    user: {
      id: `u-${uuid()}`,
      email,
      firstName: input.firstName.trim(),
      lastName: input.lastName.trim(),
      phoneNumber: input.phoneNumber?.trim() || null,
      role: "Customer",
    },
  };
  customUsers.set(email, rec);
  return { ok: true, rec };
}

export function getAddress(userId: string): ShippingAddressDto | null {
  return addresses.get(userId) ?? null;
}

export function setAddress(userId: string, address: ShippingAddressDto): void {
  addresses.set(userId, address);
}

export function userByToken(
  authorization?: string | null
): { id: string; email: string; admin: boolean } | null {
  const token = authorization?.startsWith("Bearer ") ? authorization.slice(7) : null;
  if (!token || !token.startsWith("mock.")) return null;
  const email = token.slice(5);
  const rec = findUserRecord(email);
  if (!rec) return null;
  return { id: rec.user.id, email, admin: rec.user.role === "Admin" };
}
